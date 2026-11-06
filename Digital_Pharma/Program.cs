using DB.DBB;
using Digital_Pharma.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// DbContext
builder.Services.AddDbContext<PharmaDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DigitalPharmaDB"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)
    ));

// Services
builder.Services.AddScoped<OpenFdaService>();
builder.Services.AddHttpClient<OpenFdaService>();

builder.Services.AddScoped<ClaudeAiService>();
builder.Services.AddHttpClient<ClaudeAiService>();

builder.Services.AddScoped<GeminiAiService>();
builder.Services.AddHttpClient<GeminiAiService>();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Auth}/{id?}");

app.Run();
