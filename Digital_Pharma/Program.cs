using DB.DBB;
using Digital_Pharma.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("DigitalPharma.FieldEncryption.v1");
var configuredKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(configuredKeyPath))
{
    var keyDirectory = new DirectoryInfo(Path.GetFullPath(configuredKeyPath));
    keyDirectory.Create();
    dataProtection.PersistKeysToFileSystem(keyDirectory);
}
builder.Services.AddSingleton<FieldEncryptionService>();
builder.Services.AddScoped<SensitiveDataMigrator>();
builder.Services.AddScoped<AccountPasswordService>();
builder.Services.AddScoped<PasswordResetEmailService>();
builder.Services.AddScoped<HealthReviewEngine>();
builder.Services.AddScoped<HealthPageService>();
builder.Services.AddScoped<PatientAlertService>();
builder.Services.AddHttpClient<IDrugLabelClient,DrugLabelClient>(client=> {client.Timeout=TimeSpan.FromSeconds(12);client.MaxResponseContentBufferSize=8*1024*1024;});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Auth";
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        options.Cookie.Name = "DigitalPharma.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<PharmaDbContext>();
            var id = int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
            var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id);
            var cookieVersion = int.TryParse(context.Principal?.FindFirstValue("session_version"), out var version) ? version : -1;
            if (account == null || !account.EmailConfirmed || account.LockoutEnd > DateTimeOffset.UtcNow || cookieVersion != account.SessionVersion)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }
            // Rebuild roles from trusted database state on every request, including existing cookies.
            context.ReplacePrincipal(await AccountRoles.PrincipalAsync(account, db));
        };
    });
builder.Services.AddAuthorization(options => options.FallbackPolicy =
    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("passwordRecovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6, Window = TimeSpan.FromMinutes(15), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("aiChat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("محاولات كثيرة. يرجى الانتظار دقيقة ثم المحاولة مجدداً.", cancellationToken);
    };
});

// DbContext
builder.Services.AddDbContext<PharmaDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DigitalPharmaDB"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)
    ));

// Services
builder.Services.AddScoped<OpenFdaService>();
builder.Services.AddHttpClient<OpenFdaService>();

builder.Services.AddHttpClient<GeminiAiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
    client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
});

var app = builder.Build();
if (args.Contains("--encrypt-sensitive-data"))
{
    if (!args.Contains("--confirm"))
    {
        Console.WriteLine("ENCRYPTION_NOT_STARTED: rerun with --encrypt-sensitive-data --confirm after taking a database and key-ring backup.");
        Environment.ExitCode = 2;
        return;
    }
    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<SensitiveDataMigrator>();
    var changed = await migrator.EncryptLegacyValuesAsync();
    Console.WriteLine($"SENSITIVE_DATA_ENCRYPTED: values={changed}");
    return;
}
if (args.Contains("--test-gemini"))
{
    using var scope = app.Services.CreateScope();
    var gemini = scope.ServiceProvider.GetRequiredService<GeminiAiService>();
    var result = await gemini.TestConnectionAsync();
    Console.WriteLine(result.Success
        ? $"GEMINI_CONNECTION_OK: model={result.Model}"
        : $"GEMINI_CONNECTION_FAILED: {result.Message}");
    Environment.ExitCode = result.Success ? 0 : 2;
    return;
}

if (args.Contains("--test-gemini-drug"))
{
    using var scope = app.Services.CreateScope();
    var gemini = scope.ServiceProvider.GetRequiredService<GeminiAiService>();
    var json = await gemini.GetDrugInfoAsync("Omeprazole", null, null);
    using var document = System.Text.Json.JsonDocument.Parse(json);
    var root = document.RootElement;
    var valid = !root.TryGetProperty("__error", out _)
        && root.TryGetProperty("purpose", out var purpose)
        && !string.IsNullOrWhiteSpace(purpose.GetString());
    Console.WriteLine(valid
        ? "GEMINI_DRUG_INFO_OK: Omeprazole"
        : "GEMINI_DRUG_INFO_FAILED: response did not contain the required fields.");
    Environment.ExitCode = valid ? 0 : 2;
    return;
}

if (args.Contains("--grant-admin"))
{
    var position = Array.IndexOf(args, "--grant-admin");
    if (position + 1 >= args.Length) throw new ArgumentException("Specify an existing account email.");
    using var scope=app.Services.CreateScope();
    var db=scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
    var email=args[position+1].Trim().ToLowerInvariant();
    var matches=await db.Users.Where(u=>u.Email.ToLower()==email).ToListAsync();
    if(matches.Count!=1) { Console.WriteLine("ADMIN_NOT_ASSIGNED: create the intended account first, then rerun this local command."); Environment.ExitCode=2; return; }
    matches[0].IsAdmin=true;
    await db.SaveChangesAsync();
    Console.WriteLine("ADMIN_ASSIGNED: existing account updated.");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Auth}/{id?}");

// Anonymous endpoint used by the container platform to verify that the process is healthy.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
