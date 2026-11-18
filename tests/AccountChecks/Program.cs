using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var project = Path.Combine(root, "Digital_Pharma");
var passwords = new AccountPasswordService();
var testKeyPath = Path.Combine(root, ".local", "test-data-protection-keys");
Directory.CreateDirectory(testKeyPath);
var encryptionServices = new ServiceCollection();
encryptionServices.AddDataProtection()
    .SetApplicationName("DigitalPharma.FieldEncryption.v1")
    .PersistKeysToFileSystem(new DirectoryInfo(testKeyPath));
using var encryptionProvider = encryptionServices.BuildServiceProvider();
var fieldEncryption = new FieldEncryptionService(encryptionProvider.GetRequiredService<IDataProtectionProvider>());
PharmaDbContext Database(DbContextOptions<PharmaDbContext> options) => new(options, fieldEncryption);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
var publicViewText=string.Join('\n',Directory.GetFiles(Path.Combine(project,"Views"),"*.cshtml",SearchOption.AllDirectories).Select(File.ReadAllText));
Check(!publicViewText.Contains("Gemini",StringComparison.OrdinalIgnoreCase)&&!publicViewText.Contains("جيمناي"),"Public pages do not expose the AI provider name");
var protectedSample = fieldEncryption.Encrypt("بيانات صحية تجريبية");
Check(protectedSample.StartsWith(FieldEncryptionService.Prefix) && !protectedSample.Contains("صحية") &&
      fieldEncryption.Decrypt(protectedSample) == "بيانات صحية تجريبية", "Sensitive fields use authenticated encryption and round-trip safely");
var user = new User();
const string password = "Test-only!Password42";
user.PasswordHash = passwords.Hash(user, password);
Check(user.PasswordHash != passwords.Hash(user, password), "Passwords have independent salts");
Check(passwords.Verify(user, password) != PasswordVerificationResult.Failed, "Correct modern password accepted");
Check(passwords.Verify(user, "wrong") == PasswordVerificationResult.Failed, "Wrong modern password rejected");
user.PasswordHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
Check(passwords.Verify(user, password) == PasswordVerificationResult.SuccessRehashNeeded, "Legacy digest requests upgrade");
Check(passwords.Verify(user, "wrong") == PasswordVerificationResult.Failed, "Wrong legacy password rejected");
user.PasswordHash = "corrupt-hash";
Check(passwords.Verify(user, password) == PasswordVerificationResult.Failed, "Malformed hash rejected");
var unavailableEvidence=InteractionEvidence.Assess(new ClaudeAnalysisResult {Success=false},null,null);
Check(unavailableEvidence.Severity=="unknown" && unavailableEvidence.Status=="unavailable","Unavailable interaction sources fail closed");
var labelWithoutInteractions=new OpenFdaResult {OpenFda=new OpenFdaDetails {GenericName=["Synthetic"]}};
var partialSafeEvidence=InteractionEvidence.Assess(new ClaudeAnalysisResult {Success=true,Severity="safe"},labelWithoutInteractions,labelWithoutInteractions);
Check(partialSafeEvidence.Severity=="unknown" && partialSafeEvidence.Status=="partial","Missing FDA interaction sections never become safe");
var fullLabel=new OpenFdaResult {OpenFda=new OpenFdaDetails {GenericName=["Synthetic"]},DrugInteractions=["Synthetic reference text"]};
var completeSafeEvidence=InteractionEvidence.Assess(new ClaudeAnalysisResult {Success=true,Severity="safe"},fullLabel,fullLabel);
Check(completeSafeEvidence.Severity=="safe" && completeSafeEvidence.Status=="complete","Complete evidence can retain a safe classification");
var clinicalLabel=new DrugLabel {
    Id=Guid.NewGuid().ToString(),SetId=Guid.NewGuid().ToString(),EffectiveTime="20261116",
    Names=new(){Substances=["SYNTHETIC INGREDIENT"]},
    Contraindications=["Do not use in severe renal impairment."],
    WarningsAndCautions=["Use caution in patients with heart failure."]
};
var clinicalMedication=new UserMedication {DrugName="Synthetic clinical product",LabelJson=JsonSerializer.Serialize(clinicalLabel),LabelRetrievedAt=DateTimeOffset.UtcNow};
var clinicalReview=new HealthReviewEngine().Evaluate(
    new User {ProfileCompleted=true,Gender="female",IsPregnant=false},
    [new PatientCondition {Code="severe_renal"},new PatientCondition {Code="heart_failure"}],
    [new PatientAllergy {Name="Synthetic allergy",SubstanceKey="SYNTHETIC INGREDIENT",Reaction="Synthetic",ReportedSeverity="severe"}],
    [clinicalMedication]);
Check(clinicalReview.Findings.Any(f=>f.Kind=="label-context" && f.Priority=="urgent" && f.Related.Contains("كلوي شديد")),"Contraindication section creates an urgent condition review");
Check(clinicalReview.Findings.Any(f=>f.Kind=="label-context" && f.Priority=="review" && f.Related.Contains("قصور القلب")),"Warnings section remains a non-diagnostic review signal");
Check(clinicalReview.Findings.Any(f=>f.Kind=="allergy" && f.Priority=="urgent"),"Exact severe allergy match receives urgent review priority");
Check(InteractionSeverity.Normalize("SERIOUS")=="serious" && InteractionSeverity.Normalize("invented")=="unknown","Severity normalization accepts only the shared five-level scale");
Check(InteractionSeverity.Overall(["safe","unknown"])=="unknown" && InteractionSeverity.Overall(["critical","unknown"])=="critical","Overall severity fails closed without hiding established critical risk");
var pregnancyReview=new HealthReviewEngine().Evaluate(new User{ProfileCompleted=true,Gender="female",IsPregnant=true},[],[],[new UserMedication{DrugName="Synthetic pregnancy product",LabelJson=JsonSerializer.Serialize(new DrugLabel{Names=new(){Substances=["SYNTHETIC"]},Pregnancy=["Synthetic pregnancy section"]}),LabelRetrievedAt=DateTimeOffset.UtcNow}]);
Check(pregnancyReview.Findings.Any(f=>f.Kind=="pregnancy"&&f.Priority=="review"),"Recorded pregnancy and an available label section create an explicit professional-review finding");
if (args.Contains("--passwords-only")) return;

if(args.Contains("--live-label")) {
    var labels=new DrugLabelClient(new HttpClient {Timeout=TimeSpan.FromSeconds(30)});
    var found=await labels.SearchAsync("metformin");
    Check(found.Error==null && found.Labels.Count>0,"Live FDA search returns selectable products");
    var selected=found.Labels.First(l=>l.Names.Substances.Length>0);
    var exact=await labels.ByIdAsync(selected.Id);
    Check(exact.Labels.Count==1 && exact.Labels[0].Id==selected.Id && exact.Labels[0].Names.Substances.Length>0,"Live selected label has exact identity and substances");
    return;
}

if(args.Contains("--diagnose-account")) {
    var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().SetBasePath(project).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json").AddEnvironmentVariables().Build();
    var connection=config.GetConnectionString("DigitalPharmaDB");
    await using var db=Database(new DbContextOptionsBuilder<PharmaDbContext>().UseSqlServer(connection).Options);
    var pos=Array.IndexOf(args,"--diagnose-account");
    var email=pos+1<args.Length?args[pos+1].Trim().ToLowerInvariant():"";
    Console.WriteLine("Development database reachable: "+await db.Database.CanConnectAsync());
    var accounts=await db.Users.AsNoTracking().Where(u=>u.Email.Trim().ToLower()==email).ToListAsync();
    Console.WriteLine("Matching accounts: "+accounts.Count);
    foreach(var account in accounts) {
        string kind="missing";
        if(!string.IsNullOrEmpty(account.PasswordHash))try {var bytes=Convert.FromBase64String(account.PasswordHash);kind=bytes.Length==32?"legacy-sha256":bytes.Length>=13 && bytes[0] is 0 or 1?"identity-format":"unrecognized";}catch(FormatException){kind="invalid-base64";}
        Console.WriteLine($"Credential format: {kind}; locked: {account.LockoutEnd>DateTimeOffset.UtcNow}; legacy plaintext present: {!string.IsNullOrEmpty(account.Password)}; email surrounding whitespace: {account.Email!=account.Email.Trim()}");
    }
    return;
}

if (args.Contains("--probe"))
{
    using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "appsettings.json")));
    var value = config.RootElement.GetProperty("ConnectionStrings").GetProperty("DigitalPharmaDB").GetString();
    var developmentPath = Path.Combine(project, "appsettings.Development.json");
    using var devConfig = JsonDocument.Parse(File.ReadAllText(developmentPath));
    if (devConfig.RootElement.TryGetProperty("ConnectionStrings", out var devConnections) &&
        devConnections.TryGetProperty("DigitalPharmaDB", out var devValue)) value = devValue.GetString();
    // Report only connectivity, never configuration or stored patient/account data.
    try
    {
        var connection = new SqlConnectionStringBuilder(value) { ConnectTimeout = 5 };
        await using var db = Database(new DbContextOptionsBuilder<PharmaDbContext>().UseSqlServer(connection.ConnectionString).Options);
        Check(await db.Database.CanConnectAsync(), "Configured database reachable");
        await db.Users.Select(u => u.Id).Take(1).ToListAsync();
        Console.WriteLine("PASS: Existing Users schema query succeeded (no data displayed)");
    }
    catch (Exception e) { Console.WriteLine("Database probe failed: " + e.GetType().Name); Environment.ExitCode = 1; }
    return;
}

if (args.Contains("--prepare-development"))
{
    // Explicitly scoped to a separate local development database, never the configured production database.
    const string developmentConnection = @"Server=(localdb)\MSSQLLocalDB;Database=DigitalPharma_Development;Trusted_Connection=True;TrustServerCertificate=True";
    await using var dev = Database(new DbContextOptionsBuilder<PharmaDbContext>().UseSqlServer(developmentConnection).Options);
    if(await dev.Database.CanConnectAsync())
    {
        var backupDirectory=Path.Combine(root,".local","backups"); Directory.CreateDirectory(backupDirectory);
        var backupPath=Path.Combine(backupDirectory,"DigitalPharma_Development_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+".bak");
        await dev.Database.ExecuteSqlRawAsync("BACKUP DATABASE [DigitalPharma_Development] TO DISK = {0} WITH COPY_ONLY, INIT", backupPath);
        Console.WriteLine("Development backup saved before migrations.");
    }
    await dev.Database.MigrateAsync();
    Console.WriteLine("PASS: Development database migrations applied");
    return;
}

var name = "DigitalPharma_AccountChecks_" + Guid.NewGuid().ToString("N");
var cs = new SqlConnectionStringBuilder
{
    DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = name,
    IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 15
}.ConnectionString;
var mailPickup = Path.Combine(root, ".local", "test-mail-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(mailPickup);
await using var database = Database(new DbContextOptionsBuilder<PharmaDbContext>().UseSqlServer(cs).Options);
Process? server = null;
var logs = new StringBuilder();
var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var address = $"http://127.0.0.1:{port}";
HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
async Task<string> Token(HttpClient client, string path = "/Home/Auth")
{
    var html = await client.GetStringAsync(path);
    var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
    if (!match.Success) throw new Exception("No antiforgery token on " + path);
    return WebUtility.HtmlDecode(match.Groups[1].Value);
}
async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string> values, string? token = null)
{
    values["__RequestVerificationToken"] = token ?? await Token(client);
    return await client.PostAsync(path, new FormUrlEncodedContent(values));
}
Dictionary<string,string> Signup(string email) => new()
{
    ["userName"] = "Test Patient", ["email"] = email, ["password"] = password,
    ["phoneNumber"] = "", ["dateOfBirth"] = "2000-01-01"
};
string DecodeMail(string mailText)
{
    if(mailText.Contains("Content-Transfer-Encoding: base64",StringComparison.OrdinalIgnoreCase)){
      var bodyStart=mailText.IndexOf("\r\n\r\n",StringComparison.Ordinal);
      if(bodyStart>=0){var encodedBody=Regex.Replace(mailText[(bodyStart+4)..],@"\s","");try{mailText=Encoding.UTF8.GetString(Convert.FromBase64String(encodedBody));}catch(FormatException){}}
    }
    return WebUtility.HtmlDecode(mailText.Replace("=\r\n","").Replace("=\n","").Replace("=3D","=",StringComparison.OrdinalIgnoreCase).Replace("=3F","?",StringComparison.OrdinalIgnoreCase));
}
async Task<HttpResponseMessage> ConfirmLatest(HttpClient client)
{
    string? token=null;
    for(var i=0;i<30 && token==null;i++){
      foreach(var file in Directory.GetFiles(mailPickup).OrderByDescending(path=>File.GetLastWriteTimeUtc(path))){
        var match=Regex.Match(DecodeMail(await File.ReadAllTextAsync(file)),@"ConfirmEmail\?token=([A-Za-z0-9_-]+)");
        if(match.Success){token=match.Groups[1].Value;break;}
      }
      if(token==null)await Task.Delay(100);
    }
    if(token==null)throw new Exception("Confirmation email token was not generated.");
    return await client.GetAsync("/Home/ConfirmEmail?token="+token);
}
try
{
    await database.Database.MigrateAsync();
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    var appConfiguration = args.Contains("--release-app") ? "Release" : "Debug";
    start.ArgumentList.Add(Path.Combine(project, $"bin/{appConfiguration}/net9.0/Digital_Pharma.dll"));
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ASPNETCORE_URLS"] = address;
    start.Environment["Gemini__ApiKey"] = ""; // Deterministic unavailable-source UI; never call paid AI in account checks.
    start.Environment["ConnectionStrings__DigitalPharmaDB"] = cs;
    start.Environment["DataProtection__KeyPath"] = testKeyPath;
    start.Environment["App__PublicBaseUrl"] = address;
    start.Environment["Smtp__PickupDirectory"] = mailPickup;
    start.Environment["Smtp__From"] = "no-reply@example.test";
    server = new Process { StartInfo = start };
    server.OutputDataReceived += (_, e) => { if (e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var anonymous = Client();
    bool ready = false;
    for (int i = 0; i < 80; i++)
    {
        try { ready = (await anonymous.GetAsync("/Home/Auth")).IsSuccessStatusCode; if (ready) break; }
        catch (HttpRequestException) { }
        await Task.Delay(250);
    }
    Check(ready, "Application starts and login page renders");
    foreach (var path in new[] { "/Home/Account", "/Home/MyMedications", "/Home/History", "/Home/DoctorInfo", "/Admin", "/Doctor/Status", "/Home/GetMedicationsJson", "/Home/DrugSuggest?q=ab" })
        Check((await anonymous.GetAsync(path)).StatusCode == HttpStatusCode.Redirect, "Anonymous access blocked: " + path);
    Check((await anonymous.PostAsync("/Home/SignUp", new FormUrlEncodedContent(Signup("csrf@example.test")))).StatusCode == HttpStatusCode.BadRequest, "Signup without antiforgery token blocked");
    var bad = Signup("invalid-email");
    await Post(anonymous, "/Home/SignUp", bad);
    bad = Signup("weak@example.test"); bad["password"] = "short";
    await Post(anonymous, "/Home/SignUp", bad);
    bad = Signup("future@example.test"); bad["dateOfBirth"] = "2099-01-01";
    await Post(anonymous, "/Home/SignUp", bad);
    Check(!await database.Users.AnyAsync(), "Invalid email, short password and future birth rejected");
    using var alice = Client();
    var registered = await Post(alice, "/Home/SignUp", Signup(" ALICE@example.test "));
    Check(registered.Headers.Location?.OriginalString?.StartsWith("/Home/VerifyEmailNotice")==true && !registered.Headers.Contains("Set-Cookie"), "Valid registration waits for email confirmation");
    var aliceRow = await database.Users.SingleAsync();
    Check(aliceRow.Email == "alice@example.test" && aliceRow.Password == "" && passwords.Verify(aliceRow, password) != PasswordVerificationResult.Failed, "Normalized email and protected password saved without plaintext");
    using var unconfirmedLogin=Client();await Post(unconfirmedLogin,"/Home/Login",new(){["email"]="alice@example.test",["password"]=password});
    Check((await unconfirmedLogin.GetAsync("/Home/Account")).StatusCode==HttpStatusCode.Redirect,"Unconfirmed account cannot sign in");
    var confirmed=await ConfirmLatest(alice);
    Check(confirmed.Headers.Location?.OriginalString=="/Home/ProfileSetup","One-time email link confirms account and signs in");
    await database.Entry(aliceRow).ReloadAsync();Check(aliceRow.EmailConfirmed,"Email confirmation state persists");
    var cookies = confirmed.Headers.GetValues("Set-Cookie");
    Check(cookies.Any(c => c.StartsWith("DigitalPharma.Auth=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "Authentication cookie is HttpOnly");
    Check((await alice.GetAsync("/Home/Account")).IsSuccessStatusCode, "Signed-in account page renders");
    await Post(anonymous, "/Home/SignUp", Signup("Alice@EXAMPLE.test"));
    Check(await database.Users.CountAsync() == 1, "Duplicate email rejected ignoring case");
    using var bob = Client();
    await Post(bob, "/Home/SignUp", Signup("bob@example.test"));await ConfirmLatest(bob);
    var aliceToken = await Token(alice, "/Home/MyMedications");
    await Post(alice, "/Home/AddMedication", new() { ["drugName"]="TestMedicine", ["notes"]="test" }, aliceToken);
    var medication = await database.UserMedications.SingleAsync();
    var bobToken = await Token(bob, "/Home/MyMedications");
    await Post(bob, "/Home/DeleteMedication", new() { ["id"]=medication.Id.ToString() }, bobToken);
    Check(await database.UserMedications.AnyAsync(m => m.Id == medication.Id), "Other patient cannot delete owner's medication");
    Check(!(await bob.GetStringAsync("/Home/GetMedicationsJson")).Contains("TestMedicine"), "Other patient's medication is absent from JSON");
    Check((await alice.PostAsync("/Home/DeleteMedication", new FormUrlEncodedContent(new Dictionary<string,string> { ["id"]=medication.Id.ToString() }))).StatusCode == HttpStatusCode.BadRequest, "Medication mutation without antiforgery token blocked");
    Check((await bob.GetAsync("/Admin/Index")).StatusCode==HttpStatusCode.Forbidden,"Patient cannot access administration");
    Check((await bob.GetAsync("/Doctor/Dashboard")).StatusCode==HttpStatusCode.Forbidden,"Patient cannot access doctor dashboard");
    Check((await anonymous.GetAsync("/Health/Index")).StatusCode==HttpStatusCode.Redirect,"Health profile requires login");
    var healthHtml=WebUtility.HtmlDecode(await alice.GetStringAsync("/Health/Index"));
    Check(healthHtml.Contains("<h1>صحتي</h1>") && !healthHtml.Contains("مترابط") && !healthHtml.Contains("العلاقات"),"Health page uses the requested simple title");
    Check(!healthHtml.Contains("التقدم كطبيب") && !healthHtml.Contains("href=\"/Doctor/Apply\""),"Patient navigation has no doctor application link");
    var pregnancyHtml=await alice.GetStringAsync("/Home/PregnancyDrugs");
    Check(pregnancyHtml.Contains("data-state=\"unavailable\"") && !pregnancyHtml.Contains("fa-spin"),"Unavailable pregnancy list ends with recovery actions instead of endless spinner");
    var nav=Regex.Match(healthHtml,"<nav[\\s\\S]*?</nav>").Value;
    Check(!nav.Contains("/Home/History") && nav.Contains("user-badge account-link") && nav.Contains("/Home/Account"),"Patient navigation moves history inside checker and links account badge");
    Check(nav.Contains("فحص الأدوية") && nav.Contains("البحث عن دواء") && nav.Contains("أدويتي") && nav.Contains("صحتي") && nav.Contains("طبيبي") && !nav.Contains("أدوية اليوم") && !nav.Contains("الطبيب المتابع"),"Patient navigation uses the approved five labels");
    Check(nav.IndexOf("صحتي",StringComparison.Ordinal)<nav.IndexOf("أدويتي",StringComparison.Ordinal) && !nav.Contains("account-popover"),"Health precedes Medications and the account badge links directly");
    var medicationsHtml=await alice.GetStringAsync("/Home/MyMedications");
    Check(medicationsHtml.Contains("جرعات اليوم") && medicationsHtml.Contains("قائمة أدويتي") && medicationsHtml.Contains("medications-tabs"),"Medications page exposes the two approved tabs");
    var medicationTabs=Regex.Match(medicationsHtml,"<nav class=\"medications-tabs\"[\\s\\S]*?</nav>").Value;
    Check(medicationTabs.IndexOf("قائمة أدويتي",StringComparison.Ordinal)<medicationTabs.IndexOf("جرعات اليوم",StringComparison.Ordinal),"Medication list tab is placed on the RTL right before Today doses");
    var dosesHtml=await alice.GetStringAsync("/Doses/Index");
    Check(dosesHtml.Contains("<h1>أدويتي</h1>") && dosesHtml.Contains("جرعات اليوم") && dosesHtml.Contains("قائمة أدويتي"),"Today doses remain inside the Medications experience");
    Check((await alice.GetStringAsync("/Home/Index")).Contains("/Home/History"),"Drug checker contains previous checks link");
    Check((await alice.GetAsync("/Home/History")).IsSuccessStatusCode,"Empty history remains a page rather than redirecting");
    Check((await anonymous.GetStringAsync("/Home/Auth")).Contains("toggle-login-password"),"Login exposes an optional password visibility control");
    var healthToken=await Token(alice,"/Health/Index");
    var sameDrug=await Post(alice,"/Home/CheckInteraction",new(){["drugName1"]="aspirin",["drugName2"]="ASPIRIN"},healthToken);
    Check(sameDrug.IsSuccessStatusCode && !await database.Searches.AnyAsync(),"Two-drug checker rejects duplicate names before external calls");
    var tooManyDrugs=Enumerable.Range(1,7).ToDictionary(i=>$"drugNames[{i-1}]",i=>$"drug-{i}");
    Check((await Post(alice,"/Home/CheckMultiInteraction",tooManyDrugs,healthToken)).StatusCode==HttpStatusCode.Redirect,"Multi-drug checker caps input at six medicines");
    var syntheticGroup=Guid.NewGuid();
    database.Searches.AddRange(
        new Search {UserId=aliceRow.Id,Drug1Name="A",Drug2Name="B",Severity="unknown",CheckKind="multi",CheckGroupId=syntheticGroup,EvidenceStatus="partial",EvidenceNote="Synthetic partial evidence",SourceSummary="Synthetic sources",EvaluatedAt=DateTimeOffset.UtcNow,SearchDate=DateTime.Now},
        new Search {UserId=aliceRow.Id,Drug1Name="A",Drug2Name="C",Severity="moderate",CheckKind="multi",CheckGroupId=syntheticGroup,EvidenceStatus="complete",EvidenceNote="Synthetic complete evidence",SourceSummary="Synthetic sources",EvaluatedAt=DateTimeOffset.UtcNow,SearchDate=DateTime.Now},
        new Search {UserId=aliceRow.Id,Drug1Name="B",Drug2Name="C",Severity="safe",CheckKind="multi",CheckGroupId=syntheticGroup,EvidenceStatus="complete",EvidenceNote="Synthetic complete evidence",SourceSummary="Synthetic sources",EvaluatedAt=DateTimeOffset.UtcNow,SearchDate=DateTime.Now});
    await database.SaveChangesAsync();
    var groupedHistory=await alice.GetStringAsync("/Home/History");
    Check(Regex.Matches(groupedHistory,"data-history-group").Count==1 && groupedHistory.Contains("data-kind=\"multi\"") && groupedHistory.Contains("data-evidence-status=\"partial\""),"History groups multi-drug pairs into one evidence-aware check");
    database.Searches.RemoveRange(await database.Searches.Where(s=>s.CheckGroupId==syntheticGroup).ToListAsync());await database.SaveChangesAsync();
    Check(healthHtml.Contains("dp-assistant-launcher") && healthHtml.Contains("استخدام معلوماتي الصحية"),"Patient layout exposes the optional global AI assistant");
    var drugInfoView=await File.ReadAllTextAsync(Path.Combine(project,"Views/Home/DrugInfo.cshtml"));
    Check(Regex.Matches(drugInfoView,"class=\"info-card\"").Count>=6,"Drug information restores the original detailed information cards");
    Check(!drugInfoView.Contains("data-evidence-status") && !drugInfoView.Contains("data-ai-chat=\"drug\"") && !drugInfoView.Contains("بيانات النشرة المرجعية من openFDA"),"Drug information removes the later source-status blocks and embedded options");
    var interactionResultView=await File.ReadAllTextAsync(Path.Combine(project,"Views/Home/Result.cshtml"));
    Check(interactionResultView.Contains("@if (isCritical)") && interactionResultView.Contains("critical-overlay") && interactionResultView.Contains("تحذير: خطر شديد"),"Serious interaction result restores the red danger overlay");
    var drugInfoController=await File.ReadAllTextAsync(Path.Combine(project,"Controllers/HomeController.cs"));
    Check(drugInfoController.Contains("name.Length is < 2 or > 100") && drugInfoController.IndexOf("name.Length is < 2 or > 100",StringComparison.Ordinal)<drugInfoController.IndexOf("var aiTask =",StringComparison.Ordinal),"Drug information lookup validates bounded names before external calls");
    Check(drugInfoController.Contains("LoadClinicalContextAsync(userId.Value, user)") && drugInfoController.Contains("Task.WhenAll(aiTask, fdaTask)"),"Drug information combines structured patient context with parallel AI and FDA lookups");
    var noCsrfChat=await alice.PostAsync("/Home/AssistantChat",new StringContent("{\"question\":\"مرحبا\",\"scope\":\"global\"}",Encoding.UTF8,"application/json"));
    Check(noCsrfChat.StatusCode==HttpStatusCode.BadRequest,"AI assistant requires CSRF protection");
    var chatRequest=new HttpRequestMessage(HttpMethod.Post,"/Home/AssistantChat");
    chatRequest.Headers.Add("X-CSRF-TOKEN",healthToken);
    chatRequest.Content=new StringContent("{\"question\":\"كيف أضيف دواء؟\",\"scope\":\"global\",\"pageKey\":\"Home/MyMedications\",\"includeHealth\":false}",Encoding.UTF8,"application/json");
    var chatResponse=await alice.SendAsync(chatRequest);var chatJson=await chatResponse.Content.ReadAsStringAsync();
    using var chatDocument=JsonDocument.Parse(chatJson);var chatRoot=chatDocument.RootElement;
    Check(chatResponse.IsSuccessStatusCode && chatRoot.GetProperty("success").GetBoolean() && chatRoot.GetProperty("answer").GetString()?.Contains("خدمة التحليل المساعد غير متاحة")==true,"Global AI assistant accepts a protected bounded request without health context");
    async Task<Dictionary<string,string>> Profile(string code="severe_renal") {
        await database.Entry(aliceRow).ReloadAsync();
        return new() { ["Profile.UserName"]="Test Patient",["Profile.DateOfBirth"]="2000-01-01",["Profile.Gender"]="unknown",
            ["Profile.Version"]=Convert.ToBase64String(aliceRow.HealthRowVersion),["Profile.ConditionCodes[0]"]=code };
    }
    var profile=await Profile();
    Check((await alice.PostAsync("/Health/Save",new FormUrlEncodedContent(profile))).StatusCode==HttpStatusCode.BadRequest,"Health changes require CSRF");
    Check((await Post(alice,"/Health/Save",profile,healthToken)).StatusCode==HttpStatusCode.Redirect,"Structured health profile saves");
    Check(await database.PatientConditions.AnyAsync(c=>c.UserId==aliceRow.Id && c.Code=="severe_renal"),"Condition stored with patient ownership");
    Check((await Post(alice,"/Health/Save",profile,healthToken)).StatusCode==HttpStatusCode.Conflict,"Stale profile cannot overwrite newer data");
    var invalid=await Profile("invented_code");await Post(alice,"/Health/Save",invalid,healthToken);
    Check(!await database.PatientConditions.AnyAsync(c=>c.Code=="invented_code"),"Unknown condition rejected");
    invalid=await Profile();invalid["Profile.Gender"]="male";invalid["Profile.IsPregnant"]="true";
    await Post(alice,"/Health/Save",invalid,healthToken);await database.Entry(aliceRow).ReloadAsync();
    Check(aliceRow.Gender=="unknown","Inconsistent pregnancy input rejected");
    var label=new DrugLabel {Id=Guid.NewGuid().ToString(),SetId=Guid.NewGuid().ToString(),Names=new(){Brand=["Synthetic test product"],Substances=["METFORMIN HYDROCHLORIDE"]},Contraindications=["renal impairment"]};
    var linked=new UserMedication {UserId=aliceRow.Id,DrugName="Synthetic A",LabelId=label.Id,LabelJson=JsonSerializer.Serialize(label),LabelRetrievedAt=DateTimeOffset.UtcNow};
    var duplicate=new UserMedication {UserId=aliceRow.Id,DrugName="Synthetic B",LabelId=Guid.NewGuid().ToString(),LabelJson=JsonSerializer.Serialize(label),LabelRetrievedAt=DateTimeOffset.UtcNow};
    database.UserMedications.AddRange(linked,duplicate);await database.SaveChangesAsync();
    Check((await alice.GetAsync("/Health/Index")).IsSuccessStatusCode,"Profile renders linked substances");
    Check((await bob.GetAsync("/Medications/Search?medicationId="+linked.Id)).StatusCode==HttpStatusCode.NotFound,"Cannot inspect another patient's medication link");
    Check((await Post(bob,"/Medications/Add",new(){["medicationId"]=linked.Id.ToString(),["labelId"]=label.Id},bobToken)).StatusCode==HttpStatusCode.NotFound,"Cannot replace another patient's medication link");
    await Post(alice,"/Health/SaveAllergy",new(){["Allergy.Name"]="test",["Allergy.SubstanceKey"]="METFORMIN HYDROCHLORIDE",["Allergy.Reaction"]="Synthetic reaction"},healthToken);
    var allergy=await database.PatientAllergies.SingleAsync();
    Check(allergy.UserId==aliceRow.Id && allergy.Name=="METFORMIN HYDROCHLORIDE","Allergy linked to server-known substance");
    Check((await bob.GetAsync("/Health/EditAllergy/"+allergy.Id)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot read allergy");
    Check((await Post(bob,"/Health/DeleteAllergy",new(){["id"]=allergy.Id.ToString(),["version"]=Convert.ToBase64String(allergy.RowVersion)},bobToken)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot delete allergy");
    var review=await alice.GetStringAsync("/Health/Review");
    foreach(var kind in new[]{"allergy","duplicate","condition-rule","missing"})Check(review.Contains("data-finding=\""+kind+"\""),"Review renders finding: "+kind);
    await Post(alice,"/Health/Save",await Profile("kidney_disease"),healthToken);
    review=await alice.GetStringAsync("/Health/Review");
    Check(!review.Contains("data-finding=\"condition-rule\""),"Generic kidney disease does not imply severe disease; review recomputed");
    Check((await Post(alice,"/Health/DeleteAllergy",new(){["id"]=allergy.Id.ToString(),["version"]="stale"},healthToken)).StatusCode==HttpStatusCode.Conflict,"Stale allergy deletion blocked");
    await Post(alice,"/Health/DeleteAllergy",new(){["id"]=allergy.Id.ToString(),["version"]=Convert.ToBase64String(allergy.RowVersion)},healthToken);
    Check(!(await alice.GetStringAsync("/Health/Review")).Contains("data-finding=\"allergy\""),"Deleting allergy removes its finding immediately");
    await Post(alice,"/Health/SaveAllergy",new(){["Allergy.Name"]="forged",["Allergy.SubstanceKey"]="UNKNOWN",["Allergy.Reaction"]="test"},healthToken);
    Check(!await database.PatientAllergies.AnyAsync(),"Forged substance linkage rejected");
    var countBefore=await database.UserMedications.CountAsync();
    await Post(alice,"/Medications/Add",new(){["labelId"]="bad-id"},healthToken);
    Check(await database.UserMedications.CountAsync()==countBefore,"Invalid label cannot create medication");
    if(args.Contains("--live-integration")) {
        var source=new DrugLabelClient(new HttpClient {Timeout=TimeSpan.FromSeconds(30)});
        var result=await source.SearchAsync("metformin");
        var product=result.Labels.First(l=>l.Names.Substances.Length>0);
        var added=await Post(alice,"/Medications/Add",new(){["labelId"]=product.Id,["notes"]="Isolated integration test"},healthToken);
        Check(added.Headers.Location?.OriginalString=="/Health/Review","Live medication selection saves and redirects to review");
        var saved=await database.UserMedications.AsNoTracking().SingleAsync(m=>m.UserId==aliceRow.Id && m.LabelId==product.Id);
        Check(HealthReviewEngine.ReadLabel(saved)?.Names.Substances.Length>0 && saved.LabelRetrievedAt!=null,"Server persists fetched identity and substances");
    }
    var engine=new HealthReviewEngine();
    var broken=new UserMedication {DrugName="Broken",LabelJson="{"};
    Check(engine.Evaluate(aliceRow,[],[],[broken]).LinkedCount==0,"Malformed label fails closed");
    broken.LabelJson="{\"openfda\":null,\"warnings\":null}";
    Check(engine.Evaluate(aliceRow,[],[],[broken]).LinkedCount==0,"Null source fields fail closed without crashing");
    linked.LabelRetrievedAt=DateTimeOffset.UtcNow.AddDays(-31);
    Check(engine.Evaluate(aliceRow,[],[],[linked]).Findings.Any(f=>f.Kind=="stale"),"Old label explicitly marked incomplete");
    var cardUrl="/Medications/Edit/"+linked.Id;
    var cardToken=await Token(alice,cardUrl);
    Check((await bob.GetAsync(cardUrl)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot read medication card");
    async Task<Dictionary<string,string>> Card() {
        await database.Entry(linked).ReloadAsync();
        return new(){["Id"]=linked.Id.ToString(),["Version"]=Convert.ToBase64String(linked.RowVersion),["DoseAmount"]="0.5",["DoseUnit"]="tablet",
          ["ScheduleKind"]="daily",["DailyTimes"]="20:00, 08:00",["Quantity"]="12.5",["QuantityUnit"]="tablet",["ExpiryDate"]="2028-06-30",
          ["Instructions"]="Synthetic prescription instructions",["Notes"]="Card test",["Status"]="active",["ChangeReason"]="Initial synthetic card"};
    }
    var card=await Card();
    Check((await alice.PostAsync("/Medications/Edit",new FormUrlEncodedContent(card))).StatusCode==HttpStatusCode.BadRequest,"Medication card requires CSRF");
    Check((await Post(bob,"/Medications/Edit",new(card),bobToken)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot change card");
    card["DrugName"]="FORGED";card["UserId"]="999";
    Check((await Post(alice,"/Medications/Edit",card,cardToken)).StatusCode==HttpStatusCode.Redirect,"Medication card saves valid prescription fields");
    await database.Entry(linked).ReloadAsync();
    Check(linked.DoseAmount==0.5m && linked.Quantity==12.5m && linked.DailyTimes=="08:00, 20:00" && linked.ExpiryDate==new DateTime(2028,6,30),"Dose, stock, expiry and normalized daily times persist");
    Check(linked.UserId==aliceRow.Id && linked.DrugName=="Synthetic A","Card cannot forge owner or medication identity");
    Check((await Post(alice,"/Medications/Edit",card,cardToken)).StatusCode==HttpStatusCode.Conflict,"Stale card cannot overwrite newer fields");
    foreach(var entry in new[]{("DoseAmount","-1"),("DoseAmount","0.0001"),("DoseUnit","invented"),("Quantity","-1"),("QuantityUnit",""),("DailyTimes","25:00"),("DailyTimes","08:00,08:00"),("Status","invented"),("ChangeReason"," ")}) {
        var invalidCard=await Card();invalidCard[entry.Item1]=entry.Item2;
        await Post(alice,"/Medications/Edit",invalidCard,cardToken);
        Check(await database.MedicationChanges.CountAsync(c=>c.UserMedicationId==linked.Id)==1,"Rejected invalid card field: "+entry.Item1+"="+entry.Item2);
    }
    card=await Card();card["ScheduleKind"]="as_needed";card["DailyTimes"]="";card["Instructions"]="";
    await Post(alice,"/Medications/Edit",card,cardToken);
    Check(await database.MedicationChanges.CountAsync(c=>c.UserMedicationId==linked.Id)==1,"As-needed use requires recorded instructions");
    card=await Card();card["Status"]="stopped";card["ChangeReason"]="Stopped in synthetic test";card["Quantity"]="0";
    await Post(alice,"/Medications/Edit",card,cardToken);await database.Entry(linked).ReloadAsync();
    Check(linked.IsStopped && linked.Quantity==0 && await database.UserMedications.AnyAsync(m=>m.Id==linked.Id),"Stopping retains medication and accepts zero stock");
    Check(!(await alice.GetStringAsync("/Home/GetMedicationsJson")).Contains("Synthetic A"),"Stopped medicine excluded from current medication picker");
    review=await alice.GetStringAsync("/Health/Review");
    Check(review.Contains("data-finding=\"stopped\"") && !review.Contains("data-finding=\"duplicate\""),"Review excludes stopped medicine and explains coverage");
    var cardHistory=await database.MedicationChanges.AsNoTracking().Where(c=>c.UserMedicationId==linked.Id).ToListAsync();
    Check(cardHistory.Count==2 && cardHistory.All(c=>c.CreatedAt!=default && c.BeforeJson.Length>0 && c.AfterJson.Length>0),"Card and audit snapshots save together");
    Check((await alice.GetAsync(cardUrl)).IsSuccessStatusCode,"Card history renders after edits");
    card=await Card();card["Status"]="active";card["ScheduleKind"]="as_needed";card["DailyTimes"]="";card["ChangeReason"]="Resumed in synthetic test";
    await Post(alice,"/Medications/Edit",card,cardToken);await database.Entry(linked).ReloadAsync();
    Check(!linked.IsStopped && linked.DailyTimes==null && linked.ScheduleKind=="as_needed","Resuming and switching to as-needed clears fixed times");
    Check((await alice.GetStringAsync("/Health/Review")).Contains("data-finding=\"duplicate\""),"Resumed medication returns to current review");
    Check((await anonymous.GetAsync("/Doses/Index")).StatusCode==HttpStatusCode.Redirect,"Dose schedule requires authentication");
    linked.ScheduleKind="daily";linked.DailyTimes="00:00, 23:59";linked.DoseAmount=0.5m;linked.DoseUnit="tablet";
    await database.SaveChangesAsync();
    var doseDay=DoseSchedule.Now.Date.ToString("yyyy-MM-dd");
    Dictionary<string,string> Dose(string time="00:00")=>new(){["medicationId"]=linked.Id.ToString(),["day"]=doseDay,["time"]=time,["status"]="taken",["version"]=Convert.ToBase64String(linked.RowVersion)};
    var doseHtml=await alice.GetStringAsync("/Doses/Index");
    Check(doseHtml.Contains("00:00") && doseHtml.Contains("23:59"),"Today displays scheduled times");
    Check((await alice.PostAsync("/Doses/Record",new FormUrlEncodedContent(Dose()))).StatusCode==HttpStatusCode.BadRequest,"Dose recording requires CSRF");
    Check((await Post(bob,"/Doses/Record",Dose(),bobToken)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot record dose");
    var badDose=Dose();badDose["day"]=DoseSchedule.Now.Date.AddDays(1).ToString("yyyy-MM-dd");
    Check((await Post(alice,"/Doses/Record",badDose,healthToken)).StatusCode==HttpStatusCode.BadRequest,"Future day dose rejected");
    if(DoseSchedule.Now.ToString("HH:mm")!="23:59")Check((await Post(alice,"/Doses/Record",Dose("23:59"),healthToken)).StatusCode==HttpStatusCode.BadRequest,"Future time cannot be marked taken");
    badDose=Dose();badDose["version"]="stale";
    Check((await Post(alice,"/Doses/Record",badDose,healthToken)).StatusCode==HttpStatusCode.Conflict,"Changed medication card invalidates dose form");
    badDose=Dose();badDose["status"]="invented";
    Check((await Post(alice,"/Doses/Record",badDose,healthToken)).StatusCode==HttpStatusCode.BadRequest,"Invalid dose status rejected");
    var writes=await Task.WhenAll(Post(alice,"/Doses/Record",Dose(),healthToken),Post(alice,"/Doses/Record",Dose(),healthToken));
    Check(writes.Count(r=>r.StatusCode==HttpStatusCode.Redirect)==1 && writes.Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"Concurrent dose submissions save once");
    var doseRecord=await database.DoseRecords.SingleAsync(d=>d.MedicationId==linked.Id);
    Check(doseRecord.Status=="taken" && doseRecord.DoseAmount==0.5m && doseRecord.DrugName=="Synthetic A","Dose snapshot saved from server-owned card");
    await database.Entry(linked).ReloadAsync();
    Check(linked.Quantity==12m && doseRecord.StockDeducted==0.5m,"Taken dose deducts matching stock exactly once");
    Check(await database.DoseChanges.CountAsync(c=>c.DoseRecordId==doseRecord.Id)==1,"First dose event saved atomically");
    Check((await Post(alice,"/Doses/Record",Dose(),healthToken)).StatusCode==HttpStatusCode.Conflict,"Repeat dose submission cannot duplicate");
    Check(!(await bob.GetStringAsync("/Doses/History")).Contains("Synthetic A"),"Dose history is private");
    Check((await bob.GetAsync("/Doses/Correct/"+doseRecord.Id)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot read dose correction");
    Dictionary<string,string> Correction(string state)=>new(){["Id"]=doseRecord.Id.ToString(),["Version"]=Convert.ToBase64String(doseRecord.RowVersion),["Status"]=state,["Reason"]="Synthetic correction"};
    Check((await Post(bob,"/Doses/Correct",Correction("skipped"),bobToken)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot correct dose");
    var staleCorrection=Correction("skipped");
    Check((await Post(alice,"/Doses/Correct",Correction("skipped"),healthToken)).StatusCode==HttpStatusCode.Redirect,"Dose can be corrected to skipped");
    await database.Entry(linked).ReloadAsync();await database.Entry(doseRecord).ReloadAsync();
    Check(linked.Quantity==12.5m && doseRecord.StockDeducted==0,"Changing taken dose to skipped restores deducted stock");
    Check((await Post(alice,"/Doses/Correct",staleCorrection,healthToken)).StatusCode==HttpStatusCode.Conflict,"Stale dose correction rejected");
    await database.Entry(doseRecord).ReloadAsync();
    var noReason=Correction("unrecorded");noReason["Reason"]=" ";await Post(alice,"/Doses/Correct",noReason,healthToken);
    Check(await database.DoseChanges.CountAsync(c=>c.DoseRecordId==doseRecord.Id)==2,"Correction requires reason");
    await Post(alice,"/Doses/Correct",Correction("unrecorded"),healthToken);await database.Entry(doseRecord).ReloadAsync();
    Check(doseRecord.Status=="unrecorded" && await database.DoseChanges.CountAsync(c=>c.DoseRecordId==doseRecord.Id)==3,"Undo preserves dose identity and audit trail");
    linked.DoseAmount=2;linked.IsStopped=true;await database.SaveChangesAsync();
    Check((await Post(alice,"/Doses/Record",Dose(),healthToken)).StatusCode==HttpStatusCode.Conflict,"Stopped medication cannot generate a dose");
    Check((await alice.GetStringAsync("/Doses/Index")).Contains("Synthetic A"),"Existing record remains visible after medication stop");
    await database.Entry(doseRecord).ReloadAsync();Check(doseRecord.DoseAmount==0.5m,"Editing prescription preserves past dose snapshot");
    Check((await alice.GetAsync("/Doses/Correct/"+doseRecord.Id)).IsSuccessStatusCode,"Correction audit page renders");
    var historyMedication=new UserMedication {UserId=aliceRow.Id,DrugName="History retention",DoseAmount=1,DoseUnit="tablet",ScheduleKind="daily",DailyTimes="00:00",Quantity=2,QuantityUnit="tablet"};
    database.UserMedications.Add(historyMedication);await database.SaveChangesAsync();
    var restock=new Dictionary<string,string>{["Id"]=historyMedication.Id.ToString(),["Version"]=Convert.ToBase64String(historyMedication.RowVersion),["Amount"]="3",["Unit"]="tablet",["Reason"]="Synthetic restock"};
    Check((await alice.PostAsync("/Medications/AddStock",new FormUrlEncodedContent(restock))).StatusCode==HttpStatusCode.BadRequest,"Restocking requires CSRF");
    Check((await Post(bob,"/Medications/AddStock",restock,bobToken)).StatusCode==HttpStatusCode.NotFound,"Other patient cannot alter stock");
    Check((await Post(alice,"/Medications/AddStock",restock,healthToken)).StatusCode==HttpStatusCode.Redirect,"Patient can record a new package");
    await database.Entry(historyMedication).ReloadAsync();Check(historyMedication.Quantity==5 && await database.MedicationChanges.AnyAsync(c=>c.UserMedicationId==historyMedication.Id),"Restock adds quantity and audit entry atomically");
    var historyPost=new Dictionary<string,string>{["medicationId"]=historyMedication.Id.ToString(),["day"]=doseDay,["time"]="00:00",["status"]="skipped",["version"]=Convert.ToBase64String(historyMedication.RowVersion)};
    Check((await Post(alice,"/Doses/Record",historyPost,healthToken)).StatusCode==HttpStatusCode.Redirect,"Initial skipped dose is supported");
    await database.Entry(historyMedication).ReloadAsync();Check(historyMedication.Quantity==5,"Skipped dose never deducts stock");
    var alertMedication=new UserMedication {UserId=aliceRow.Id,DrugName="Alert medicine",DoseAmount=1,DoseUnit="tablet",ScheduleKind="daily",DailyTimes="00:00",Quantity=1,QuantityUnit="tablet",ExpiryDate=DateTime.Today.AddDays(-1)};
    database.UserMedications.Add(alertMedication);await database.SaveChangesAsync();
    var medicationDashboard=await alice.GetStringAsync("/Home/MyMedications");
    Check(medicationDashboard.Contains("المخزون منخفض") && medicationDashboard.Contains("منتهي الصلاحية") && medicationDashboard.Contains("الالتزام المسجل خلال 7 أيام"),"Medication dashboard shows stock, expiry and adherence summaries");
    Check(medicationDashboard.Contains("med-search") && medicationDashboard.Contains("med-filter") && medicationDashboard.Contains("med-sort") && medicationDashboard.Contains("تنظيم القائمة"),"Medication list exposes simple client-side search, filters and sorting");
    var notificationCenter=await alice.GetStringAsync("/Notifications/Index");
    Check(notificationCenter.Contains("data-system-notification=\"stock\"") && notificationCenter.Contains("data-system-notification=\"expiry\"") && notificationCenter.Contains("data-system-notification=\"dose\""),"Notification center combines stock, expiry and due-dose alerts");
    Check(medicationDashboard.Contains("notification-count"),"Navigation bell counts current medication alerts");
    database.UserMedications.Remove(alertMedication);await database.SaveChangesAsync();
    database.UserMedications.Remove(historyMedication);await database.SaveChangesAsync();
    Check(await database.DoseRecords.AnyAsync(d=>d.MedicationId==historyMedication.Id),"Deleting medication retains dose history");
    Dictionary<string,string> Application() => new() { ["FullName"]="Test Doctor", ["LicenseNumber"]="TEST", ["Specialization"]="Test", ["Country"]="Jordan", ["LicensingAuthority"]="Test Authority", ["ContactPhone"]="0791234567", ["IsVerified"]="true", ["IsAdmin"]="true", ["Status"]="Approved", ["UserId"]=aliceRow.Id.ToString(), ["Id"]="999" };
    var missing=Application(); missing["Country"]="";
    await Post(bob,"/Doctor/Apply",missing,await Token(bob,"/Doctor/Apply"));
    Check(!await database.Doctors.AnyAsync(),"Incomplete professional application rejected");
    await Post(bob, "/Doctor/Apply", Application(), await Token(bob, "/Doctor/Apply"));
    var doctor = await database.Doctors.SingleAsync();
    Check(!doctor.IsVerified && doctor.UserId != aliceRow.Id && doctor.Id != 999, "Doctor submission cannot forge approval or ownership");
    Check((await bob.GetAsync("/Doctor/Dashboard")).StatusCode==HttpStatusCode.Forbidden,"Pending applicant cannot access doctor dashboard");
    Check((await bob.GetAsync("/Admin/Index")).StatusCode==HttpStatusCode.Forbidden,"Application overposting does not grant admin role");
    Check((await bob.GetAsync("/Home/MyMedications")).Headers.Location?.OriginalString=="/Doctor/Status","Applicant routed to status instead of patient pages");
    Check((await Post(bob,"/Doctor/Apply",Application(),await Token(bob,"/Doctor/Status"))).StatusCode==HttpStatusCode.Conflict,"Pending application cannot be overwritten");
    Check((await alice.GetAsync("/Home/Logout")).StatusCode == HttpStatusCode.MethodNotAllowed, "GET cannot log user out");
    await Post(alice, "/Home/Logout", new(), await Token(alice, "/Home/Account"));
    Check((await alice.GetAsync("/Home/Account")).StatusCode == HttpStatusCode.Redirect, "POST logout revokes browser access");
    await Post(alice, "/Home/Login", new() { ["email"]="alice@example.test", ["password"]="wrong" });
    Check((await alice.GetAsync("/Home/Account")).StatusCode == HttpStatusCode.Redirect, "Wrong password cannot sign in");
    await Post(alice, "/Home/Login", new() { ["email"]=" ALICE@example.test ", ["password"]=password });
    Check((await alice.GetAsync("/Home/Account")).IsSuccessStatusCode, "Valid login with normalized email succeeds");
    var legacy = new User { UserName="Legacy", Email="legacy@example.test", Password=password, EmailConfirmed=true,
        PasswordHash=Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password))), DateOfBirth=new DateTime(2000,1,1) };
    database.Users.Add(legacy); await database.SaveChangesAsync();
    // Login/signup share a 10/minute budget; restart only the isolated test server for the next test group.
    server.Kill(true); await server.WaitForExitAsync(); server.Dispose();
    server = new Process { StartInfo = start };
    server.OutputDataReceived += (_, e) => { if (e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
    for (int i=0;i<80;i++) { try { if ((await anonymous.GetAsync("/Home/Auth")).IsSuccessStatusCode) break; } catch(HttpRequestException){} await Task.Delay(250); }
    using var old = Client();
    await Post(old, "/Home/Login", new() { ["email"]=legacy.Email, ["password"]=password });
    await database.Entry(legacy).ReloadAsync();
    Check(legacy.Password == "" && Convert.FromBase64String(legacy.PasswordHash!).Length != 32 && (await old.GetAsync("/Home/Account")).IsSuccessStatusCode, "Legacy account upgrades and plaintext is erased on login");
    legacy.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10); await database.SaveChangesAsync();
    Check((await old.GetAsync("/Home/Account")).StatusCode == HttpStatusCode.Redirect, "Existing cookie rejected when account is locked");
    // Administrative role changes must take effect for an already signed-in account.
    await database.Entry(aliceRow).ReloadAsync(); aliceRow.IsAdmin=true; await database.SaveChangesAsync();
    Check((await alice.GetAsync("/Admin/Index")).IsSuccessStatusCode,"Trusted admin grant applies to existing cookie");
    Check((await alice.GetAsync("/Home/Auth")).Headers.Location?.OriginalString=="/Admin/Index","Admin login landing is administration");
    var managedAccount=new User {UserName="Managed Account",Email="managed-account@example.test",DateOfBirth=new DateTime(1995,1,1),PasswordHash=passwords.Hash(new User(),password)};
    database.Users.Add(managedAccount);await database.SaveChangesAsync();
    var accountsToken=await Token(alice,"/Admin/Accounts");
    Check((await alice.GetStringAsync("/Admin/Accounts?q=managed-account")).Contains("Managed Account"),"Administrator can search basic account management");
    Check((await bob.GetAsync("/Admin/Accounts")).StatusCode==HttpStatusCode.Forbidden,"Non-admin cannot list accounts");
    var accountAction=new Dictionary<string,string>{{"id",managedAccount.Id.ToString()},{"action","lock"},{"reason","Synthetic account restriction"}};
    Check((await alice.PostAsync("/Admin/AccountState",new FormUrlEncodedContent(accountAction))).StatusCode==HttpStatusCode.BadRequest,"Account management requires CSRF");
    var badAccountAction=new Dictionary<string,string>(accountAction);badAccountAction["reason"]=" ";
    Check((await Post(alice,"/Admin/AccountState",badAccountAction,accountsToken)).StatusCode==HttpStatusCode.BadRequest,"Account management requires an audit reason");
    Check((await Post(alice,"/Admin/AccountState",accountAction,accountsToken)).StatusCode==HttpStatusCode.Redirect,"Administrator can restrict a non-admin account");
    await database.Entry(managedAccount).ReloadAsync();Check(managedAccount.LockoutEnd>DateTimeOffset.UtcNow && managedAccount.SessionVersion==1,"Account restriction revokes active sessions");
    accountAction["action"]="unlock";accountAction["reason"]="Synthetic account reactivation";await Post(alice,"/Admin/AccountState",accountAction,accountsToken);
    await database.Entry(managedAccount).ReloadAsync();Check(managedAccount.LockoutEnd==null && await database.AdminAccountEvents.CountAsync(e=>e.TargetUserId==managedAccount.Id)==2,"Account reactivation and audit trail persist");
    async Task<Dictionary<string,string>> Decision(string decision, string note="Verified against a synthetic test register")
    {
        await database.Entry(doctor).ReloadAsync();
        return new() { ["Id"]=doctor.Id.ToString(),["Version"]=Convert.ToBase64String(doctor.RowVersion),["Decision"]=decision,["Note"]=note,["CredentialsChecked"]="true" };
    }
    string reviewUrl="/Admin/Review/"+doctor.Id;
    var adminToken=await Token(alice,reviewUrl);
    Check((await bob.GetAsync(reviewUrl)).StatusCode==HttpStatusCode.Forbidden,"Applicant cannot inspect administrative review");
    Check((await Post(bob,"/Admin/Review",await Decision("Approved"),await Token(bob,"/Doctor/Status"))).StatusCode==HttpStatusCode.Forbidden,"Applicant cannot approve own application");
    Check((await alice.PostAsync("/Admin/Review",new FormUrlEncodedContent(await Decision("Approved")))).StatusCode==HttpStatusCode.BadRequest,"Admin review requires CSRF token");
    var unconfirmed=await Decision("Approved"); unconfirmed["CredentialsChecked"]="false";
    Check((await Post(alice,"/Admin/Review",unconfirmed,adminToken)).StatusCode==HttpStatusCode.BadRequest,"Approval requires explicit credential check");
    Check((await Post(alice,"/Admin/Review",await Decision("Approved"," "),adminToken)).StatusCode==HttpStatusCode.BadRequest,"Decision requires a reason");
    Check((await Post(alice,"/Admin/Review",await Decision("999"),adminToken)).StatusCode==HttpStatusCode.BadRequest,"Invalid decision enum rejected");
    var stale=await Decision("Approved");
    await Post(alice,"/Admin/Review",await Decision("NeedsInformation","Please complete the licensing details"),adminToken);
    Check((await Post(alice,"/Admin/Review",stale,adminToken)).StatusCode==HttpStatusCode.Conflict,"Stale review cannot overwrite a newer decision");
    Check((await bob.GetStringAsync("/Doctor/Status")).Contains("Please complete the licensing details"),"Applicant sees review feedback");
    await database.Entry(doctor).ReloadAsync();
    var updated=Application(); updated["Version"]=Convert.ToBase64String(doctor.RowVersion); updated["LicenseNumber"]="CORRECTED";
    await Post(bob,"/Doctor/Apply",updated,await Token(bob,"/Doctor/Apply"));
    await database.Entry(doctor).ReloadAsync();
    Check(doctor.Status==DoctorApplicationStatus.Pending && doctor.LicenseNumber=="CORRECTED","Correction resubmits to pending and retains identity");
    await Post(alice,"/Admin/Review",await Decision("Rejected","Synthetic rejection"),adminToken);
    Check((await bob.GetAsync("/Doctor/Dashboard")).StatusCode==HttpStatusCode.Forbidden,"Rejected doctor remains unauthorized");
    await database.Entry(doctor).ReloadAsync(); updated["Version"]=Convert.ToBase64String(doctor.RowVersion);
    await Post(bob,"/Doctor/Apply",updated,await Token(bob,"/Doctor/Apply"));
    await Post(alice,"/Admin/Review",await Decision("Approved"),adminToken);
    Check((await bob.GetAsync("/Doctor/Dashboard")).IsSuccessStatusCode,"Approval grants doctor access without another login");
    Check((await bob.GetAsync("/Home/Auth")).Headers.Location?.OriginalString=="/Doctor/Dashboard","Approved doctor lands in doctor dashboard");
    Check((await bob.GetAsync("/Admin/Index")).StatusCode==HttpStatusCode.Forbidden,"Approved doctor still cannot administer");
    await Post(alice,"/Admin/Review",await Decision("Suspended","Synthetic suspension"),adminToken);
    Check((await bob.GetAsync("/Doctor/Dashboard")).StatusCode==HttpStatusCode.Forbidden,"Suspension immediately removes existing-cookie doctor access");
    Check((await Post(bob,"/Doctor/Apply",Application(),await Token(bob,"/Doctor/Status"))).StatusCode==HttpStatusCode.Conflict,"Suspended doctor cannot bypass review by resubmitting");
    await Post(alice,"/Admin/Review",await Decision("Approved"),adminToken);
    Check((await bob.GetAsync("/Doctor/Dashboard")).IsSuccessStatusCode,"Admin can reinstate after renewed verification");
    var history=await database.DoctorReviews.Where(r=>r.DoctorId==doctor.Id).OrderBy(r=>r.Id).ToListAsync();
    Check(history.Count==8 && history.All(r=>r.ActorUserId>0 && r.CreatedAt!=default && !string.IsNullOrEmpty(r.Note) && !string.IsNullOrEmpty(r.ApplicationSnapshot)),"Submission and review audit trail preserves all eight transitions and snapshots");
    aliceRow.IsAdmin=false; await database.SaveChangesAsync();
    Check((await alice.GetAsync("/Admin/Index")).StatusCode==HttpStatusCode.Forbidden,"Admin revocation applies to existing cookie");
    Check((await anonymous.GetAsync("/Care/Index")).StatusCode==HttpStatusCode.Redirect,"Care links require authentication");
    Check((await alice.GetAsync("/Care/Patients")).StatusCode==HttpStatusCode.Forbidden,"Patient cannot open doctor patient list");
    var careToken=await Token(alice,"/Care/Index");
    Check((await alice.PostAsync("/Care/SendRequest",new FormUrlEncodedContent(new Dictionary<string,string>{["doctorId"]=doctor.Id.ToString(),["consent"]="true"}))).StatusCode==HttpStatusCode.BadRequest,"Care request requires CSRF");
    Check((await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString()},careToken)).StatusCode==HttpStatusCode.BadRequest,"Explicit patient consent required");
    Check((await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString(),["consent"]="true",["PatientId"]="999",["Status"]="active"},careToken)).StatusCode==HttpStatusCode.Redirect,"Consenting patient sends request");
    var care=await database.CareLinks.SingleAsync();
    Check(care.PatientId==aliceRow.Id && care.Status=="pending" && care.ConsentedAt!=default,"Server fixes patient identity and pending status");
    Check((await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString(),["consent"]="true"},careToken)).StatusCode==HttpStatusCode.Conflict,"Duplicate pending care request blocked");
    Check((await bob.GetAsync("/Care/Patient/"+care.Id)).StatusCode==HttpStatusCode.NotFound,"Pending doctor cannot read patient health");
    var patientsHtml=await bob.GetStringAsync("/Care/Patients");
    Check(!patientsHtml.Contains("Synthetic A"),"Pending patient list omits medication details");
    var doctorCareToken=await Token(bob,"/Care/Patients");
    async Task<Dictionary<string,string>> CareDecision(string decision) { await database.Entry(care).ReloadAsync();return new(){["id"]=care.Id.ToString(),["version"]=Convert.ToBase64String(care.RowVersion),["decision"]=decision}; }
    Check((await Post(alice,"/Care/Decide",await CareDecision("active"),careToken)).StatusCode==HttpStatusCode.Forbidden,"Patient cannot forge doctor acceptance");
    var oldDecision=await CareDecision("active");
    Check((await Post(bob,"/Care/Decide",new(oldDecision),doctorCareToken)).StatusCode==HttpStatusCode.Redirect,"Approved doctor accepts consented request");
    Check((await Post(bob,"/Care/Decide",oldDecision,doctorCareToken)).StatusCode==HttpStatusCode.Conflict,"Old acceptance cannot replay");
    var patientView=await bob.GetAsync("/Care/Patient/"+care.Id);
    var patientHtml=await patientView.Content.ReadAsStringAsync();
    Check(patientView.IsSuccessStatusCode && patientHtml.Contains("Synthetic A"),"Accepted doctor sees health and medication summary");
    Check(patientView.Headers.CacheControl?.NoStore==true,"Patient health response disables caching");
    var alertKeyMatch=Regex.Match(patientHtml,"name=\"alertKey\" value=\"([A-F0-9]{64})\"");
    Check(alertKeyMatch.Success && patientHtml.Contains("قرارات الطبيب على التنبيهات"),"Doctor sees server-derived clinical alerts with decision forms");
    var alertOperation=Guid.NewGuid();
    var alertDecision=new Dictionary<string,string>{{"linkId",care.Id.ToString()},{"alertKey",alertKeyMatch.Groups[1].Value},{"status","monitor"},{"reason","Synthetic monitoring decision"},{"operationId",alertOperation.ToString()}};
    Check((await bob.PostAsync("/Care/RecordAlertDecision",new FormUrlEncodedContent(alertDecision))).StatusCode==HttpStatusCode.BadRequest,"Clinical alert decision requires CSRF");
    var shortAlertDecision=new Dictionary<string,string>(alertDecision);shortAlertDecision["reason"]=" ";
    Check((await Post(bob,"/Care/RecordAlertDecision",shortAlertDecision,doctorCareToken)).StatusCode==HttpStatusCode.BadRequest,"Clinical alert decision requires a clear reason");
    Check((await Post(bob,"/Care/RecordAlertDecision",alertDecision,doctorCareToken)).StatusCode==HttpStatusCode.Redirect,"Linked approved doctor records a clinical alert decision");
    var savedAlertDecision=await database.ClinicalAlertDecisions.SingleAsync(d=>d.OperationId==alertOperation);
    Check(savedAlertDecision.PatientId==aliceRow.Id && savedAlertDecision.DoctorUserId==doctor.UserId && savedAlertDecision.Status=="monitor" && savedAlertDecision.Reason=="Synthetic monitoring decision" && savedAlertDecision.AlertSnapshot.Contains("Synthetic"),"Alert decision preserves actor, patient, reason, state and server snapshot");
    Check((await Post(bob,"/Care/RecordAlertDecision",alertDecision,doctorCareToken)).StatusCode==HttpStatusCode.Conflict,"Clinical alert decision operation cannot replay");
    Check((await bob.GetAsync("/Medications/Edit/"+linked.Id)).StatusCode==HttpStatusCode.Forbidden,"Doctor cannot modify patient medication through patient routes");
    var unrelatedUser=new User {UserName="Other Doctor",Email="other-doctor@example.test",DateOfBirth=new DateTime(1980,1,1),IsDoctorApplicant=true};
    var unrelatedDoctor=new Doctor {User=unrelatedUser,FullName="Other Doctor",LicenseNumber="OTHER",Specialization="Test",Status=DoctorApplicationStatus.Approved,IsVerified=true};
    database.Doctors.Add(unrelatedDoctor);await database.SaveChangesAsync();
    var unrelatedLink=new CareLink {PatientId=aliceRow.Id,DoctorId=unrelatedDoctor.Id,Status="active",ConsentedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow};
    database.CareLinks.Add(unrelatedLink);await database.SaveChangesAsync();
    Check((await bob.GetAsync("/Care/Patient/"+unrelatedLink.Id)).StatusCode==HttpStatusCode.NotFound,"Doctor cannot read another doctor's active link");
    Check((await Post(bob,"/Care/Revoke",new(){["id"]=unrelatedLink.Id.ToString(),["version"]=Convert.ToBase64String(unrelatedLink.RowVersion)},doctorCareToken)).StatusCode==HttpStatusCode.NotFound,"Doctor cannot revoke another doctor's link");
    await database.Entry(doctor).ReloadAsync();doctor.Status=DoctorApplicationStatus.Suspended;doctor.IsVerified=false;await database.SaveChangesAsync();
    Check((await bob.GetAsync("/Care/Patient/"+care.Id)).StatusCode==HttpStatusCode.Forbidden,"Suspension immediately blocks linked patient access");
    Check((await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString(),["consent"]="true"},careToken)).StatusCode==HttpStatusCode.NotFound,"Suspended doctor cannot receive new requests");
    doctor.Status=DoctorApplicationStatus.Approved;doctor.IsVerified=true;await database.SaveChangesAsync();
    await database.Entry(care).ReloadAsync();
    Check((await Post(alice,"/Care/Revoke",new(){["id"]=care.Id.ToString(),["version"]="stale"},careToken)).StatusCode==HttpStatusCode.Conflict,"Stale revocation rejected");
    Check((await Post(alice,"/Care/Revoke",new(){["id"]=care.Id.ToString(),["version"]=Convert.ToBase64String(care.RowVersion)},careToken)).StatusCode==HttpStatusCode.Redirect,"Patient revokes active link");
    Check((await bob.GetAsync("/Care/Patient/"+care.Id)).StatusCode==HttpStatusCode.NotFound,"Revocation blocks existing-session access immediately");
    Check((await Post(bob,"/Care/Decide",await CareDecision("active"),doctorCareToken)).StatusCode==HttpStatusCode.Conflict,"Doctor cannot reactivate revoked consent");
    await database.Entry(care).ReloadAsync();
    await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString(),["consent"]="true",["version"]=Convert.ToBase64String(care.RowVersion)},careToken);
    Check((await Post(bob,"/Care/Decide",await CareDecision("rejected"),doctorCareToken)).StatusCode==HttpStatusCode.Redirect,"Doctor can reject renewed request");
    Check((await bob.GetAsync("/Care/Patient/"+care.Id)).StatusCode==HttpStatusCode.NotFound,"Rejected request grants no access");
    var careEvents=await database.CareLinkEvents.Where(e=>e.CareLinkId==care.Id).ToListAsync();
    Check(careEvents.Count==5 && careEvents.All(e=>e.ActorUserId>0 && e.CreatedAt!=default && e.Scope=="health-medications-doses-read-v1"),"Consent, decisions and revocation audit trail preserved");
    // Treatment editing needs a separate, fresh patient grant; existing read consent never expands silently.
    await database.Entry(care).ReloadAsync();
    await Post(alice,"/Care/SendRequest",new(){["doctorId"]=doctor.Id.ToString(),["consent"]="true",["version"]=Convert.ToBase64String(care.RowVersion)},careToken);
    await Post(bob,"/Care/Decide",await CareDecision("active"),doctorCareToken);await database.Entry(care).ReloadAsync();
    Check(!care.CanManageTreatment,"Existing read consent does not grant treatment editing");
    var treatmentUrl="/Treatment/Edit?linkId="+care.Id+"&id="+linked.Id;
    Check((await bob.GetAsync(treatmentUrl)).StatusCode==HttpStatusCode.NotFound,"Doctor cannot edit without separate treatment consent");
    Dictionary<string,string> Permission(bool allow,bool consent=true)=>new(){["id"]=care.Id.ToString(),["version"]=Convert.ToBase64String(care.RowVersion),["allow"]=allow.ToString(),["consent"]=consent.ToString()};
    Check((await Post(alice,"/Care/TreatmentPermission",Permission(true,false),careToken)).StatusCode==HttpStatusCode.BadRequest,"Treatment grant requires explicit consent");
    Check((await Post(bob,"/Care/TreatmentPermission",Permission(true),doctorCareToken)).StatusCode==HttpStatusCode.Forbidden,"Doctor cannot grant own treatment permission");
    await Post(alice,"/Care/TreatmentPermission",Permission(true),careToken);await database.Entry(care).ReloadAsync();
    Check(care.CanManageTreatment,"Patient can grant treatment editing");
    var treatmentToken=await Token(bob,treatmentUrl);
    async Task<Dictionary<string,string>> Treatment() {
      await database.Entry(linked).ReloadAsync();await database.Entry(care).ReloadAsync();
      return new(){["LinkId"]=care.Id.ToString(),["LinkVersion"]=Convert.ToBase64String(care.RowVersion),["Id"]=linked.Id.ToString(),["Version"]=Convert.ToBase64String(linked.RowVersion),["OperationId"]=Guid.NewGuid().ToString(),["DoseAmount"]="1.5",["DoseUnit"]="tablet",["ScheduleKind"]="daily",["DailyTimes"]="08:00, 20:00",["Instructions"]="Synthetic doctor instructions",["Status"]="active",["ChangeReason"]="Synthetic treatment adjustment"};
    }
    var treatment=await Treatment();
    Check((await bob.PostAsync("/Treatment/Edit",new FormUrlEncodedContent(treatment))).StatusCode==HttpStatusCode.BadRequest,"Doctor treatment writes require CSRF");
    Check((await Post(alice,"/Treatment/Edit",new(treatment),careToken)).StatusCode==HttpStatusCode.Forbidden,"Patient cannot impersonate doctor treatment write");
    var invalidTreatment=new Dictionary<string,string>(treatment);invalidTreatment["ChangeReason"]=" ";await Post(bob,"/Treatment/Edit",invalidTreatment,treatmentToken);
    Check(!await database.TreatmentChanges.AnyAsync(),"Treatment reason mandatory; no partial audit or write");
    var previousQuantity=linked.Quantity;var previousNotes=linked.Notes;var previousOwner=linked.UserId;
    treatment["Quantity"]="999";treatment["QuantityUnit"]="tablet";treatment["Notes"]="FORGED";treatment["UserId"]="999";
    Check((await Post(bob,"/Treatment/Edit",new(treatment),treatmentToken)).StatusCode==HttpStatusCode.Redirect,"Doctor updates authorized treatment plan");
    await database.Entry(linked).ReloadAsync();
    Check(linked.DoseAmount==1.5m && linked.DailyTimes=="08:00, 20:00" && !linked.IsStopped,"Treatment fields persisted");
    Check(linked.Quantity==previousQuantity && linked.Notes==previousNotes && linked.UserId==previousOwner,"Doctor cannot overwrite patient inventory, personal notes or owner");
    var treatmentChange=await database.TreatmentChanges.SingleAsync();var notice=await database.PatientNotifications.SingleAsync();
    Check(treatmentChange.DoctorUserId==doctor.UserId && treatmentChange.PatientId==aliceRow.Id && treatmentChange.BeforeJson!=null && treatmentChange.AfterJson.Contains("1.5") && notice.TreatmentChangeId==treatmentChange.Id,"Treatment audit and patient notification saved atomically");
    Check((await Post(bob,"/Treatment/Edit",new(treatment),treatmentToken)).StatusCode==HttpStatusCode.Conflict,"Repeated treatment submission cannot duplicate change");
    Check(await database.PatientNotifications.CountAsync()==1,"No duplicate patient notification");
    var staleTreatment=await Treatment();staleTreatment["Version"]="old";await Post(bob,"/Treatment/Edit",staleTreatment,treatmentToken);
    Check(await database.TreatmentChanges.CountAsync()==1,"Stale medication version rejected without side effects");
    Check((await bob.GetAsync("/Notifications/Details/"+notice.Id)).StatusCode==HttpStatusCode.Forbidden,"Doctor cannot open patient notification endpoint");
    Check((await alice.GetStringAsync("/Notifications/Details/"+notice.Id)).Contains("Synthetic treatment adjustment"),"Patient reads before-and-after treatment notification");
    var notificationsHtml=await alice.GetStringAsync("/Notifications/Index");
    Check(notificationsHtml.Contains("notification-count") && notificationsHtml.Contains("notification-bell"),"Unread notification count appears on the bell");
    Check(notificationsHtml.Contains("doctor-toast") && notificationsHtml.Contains("لديك تعديل جديد من الطبيب"),"Latest doctor change appears as a patient toast");
    await Post(alice,"/Notifications/Read",new(){["id"]=notice.Id.ToString()},careToken);await database.Entry(notice).ReloadAsync();
    Check(notice.ReadAt!=null,"Patient can acknowledge notification");
    var otherNoticeChange=new TreatmentChange {PatientId=legacy.Id,DoctorUserId=doctor.UserId,LinkId=care.Id,MedicationId=linked.Id,OperationId=Guid.NewGuid(),DoctorName="Test",Reason="Private",AfterJson=treatmentChange.AfterJson,CreatedAt=DateTimeOffset.UtcNow};
    var otherNotice=new PatientNotification {PatientId=legacy.Id,Change=otherNoticeChange};database.PatientNotifications.Add(otherNotice);await database.SaveChangesAsync();
    Check((await alice.GetAsync("/Notifications/Details/"+otherNotice.Id)).StatusCode==HttpStatusCode.NotFound,"Patient cannot read another patient's notification");
    Check((await Post(alice,"/Notifications/Read",new(){["id"]=otherNotice.Id.ToString()},careToken)).StatusCode==HttpStatusCode.NotFound,"Patient cannot acknowledge another patient's notification");
    treatment=await Treatment();treatment["Status"]="stopped";treatment["ChangeReason"]="Synthetic stop";
    await Post(bob,"/Treatment/Edit",treatment,treatmentToken);await database.Entry(linked).ReloadAsync();
    Check(linked.IsStopped && await database.UserMedications.AnyAsync(m=>m.Id==linked.Id),"Doctor stop archives medication without deleting history");
    Check(!(await alice.GetStringAsync("/Home/GetMedicationsJson")).Contains("Synthetic A"),"Doctor stop updates patient's current medication list");
    if(args.Contains("--live-treatment")) {
      var source=new DrugLabelClient(new HttpClient {Timeout=TimeSpan.FromSeconds(30)});var products=await source.SearchAsync("ibuprofen");var product=products.Labels.First(l=>l.Names.Substances.Length>0);
      var addition=await Treatment();addition["Id"]="0";addition["Version"]="new";addition["LabelId"]=product.Id;addition["ChangeReason"]="Synthetic live product addition";
      Check((await Post(bob,"/Treatment/Edit",addition,treatmentToken)).StatusCode==HttpStatusCode.Redirect,"Doctor can add a verified live product to consented patient");
      Check(await database.UserMedications.AnyAsync(m=>m.UserId==aliceRow.Id && m.LabelId==product.Id),"Doctor addition saves verified label identity");
    }
    // Export only synthetic rendered HTML for layout review; not a production auth bypass.
    var preview=Path.Combine(root,".local","treatment-preview");Directory.CreateDirectory(preview);
    async Task Preview(HttpClient client,string url,string filename){var html=await client.GetStringAsync(url);html=html.Replace("/css/doctor-workflow.css","doctor-workflow.css").Replace("/lib/bootstrap/dist/css/bootstrap.min.css","bootstrap.min.css");html=Regex.Replace(html,"/css/navigation.css[^\"']*","navigation.css");html=Regex.Replace(html,"/css/patient-pages.css[^\"']*","patient-pages.css");await File.WriteAllTextAsync(Path.Combine(preview,filename),html);}
    File.Copy(Path.Combine(project,"wwwroot/css/doctor-workflow.css"),Path.Combine(preview,"doctor-workflow.css"),true);File.Copy(Path.Combine(project,"wwwroot/css/navigation.css"),Path.Combine(preview,"navigation.css"),true);File.Copy(Path.Combine(project,"wwwroot/css/patient-pages.css"),Path.Combine(preview,"patient-pages.css"),true);File.Copy(Path.Combine(project,"wwwroot/lib/bootstrap/dist/css/bootstrap.min.css"),Path.Combine(preview,"bootstrap.min.css"),true);
    await Preview(bob,treatmentUrl,"doctor-edit.html");await Preview(alice,"/Notifications/Details/"+notice.Id,"patient-notice.html");await Preview(alice,"/Care/Index","patient-consent.html");await Preview(alice,"/Home/MyMedications","patient-medications.html");await Preview(alice,"/Doses/Index","patient-doses.html");await Preview(alice,"/Health/Index","patient-health.html");
    treatment=await Treatment();await database.Entry(care).ReloadAsync();await Post(alice,"/Care/TreatmentPermission",Permission(false),careToken);
    Check((await Post(bob,"/Treatment/Edit",treatment,treatmentToken)).StatusCode==HttpStatusCode.NotFound,"Withdrawal blocks already-open doctor edit form");
    Check((await bob.GetAsync("/Care/Patient/"+care.Id)).IsSuccessStatusCode,"Withdrawing treatment edit retains separately consented read access");
    await database.Entry(care).ReloadAsync();await Post(alice,"/Care/TreatmentPermission",Permission(true),careToken);treatment=await Treatment();
    doctor.Status=DoctorApplicationStatus.Suspended;doctor.IsVerified=false;await database.SaveChangesAsync();
    Check((await Post(bob,"/Treatment/Edit",treatment,treatmentToken)).StatusCode==HttpStatusCode.Forbidden,"Suspended doctor cannot save treatment");
    doctor.Status=DoctorApplicationStatus.Approved;doctor.IsVerified=true;await database.SaveChangesAsync();
    await database.Entry(care).ReloadAsync();await Post(alice,"/Care/Revoke",new(){["id"]=care.Id.ToString(),["version"]=Convert.ToBase64String(care.RowVersion)},careToken);
    Check((await Post(bob,"/Treatment/Edit",treatment,treatmentToken)).StatusCode==HttpStatusCode.NotFound,"Revocation blocks open treatment form");
    Check((await alice.GetAsync("/Notifications/Details/"+notice.Id)).IsSuccessStatusCode,"Patient retains treatment evidence after relationship ends");
    var accountSummary=await alice.GetStringAsync("/Home/Account");
    Check(accountSummary.Contains("ملخص المتابعة")&&accountSummary.Contains("تنبيهات حالية")&&accountSummary.Contains("الجرعة القادمة اليوم")&&accountSummary.Contains("الطبيب المتابع"),"Account page provides the tested personal health and follow-up summary");
    Check(await database.TreatmentChanges.AnyAsync(c=>c.PatientId==aliceRow.Id),"Treatment history remains available for unified activity");
    var activityResponse=await alice.GetAsync("/Home/Activity?filter=treatment");
    var activityHtml=WebUtility.HtmlDecode(await activityResponse.Content.ReadAsStringAsync());
    var careActivityResponse=await alice.GetAsync("/Home/Activity?filter=care");
    var careActivityHtml=WebUtility.HtmlDecode(await careActivityResponse.Content.ReadAsStringAsync());
    Check(activityResponse.IsSuccessStatusCode&&careActivityResponse.IsSuccessStatusCode,"Unified activity filters return successful pages");
    Check(activityHtml.Contains("سجل النشاط"),"Unified activity history page renders");
    Check(activityHtml.Contains("تعديل من الطبيب"),"Activity treatment filter includes doctor changes");
    Check(careActivityHtml.Contains("تغيير في ربط الطبيب"),"Activity care filter includes relationship events");
    Check((await anonymous.GetAsync("/Home/Activity")).StatusCode==HttpStatusCode.Redirect,"Anonymous user cannot open account activity history");
    var reportIndex=await alice.GetStringAsync("/Reports/Index");
    Check(reportIndex.Contains("تقرير زيارة الطبيب") && reportIndex.Contains("اختر المعلومات"),"Patient can choose visit report sections");
    var reportToken=await Token(alice,"/Reports/Index");
    var reportResponse=await Post(alice,"/Reports/Preview",new(){["IncludeProfile"]="true",["IncludeConditions"]="true",["IncludeMedications"]="true",["IncludeDoseSummary"]="true",["IncludeRecentChecks"]="true",["VisitNote"]="Synthetic visit question"},reportToken);
    var reportHtml=await reportResponse.Content.ReadAsStringAsync();
    Check(reportResponse.IsSuccessStatusCode && reportHtml.Contains("تاريخ الإعداد") && reportHtml.Contains("طباعة أو حفظ PDF"),"Visit report shows generation date and print-to-PDF action");
    Check(reportHtml.Contains("Synthetic visit question") && reportHtml.Contains("الأدوية الحالية"),"Visit report contains only selected patient information and visit note");
    var sharedSearch=new Search {UserId=aliceRow.Id,Drug1Name="Synthetic Share A",Drug2Name="Synthetic Share B",Severity="moderate",ResultMessage="Synthetic shared result",CheckGroupId=Guid.NewGuid(),CheckKind="pair",EvidenceStatus="complete",EvaluatedAt=DateTimeOffset.UtcNow};
    database.Searches.Add(sharedSearch);await database.SaveChangesAsync();
    Check((await alice.GetAsync("/Home/ManageShare/"+sharedSearch.Id)).IsSuccessStatusCode,"Patient can manage own result sharing");
    var shareManageToken=await Token(alice,"/Home/ManageShare/"+sharedSearch.Id);
    await Post(alice,"/Home/CreateShare",new(){["id"]=sharedSearch.Id.ToString(),["durationDays"]="1"},shareManageToken);
    var sharePage=await alice.GetStringAsync("/Home/ManageShare/"+sharedSearch.Id);
    var shareMatch=Regex.Match(WebUtility.HtmlDecode(sharePage),@"ViewShared\?token=([A-Za-z0-9_-]+)");
    Check(shareMatch.Success,"Share link is shown once after explicit creation");
    database.ChangeTracker.Clear();var storedShare=await database.Searches.SingleAsync(s=>s.Id==sharedSearch.Id);
    Check(storedShare.ShareToken==null && storedShare.ShareTokenHash?.Length==32 && storedShare.ShareExpiresAt>DateTimeOffset.UtcNow,"Only expiring SHA-256 share-token fingerprint is stored");
    var publicSharePath="/Home/ViewShared?token="+shareMatch.Groups[1].Value;
    Check((await anonymous.GetStringAsync(publicSharePath)).Contains("Synthetic shared result"),"Valid unexpired share link opens only the selected result");
    var revokeToken=await Token(alice,"/Home/ManageShare/"+sharedSearch.Id);
    await Post(alice,"/Home/RevokeShare",new(){["id"]=sharedSearch.Id.ToString()},revokeToken);
    Check((await anonymous.GetStringAsync(publicSharePath)).Contains("غير صالح أو منتهي"),"Revoked share link stops working immediately");
    Check((await anonymous.GetAsync("/Home/ManageShare/"+sharedSearch.Id)).StatusCode==HttpStatusCode.Redirect,"Anonymous user cannot manage result sharing");
    using var applicant=Client(); var applicationSignup=Signup("new-doctor@example.test"); applicationSignup["accountType"]="doctor";
    applicationSignup["IsAdmin"]="true";
    Check((await Post(applicant,"/Home/SignUp",applicationSignup)).Headers.Location?.OriginalString?.StartsWith("/Home/VerifyEmailNotice")==true,"Doctor signup waits for email verification");
    Check((await ConfirmLatest(applicant)).Headers.Location?.OriginalString=="/Doctor/Apply","Confirmed doctor signup directs to professional application");
    Check((await applicant.GetAsync("/Admin/Index")).StatusCode==HttpStatusCode.Forbidden,"Signup cannot forge admin role");
    Check((await applicant.GetAsync("/Doctor/Dashboard")).StatusCode==HttpStatusCode.Forbidden,"New doctor signup has no professional permissions");
    using var recoveryClient=Client();
    Check((await Post(recoveryClient,"/Home/SignUp",Signup("recover@example.test"))).StatusCode==HttpStatusCode.Redirect,"Recovery test account created");
    await ConfirmLatest(recoveryClient);
    var recoveryToken=await Token(recoveryClient,"/Home/ForgotPassword");
    var recoveryResponse=await Post(recoveryClient,"/Home/ForgotPassword",new(){["email"]="recover@example.test"},recoveryToken);
    Check(recoveryResponse.Headers.Location?.OriginalString=="/Home/ForgotPasswordConfirmation","Password recovery returns the non-enumerating confirmation page");
    string? mailFile=null;for(var i=0;i<30 && mailFile==null;i++){mailFile=Directory.GetFiles(mailPickup).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(f=>DecodeMail(File.ReadAllText(f)).Contains("ResetPassword?token="));if(mailFile==null)await Task.Delay(100);}
    Check(mailFile!=null,"Password recovery email generated");
    var mailText=await File.ReadAllTextAsync(mailFile!);
    var resetMatch=Regex.Match(DecodeMail(mailText),@"ResetPassword\?token=([A-Za-z0-9_-]+)");
    Check(resetMatch.Success,"Password recovery email contains an opaque reset link");
    var opaqueToken=resetMatch.Groups[1].Value;var resetPath="/Home/ResetPassword?token="+opaqueToken;
    var resetToken=await Token(recoveryClient,resetPath);
    var resetResponse=await Post(recoveryClient,"/Home/ResetPassword",new(){["token"]=opaqueToken,["password"]="New-test!Password84",["confirmPassword"]="New-test!Password84"},resetToken);
    Check(resetResponse.StatusCode==HttpStatusCode.Redirect && resetResponse.Headers.Location!=null,"Valid one-time link changes the password");
    Check((await recoveryClient.GetAsync("/Home/Account")).StatusCode==HttpStatusCode.Redirect,"Password reset invalidates existing sessions");
    using var recovered=Client();await Post(recovered,"/Home/Login",new(){["email"]="recover@example.test",["password"]="New-test!Password84"});
    Check((await recovered.GetAsync("/Home/Account")).IsSuccessStatusCode,"New password signs in");
    using var oldPassword=Client();await Post(oldPassword,"/Home/Login",new(){["email"]="recover@example.test",["password"]=password});
    Check((await oldPassword.GetAsync("/Home/Account")).StatusCode==HttpStatusCode.Redirect,"Old password no longer signs in");
    Check((await recoveryClient.GetStringAsync(resetPath)).Contains("الرابط غير صالح"),"Password reset link cannot be reused");
    var storedReset=await database.PasswordResetTokens.SingleAsync(t=>t.User.Email=="recover@example.test");
    Check(storedReset.TokenHash.Length==32 && storedReset.UsedAt!=null,"Only a used SHA-256 token fingerprint is retained");
    var rateToken = await Token(anonymous);
    HttpStatusCode last = 0;
    for (int i=0;i<11;i++) last = (await Post(anonymous,"/Home/Login",new(){["email"]="nobody@example.test",["password"]="wrong"},rateToken)).StatusCode;
    Check(last == HttpStatusCode.TooManyRequests, "Excessive account requests rate-limited");
    Console.WriteLine($"SUCCESS: {passed} checks passed against isolated database.");
}
catch
{
    // Logs contain only synthetic test data; do not print application configuration.
    File.WriteAllText(Path.Combine(root,"tests/AccountChecks/last-run.log"),logs.ToString());
    throw;
}
finally
{
    if (server is { HasExited: false }) { server.Kill(true); await server.WaitForExitAsync(); }
    server?.Dispose();
    await database.Database.EnsureDeletedAsync();
    if(Directory.Exists(mailPickup)) Directory.Delete(mailPickup,true);
}
