using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace DigitalPharma.MVC.Controllers
{
    [Authorize]
    [AutoValidateAntiforgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private readonly OpenFdaService _fdaService;
        private readonly GeminiAiService _claudeService;
        private readonly PharmaDbContext _context;
        private readonly AccountPasswordService _passwords;
        private readonly PasswordResetEmailService _resetEmails;
        private readonly PatientAlertService _patientAlerts;
        private readonly IConfiguration _configuration;
        private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public HomeController(PharmaDbContext context, OpenFdaService fdaService, GeminiAiService claudeService,
            AccountPasswordService passwords, PasswordResetEmailService resetEmails, PatientAlertService patientAlerts, IConfiguration configuration)
        {
            _context = context;
            _passwords = passwords;
            _fdaService = fdaService;
            _claudeService = claudeService;
            _resetEmails = resetEmails;
            _patientAlerts = patientAlerts;
            _configuration = configuration;
        }

        // ─── Authentication ───────────────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Auth()
        {
            if (CurrentUserId != null)
                return User.IsInRole(AccountRoles.Admin) ? RedirectToAction("Index","Admin") :
                    User.IsInRole(AccountRoles.Doctor) ? RedirectToAction("Dashboard","Doctor") :
                    User.IsInRole(AccountRoles.Applicant) ? RedirectToAction("Status","Doctor") : RedirectToAction("Index");
            return View();
        }

        // Registration and login share an IP-based request limit.
        [HttpPost, AllowAnonymous, EnableRateLimiting("accounts")]
        public async Task<IActionResult> SignUp(string userName, string email, string password,
            string phoneNumber, DateTime dateOfBirth, string accountType = "patient")
        {
            if (accountType is not ("patient" or "doctor")) return BadRequest();
            email = (email ?? "").Trim().ToLowerInvariant();
            userName = (userName ?? "").Trim();
            if (userName.Length is < 2 or > 100 || email.Length > 254 ||
                !new EmailAddressAttribute().IsValid(email) || string.IsNullOrEmpty(password) ||
                password.Length is < 8 or > 128 || dateOfBirth == default ||
                dateOfBirth.Date > DateTime.Today || dateOfBirth.Date < DateTime.Today.AddYears(-120) ||
                (phoneNumber?.Length ?? 0) > 30)
            {
                TempData["SignupError"] = "تحقق من الاسم والبريد وتاريخ الميلاد. يجب أن تكون كلمة المرور بين 8 و128 حرفاً.";
                return RedirectToAction("Auth");
            }

            // Keep retries and the serializable email check/insert in one unit.
            var confirmationBytes=RandomNumberGenerator.GetBytes(32);
            var confirmationToken=WebEncoders.Base64UrlEncode(confirmationBytes);
            var user = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email)) return null;
                var account = new User
                {
                    UserName = userName, Email = email, Password = string.Empty,
                    PhoneNumber = phoneNumber?.Trim(), DateOfBirth = dateOfBirth.Date,
                    ProfileCompleted = false, IsDoctorApplicant = accountType == "doctor"
                };
                account.PasswordHash = _passwords.Hash(account, password);
                _context.Users.Add(account);
                _context.EmailConfirmationTokens.Add(new(){User=account,TokenHash=SHA256.HashData(confirmationBytes),CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=DateTimeOffset.UtcNow.AddHours(24)});
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return account;
            });
            if (user == null)
            {
                TempData["SignupError"] = "تعذر إنشاء الحساب بهذه البيانات. جرّب تسجيل الدخول إن كان لديك حساب.";
                return RedirectToAction("Auth");
            }
            var confirmationUrl=BuildPublicUrl(nameof(ConfirmEmail),new{token=confirmationToken});
            if(confirmationUrl!=null)await _resetEmails.SendConfirmationAsync(user.Email,confirmationUrl,HttpContext.RequestAborted);
            return RedirectToAction(nameof(VerifyEmailNotice),new{email=user.Email});
        }

        [HttpPost, AllowAnonymous, EnableRateLimiting("accounts")]
        public async Task<IActionResult> Login(string email, string password)
        {
            var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
            var user = normalizedEmail.Length <= 254 && !string.IsNullOrEmpty(password) && password.Length <= 128
                ? await AccountRoles.AuthenticationFields(_context.Users.AsNoTracking().Where(u => u.Email.ToLower() == normalizedEmail)).FirstOrDefaultAsync() : null;
            var verification = user == null ? PasswordVerificationResult.Failed : _passwords.Verify(user, password);
            if (user == null || verification == PasswordVerificationResult.Failed || user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                TempData["LoginEmail"] = normalizedEmail.Length<=254?normalizedEmail:"";
                TempData["LoginError"] = "البريد الإلكتروني أو كلمة المرور غير صحيحة، أو الحساب غير متاح.";
                return RedirectToAction("Auth");
            }
            if(!user.EmailConfirmed)
            {
                TempData["LoginError"]="أكد بريدك الإلكتروني قبل تسجيل الدخول. يمكنك طلب رابط جديد.";
                TempData["LoginEmail"]=normalizedEmail;
                return RedirectToAction(nameof(VerifyEmailNotice),new{email=normalizedEmail});
            }
            if (verification == PasswordVerificationResult.SuccessRehashNeeded || !string.IsNullOrEmpty(user.Password))
            {
                var hash = verification == PasswordVerificationResult.SuccessRehashNeeded
                    ? _passwords.Hash(user, password) : user.PasswordHash;
                var updated = await _context.Users
                    .Where(u => u.Id == user.Id && u.SessionVersion == user.SessionVersion && u.PasswordHash == user.PasswordHash)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(u => u.PasswordHash, hash)
                        .SetProperty(u => u.Password, string.Empty));
                if (updated == 0)
                {
                    TempData["LoginError"] = "تغيّرت بيانات الحساب. أعد تسجيل الدخول.";
                    return RedirectToAction("Auth");
                }
            }
            await SignInUserAsync(user);
            return await LandingAsync(user);
        }

        [HttpGet, AllowAnonymous]
        public IActionResult ForgotPassword() => View();

        [HttpGet,AllowAnonymous]
        public IActionResult VerifyEmailNotice(string? email){ViewBag.Email=(email??"").Trim();return View();}

        [HttpGet,AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string? token,CancellationToken cancellationToken)
        {
            if(!TryHashResetToken(token,out var hash))return View("ResetPasswordInvalid");
            var entry=await _context.EmailConfirmationTokens.Include(t=>t.User).SingleOrDefaultAsync(t=>t.TokenHash==hash && t.UsedAt==null && t.ExpiresAt>DateTimeOffset.UtcNow,cancellationToken);
            if(entry==null)return View("ResetPasswordInvalid");
            entry.UsedAt=DateTimeOffset.UtcNow;entry.User.EmailConfirmed=true;entry.User.SessionVersion++;
            await _context.SaveChangesAsync(cancellationToken);await SignInUserAsync(entry.User);
            TempData["Success"]="تم تأكيد البريد الإلكتروني.";
            return await LandingAsync(entry.User);
        }

        [HttpPost,AllowAnonymous,EnableRateLimiting("passwordRecovery")]
        public async Task<IActionResult> ResendEmailConfirmation(string email,CancellationToken cancellationToken)
        {
            var normalized=(email??"").Trim().ToLowerInvariant();
            if(normalized.Length<=254 && new EmailAddressAttribute().IsValid(normalized))
            {
                var user=await _context.Users.SingleOrDefaultAsync(u=>u.Email.ToLower()==normalized,cancellationToken);
                if(user!=null && !user.EmailConfirmed && (user.LockoutEnd==null||user.LockoutEnd<=DateTimeOffset.UtcNow))
                {
                    var raw=RandomNumberGenerator.GetBytes(32);var encoded=WebEncoders.Base64UrlEncode(raw);
                    var entry=new EmailConfirmationToken{UserId=user.Id,TokenHash=SHA256.HashData(raw),CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=DateTimeOffset.UtcNow.AddHours(24)};
                    _context.EmailConfirmationTokens.Add(entry);await _context.SaveChangesAsync(cancellationToken);
                    var url=BuildPublicUrl(nameof(ConfirmEmail),new{token=encoded});
                    if(url==null||!await _resetEmails.SendConfirmationAsync(user.Email,url,cancellationToken)){_context.EmailConfirmationTokens.Remove(entry);await _context.SaveChangesAsync(cancellationToken);}
                }
            }
            TempData["VerifyEmailMessage"]="إذا كان الحساب بانتظار التأكيد، أرسلنا رابطاً جديداً.";
            return RedirectToAction(nameof(VerifyEmailNotice),new{email=normalized});
        }

        [HttpPost, AllowAnonymous, EnableRateLimiting("passwordRecovery")]
        public async Task<IActionResult> ForgotPassword(string email, CancellationToken cancellationToken)
        {
            var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
            if (normalizedEmail.Length <= 254 && new EmailAddressAttribute().IsValid(normalizedEmail))
            {
                var user = await _context.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
                if (user != null && (user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow))
                {
                    var rawToken = RandomNumberGenerator.GetBytes(32);
                    var encodedToken = WebEncoders.Base64UrlEncode(rawToken);
                    var token = new PasswordResetToken
                    {
                        UserId = user.Id,
                        TokenHash = SHA256.HashData(rawToken),
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                    };
                    _context.PasswordResetTokens.Add(token);
                    await _context.SaveChangesAsync(cancellationToken);

                    var configuredBase = _configuration["App:PublicBaseUrl"]?.TrimEnd('/');
                    var relativePath = Url.Action(nameof(ResetPassword), "Home", new { token = encodedToken });
                    var resetUrl = Uri.TryCreate(configuredBase, UriKind.Absolute, out var baseUri) && relativePath != null
                        ? new Uri(baseUri, relativePath).AbsoluteUri : null;
                    var delivered = resetUrl != null && await _resetEmails.SendAsync(user.Email, resetUrl, cancellationToken);
                    if (!delivered)
                    {
                        _context.PasswordResetTokens.Remove(token);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet, AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> ResetPassword(string? token, CancellationToken cancellationToken)
        {
            if (!TryHashResetToken(token, out var hash) || !await ActiveResetToken(hash).AnyAsync(cancellationToken))
                return View("ResetPasswordInvalid");
            ViewBag.Token = token;
            return View();
        }

        [HttpPost, AllowAnonymous, EnableRateLimiting("passwordRecovery")]
        public async Task<IActionResult> ResetPassword(string token, string password, string confirmPassword, CancellationToken cancellationToken)
        {
            if (password != confirmPassword || password.Length is < 8 or > 128 || !TryHashResetToken(token, out var hash))
            {
                ViewBag.Token = token;
                ViewBag.Error = "تحقق من تطابق كلمتي المرور وأن طولها بين 8 و128 حرفاً.";
                return View();
            }

            var reset = await ActiveResetToken(hash).Include(t => t.User).SingleOrDefaultAsync(cancellationToken);
            if (reset == null) return View("ResetPasswordInvalid");
            var now = DateTimeOffset.UtcNow;
            reset.User.PasswordHash = _passwords.Hash(reset.User, password);
            reset.User.Password = string.Empty;
            reset.User.SessionVersion++;
            var activeTokens = await _context.PasswordResetTokens.Where(t => t.UserId == reset.UserId && t.UsedAt == null).ToListAsync(cancellationToken);
            foreach (var item in activeTokens) item.UsedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["RecoverySuccess"] = "تم تعيين كلمة المرور الجديدة. يمكنك تسجيل الدخول الآن.";
            return RedirectToAction(nameof(Auth));
        }

        private IQueryable<PasswordResetToken> ActiveResetToken(byte[] hash) =>
            _context.PasswordResetTokens.Where(t => t.TokenHash.SequenceEqual(hash) && t.UsedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow);

        private string? BuildPublicUrl(string action,object values)
        {
            var configuredBase=_configuration["App:PublicBaseUrl"]?.TrimEnd('/');
            var relative=Url.Action(action,"Home",values);
            return Uri.TryCreate(configuredBase,UriKind.Absolute,out var baseUri)&&relative!=null?new Uri(baseUri,relative).AbsoluteUri:null;
        }

        private static bool TryHashResetToken(string? token, out byte[] hash)
        {
            hash = [];
            if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return false;
            try
            {
                var bytes = WebEncoders.Base64UrlDecode(token);
                if (bytes.Length != 32) return false;
                hash = SHA256.HashData(bytes);
                return true;
            }
            catch (FormatException) { return false; }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Auth");
        }

        private async Task SignInUserAsync(User user) =>
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                await AccountRoles.PrincipalAsync(user, _context), new AuthenticationProperties { IsPersistent = false });

        private async Task<IActionResult> LandingAsync(User user)
        {
            var principal=await AccountRoles.PrincipalAsync(user,_context);
            if(principal.IsInRole(AccountRoles.Admin)) return RedirectToAction("Index","Admin");
            if(principal.IsInRole(AccountRoles.Doctor)) return RedirectToAction("Dashboard","Doctor");
            if(principal.IsInRole(AccountRoles.Applicant))
                return await _context.Doctors.AsNoTracking().AnyAsync(d=>d.UserId==user.Id)
                    ? RedirectToAction("Status","Doctor")
                    : RedirectToAction("Apply","Doctor");
            return user.ProfileCompleted ? RedirectToAction("Index") : RedirectToAction("ProfileSetup");
        }
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var action=context.ActionDescriptor.RouteValues["action"];
            var publicOrAccountActions=new[] { "Auth", "Login", "SignUp", "Logout", "ForgotPassword", "ForgotPasswordConfirmation", "ResetPassword", "ViewShared", "DoctorInfo", "Error" };
            if(User.Identity?.IsAuthenticated==true && !User.IsInRole(AccountRoles.Patient) && !publicOrAccountActions.Contains(action))
            {
                var account=await AccountRoles.AuthenticationFields(_context.Users.AsNoTracking().Where(u=>u.Id==CurrentUserId)).SingleAsync();
                context.Result=await LandingAsync(account); return;
            }
            await next();
        }

        [HttpGet]
        public IActionResult ProfileSetup() => RedirectToAction("Index","Health");
        [HttpPost]
        public IActionResult ProfileSetup(string? unused) => RedirectToAction("Index","Health");

        // ─── Main Drug Checker ─────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Index()
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CheckInteraction(string drugName1, string drugName2)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            drugName1=(drugName1??"").Trim();drugName2=(drugName2??"").Trim();
            if (drugName1.Length is < 2 or > 100 || drugName2.Length is < 2 or > 100 ||
                string.Equals(drugName1,drugName2,StringComparison.OrdinalIgnoreCase))
            {
                ViewBag.Error = "أدخل اسمين مختلفين، من حرفين إلى 100 حرف لكل دواء.";
                return View("Index");
            }

            var user = await _context.Users.FindAsync(userId.Value);

            // Load saved medications
            var savedMeds = await _context.UserMedications
                .Where(m => m.UserId == userId && !m.IsStopped)
                .Select(m => m.DrugName)
                .ToListAsync();
            var clinicalContext=await LoadClinicalContextAsync(userId.Value,user);

            var task1 = _fdaService.GetDrugDataAsync(drugName1);
            var task2 = _fdaService.GetDrugDataAsync(drugName2);
            await Task.WhenAll(task1, task2);

            var res1 = await task1;
            var res2 = await task2;

            OpenFdaResult? drug1 = res1?.Results?.FirstOrDefault();
            OpenFdaResult? drug2 = res2?.Results?.FirstOrDefault();

            string? fdaInfo1 = drug1?.DrugInteractions != null
                ? string.Join(" ", drug1.DrugInteractions) : null;
            string? fdaInfo2 = drug2?.DrugInteractions != null
                ? string.Join(" ", drug2.DrugInteractions) : null;

            string displayName1 = drug1?.OpenFda?.BrandName?.FirstOrDefault()
                ?? drug1?.OpenFda?.GenericName?.FirstOrDefault()
                ?? drugName1;
            string displayName2 = drug2?.OpenFda?.BrandName?.FirstOrDefault()
                ?? drug2?.OpenFda?.GenericName?.FirstOrDefault()
                ?? drugName2;

            var aiResult = await _claudeService.AnalyzeDrugInteractionAsync(
                displayName1, displayName2,
                user?.Gender, user?.Age,
                clinicalContext, user?.IsPregnant,
                fdaInfo1, fdaInfo2,
                savedMeds.Count > 0 ? savedMeds : null);
            var evidence=InteractionEvidence.Assess(aiResult,drug1,drug2);
            var evaluatedAt=DateTimeOffset.UtcNow;
            var summary=string.IsNullOrWhiteSpace(aiResult.Summary)?evidence.Note:aiResult.Summary;

            var patientContext = BuildPatientSnapshot(user)+" | "+clinicalContext;

            var search = new Search
            {
                Drug1Name      = displayName1,
                Drug2Name      = displayName2,
                InteractionFound = evidence.Severity!="unknown" && aiResult.InteractionFound,
                ResultMessage  = summary,
                AiAnalysis     = aiResult.Details,
                Severity       = evidence.Severity,
                Recommendation = aiResult.Recommendation,
                PatientContext = patientContext,
                SafeAlternatives = aiResult.SafeAlternatives,
                UserId         = userId,
                CheckGroupId   = Guid.NewGuid(),
                CheckKind      = "pair",
                EvidenceStatus = evidence.Status,
                EvidenceNote   = evidence.Note,
                SourceSummary  = SourceSummary(evidence.Status),
                EvaluatedAt    = evaluatedAt
            };

            int? savedSearchId = null;
            try
            {
                _context.Searches.Add(search);
                await _context.SaveChangesAsync();
                savedSearchId = search.Id;
            }
            catch { /* don't fail the whole request if save fails */ }

            var viewModel = new InteractionAnalysis
            {
                Drug1               = drug1 ?? new OpenFdaResult(),
                Drug2               = drug2 ?? new OpenFdaResult(),
                InteractionFound    = evidence.Severity!="unknown" && aiResult.InteractionFound,
                AnalysisMessage     = summary,
                Severity            = evidence.Severity,
                AiDetails           = aiResult.Details,
                AiRecommendation    = aiResult.Recommendation,
                AiPatientNotes      = aiResult.PatientSpecificNotes,
                AiSafeAlternatives  = aiResult.SafeAlternatives,
                AiSavedMedConflicts = aiResult.SavedMedConflicts,
                DisplayDrug1        = displayName1,
                DisplayDrug2        = displayName2,
                AiSuccess           = aiResult.Success,
                EvaluationStatus    = evidence.Status,
                EvidenceNote        = evidence.Note,
                EvaluatedAt         = evaluatedAt,
                SearchId            = savedSearchId
            };

            return View("Result", viewModel);
        }

        // ─── Multi-Drug Interaction Checker ────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CheckMultiInteraction(List<string> drugNames)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            var drugs = (drugNames??[]).Select(d=>(d??"").Trim()).Where(d=>d.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (drugs.Count < 2 || drugs.Count>6 || drugs.Any(d=>d.Length is <2 or >100))
            {
                TempData["Error"] = "أدخل من دوائين إلى 6 أدوية مختلفة، وبحد أقصى 100 حرف للاسم.";
                return RedirectToAction("Index");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            var vm = new MultiDrugCheckViewModel { DrugNames = drugs };
            var savedMeds=await _context.UserMedications.Where(m=>m.UserId==userId && !m.IsStopped).Select(m=>m.DrugName).ToListAsync();
            var clinicalContext=await LoadClinicalContextAsync(userId.Value,user);
            var lookups=await Task.WhenAll(drugs.Select(async d=>(Name:d,Result:(await _fdaService.GetDrugDataAsync(d))?.Results?.FirstOrDefault())));
            var labels=lookups.ToDictionary(x=>x.Name,x=>x.Result,StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < drugs.Count; i++)
            {
                for (int j = i + 1; j < drugs.Count; j++)
                {
                    var d1 = drugs[i];
                    var d2 = drugs[j];

                    var r1=labels[d1];var r2=labels[d2];

                    string dn1 = r1?.OpenFda?.BrandName?.FirstOrDefault() ?? r1?.OpenFda?.GenericName?.FirstOrDefault() ?? d1;
                    string dn2 = r2?.OpenFda?.BrandName?.FirstOrDefault() ?? r2?.OpenFda?.GenericName?.FirstOrDefault() ?? d2;

                    string? fi1 = r1?.DrugInteractions != null ? string.Join(" ", r1.DrugInteractions) : null;
                    string? fi2 = r2?.DrugInteractions != null ? string.Join(" ", r2.DrugInteractions) : null;

                    var ai = await _claudeService.AnalyzeDrugInteractionAsync(
                        dn1, dn2,
                        user?.Gender, user?.Age,
                        clinicalContext, user?.IsPregnant,
                        fi1, fi2,savedMeds.Count>0?savedMeds:null);
                    var evidence=InteractionEvidence.Assess(ai,r1,r2);

                    vm.Results.Add(new DrugPairResult
                    {
                        Drug1           = dn1,
                        Drug2           = dn2,
                        Severity        = evidence.Severity,
                        Summary         = string.IsNullOrWhiteSpace(ai.Summary)?evidence.Note:ai.Summary,
                        Details         = ai.Details,
                        Recommendation  = ai.Recommendation,
                        SafeAlternatives = ai.SafeAlternatives,
                        InteractionFound = evidence.Severity!="unknown" && ai.InteractionFound,
                        EvaluationStatus=evidence.Status,EvidenceNote=evidence.Note,EvaluatedAt=DateTimeOffset.UtcNow
                    });
                }
            }

            vm.OverallSeverity=InteractionSeverity.Overall(vm.Results.Select(r=>r.Severity));

            var groupId=Guid.NewGuid();var patientSnapshot=BuildPatientSnapshot(user)+" | "+clinicalContext;
            try
            {
                foreach(var result in vm.Results)
                    _context.Searches.Add(new Search {
                        Drug1Name=result.Drug1,Drug2Name=result.Drug2,InteractionFound=result.InteractionFound,
                        ResultMessage=result.Summary,AiAnalysis=result.Details,Severity=result.Severity,
                        Recommendation=result.Recommendation,SafeAlternatives=result.SafeAlternatives,
                        PatientContext=patientSnapshot,UserId=userId,CheckGroupId=groupId,CheckKind="multi",
                        EvidenceStatus=result.EvaluationStatus,EvidenceNote=result.EvidenceNote,
                        SourceSummary=SourceSummary(result.EvaluationStatus),EvaluatedAt=result.EvaluatedAt,
                        SearchDate=result.EvaluatedAt.LocalDateTime
                    });
                await _context.SaveChangesAsync();
            }
            catch(DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                TempData["HistoryWarning"]="ظهرت النتيجة، لكن تعذر حفظها في السجل.";
            }

            return View("MultiResult", vm);
        }

        // ─── Drug Info Page ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DrugInfo(string name)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            if (string.IsNullOrWhiteSpace(name))
                return View(new DrugInfoResult { QueryName = "", Success = false, ErrorMessage = "يرجى إدخال اسم دواء" });

            name = name.Trim();
            if (name.Length is < 2 or > 100)
                return View(new DrugInfoResult { QueryName = name, Success = false, ErrorMessage = "يجب أن يكون اسم الدواء بين حرفين و100 حرف." });

            var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId.Value);
            var savedMeds = await _context.UserMedications
                .AsNoTracking()
                .Where(m => m.UserId == userId.Value && !m.IsStopped)
                .Select(m => m.DrugName)
                .ToListAsync();

            var clinicalContext = await LoadClinicalContextAsync(userId.Value, user);
            var aiTask = _claudeService.GetDrugInfoAsync(name, clinicalContext + " | " + BuildPatientSnapshot(user), savedMeds);
            var fdaTask = _fdaService.GetDrugDataAsync(name);
            await Task.WhenAll(aiTask, fdaTask);
            var jsonText = await aiTask;
            var fdaResult = (await fdaTask)?.Results?.FirstOrDefault();
            var fdaInteractions = fdaResult?.DrugInteractions?
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

            var result = new DrugInfoResult
            {
                QueryName = name,
                FdaAvailable = fdaResult != null,
                FdaInteractions = fdaInteractions,
                EvaluatedAt = DateTimeOffset.UtcNow
            };
            try
            {
                var jsonStr = ExtractJsonBrace(jsonText);
                using var doc = JsonDocument.Parse(jsonStr);
                var r = doc.RootElement;

                // Check for error returned by the service
                if (r.TryGetProperty("__error", out var errProp))
                {
                    result.ErrorMessage = errProp.GetString() ?? "تعذر الحصول على التحليل المساعد.";
                }
                else
                {
                result.Name               = GetStr(r, "name",              name);
                result.Category           = GetStr(r, "category",          "");
                result.Purpose            = GetStr(r, "purpose",           "");
                result.Mechanism          = GetStr(r, "mechanism",         "");
                result.SideEffects        = GetStr(r, "sideEffects",       "");
                result.Contraindications  = GetStr(r, "contraindications", "");
                result.PregnancySafety    = GetStr(r, "pregnancySafety",   "");
                result.KeyInteractions    = GetStr(r, "keyInteractions",   "");
                if (string.IsNullOrWhiteSpace(result.KeyInteractions))
                    result.KeyInteractions = GetStr(r, "keyInterinteractions", "");
                result.SavedMedConflicts  = GetStr(r, "savedMedConflicts", "");
                result.ConditionWarnings  = GetStr(r, "conditionWarnings", "");
                result.DosageNotes        = GetStr(r, "dosageNotes",       "");

                // If all main content fields are empty → API call silently failed
                bool isEmpty = string.IsNullOrEmpty(result.Purpose) &&
                               string.IsNullOrEmpty(result.Mechanism) &&
                               string.IsNullOrEmpty(result.SideEffects);
                result.AiAvailable = !isEmpty;
                }
            }
            catch (JsonException)
            {
                result.ErrorMessage = "وصلت استجابة تحليلية غير مكتملة؛ عُرضت البيانات المرجعية المتاحة فقط.";
            }

            result.Success = result.AiAvailable || result.FdaAvailable;
            result.EvidenceStatus = result.AiAvailable && result.FdaAvailable ? "complete"
                : result.Success ? "partial" : "unavailable";
            result.SourceSummary = result.EvidenceStatus switch
            {
                "complete" => "تحليل مساعد مع نشرة دوائية مرجعية",
                "partial" when result.FdaAvailable => "نشرة دوائية مرجعية فقط؛ التحليل المساعد غير متاح",
                "partial" => "تحليل مساعد فقط؛ لم تُوجد نشرة مرجعية مطابقة",
                _ => "لم يتوفر أي مصدر الآن"
            };
            result.EvidenceNote = result.EvidenceStatus switch
            {
                "complete" when string.IsNullOrWhiteSpace(result.FdaInteractions) => "عُثر على النشرة، لكن قسم التداخلات فيها غير متوفر أو غير منظم؛ لا يعني ذلك أن الدواء آمن.",
                "complete" => "تتوفر نتيجة من المصدرين، لكن يجب مراجعة الطبيب أو الصيدلاني قبل تغيير العلاج.",
                "partial" => "المعلومات جزئية ولا تكفي للحكم على الأمان أو نفي التداخلات.",
                _ => "تعذر التحقق حالياً؛ لا تستخدم غياب النتيجة لاتخاذ قرار علاجي."
            };
            if (!result.Success)
                result.ErrorMessage = "تعذر الحصول على معلومات موثوقة حالياً. حاول لاحقاً أو استشر صيدلانياً.";

            return View(result);
        }

        // ─── Drug Autocomplete (AJAX) ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DrugSuggest(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Json(new List<string>());

            var suggestions = await _fdaService.GetDrugSuggestionsAsync(q);
            return Json(suggestions);
        }

        // ─── Pregnancy Dangerous Drugs ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> PregnancyDrugs()
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var jsonText = await _claudeService.GetPregnancyDangerousDrugsAsync(timeout.Token);

            var drugs = new List<PregnancyDrugItem>();
            try
            {
                var jsonStr = ExtractJsonArray(jsonText);
                using var doc = JsonDocument.Parse(jsonStr);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    drugs.Add(new PregnancyDrugItem
                    {
                        Name        = GetStr(el, "name",        ""),
                        Category    = GetStr(el, "category",    ""),
                        Risk        = GetStr(el, "risk",        ""),
                        Alternative = GetStr(el, "alternative", ""),
                        Severity    = GetStr(el, "severity",    "serious"),
                    });
                }
            }
            catch (JsonException) { drugs.Clear(); }
            drugs = drugs.Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Risk)).ToList();
            foreach (var drug in drugs)
                if (drug.Severity is not ("critical" or "serious" or "moderate")) drug.Severity = "serious";

            return View(drugs);
        }

        // ─── My Medications (saved list) ───────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> MyMedications()
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            var meds = await _context.UserMedications
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            var today = DoseSchedule.Now.Date;
            var since = today.AddDays(-6);
            var recent = await _context.DoseRecords.AsNoTracking()
                .Where(d => d.UserId == userId && d.Day >= since && d.Day <= today &&
                    (d.Status == "taken" || d.Status == "skipped"))
                .Select(d => d.Status)
                .ToListAsync();
            var taken = recent.Count(status => status == "taken");
            var skipped = recent.Count - taken;

            ViewBag.HealthReview = (await HttpContext.RequestServices.GetRequiredService<HealthPageService>().LoadAsync(userId.Value)).Review;
            return View(new MedicationDashboard
            {
                Medications = meds,
                TakenLast7Days = taken,
                SkippedLast7Days = skipped,
                RecordedAdherencePercent = taken + skipped == 0 ? null : decimal.Round(taken * 100m / (taken + skipped), 0)
            });
        }

        [HttpPost]
        public async Task<IActionResult> AddMedication(string drugName, string? notes)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            if (string.IsNullOrWhiteSpace(drugName) || drugName.Length > 200 || (notes?.Length ?? 0)>1000)
            {
                TempData["MedError"] = "يرجى إدخال اسم الدواء";
                return RedirectToAction("MyMedications");
            }

            var exists = await _context.UserMedications
                .AnyAsync(m => m.UserId == userId && m.DrugName.ToLower() == drugName.Trim().ToLower());
            if (exists)
            {
                TempData["MedError"] = "هذا الدواء موجود بالفعل في قائمتك";
                return RedirectToAction("MyMedications");
            }

            _context.UserMedications.Add(new UserMedication
            {
                DrugName = drugName.Trim(),
                Notes    = notes?.Trim(),
                UserId   = userId.Value
            });
            await _context.SaveChangesAsync();

            TempData["MedSuccess"] = $"تم إضافة \"{drugName.Trim()}\" إلى قائمتك";
            return RedirectToAction("MyMedications");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMedication(int id)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            var med = await _context.UserMedications
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (med != null)
            {
                _context.UserMedications.Remove(med);
                await _context.SaveChangesAsync();
                TempData["MedSuccess"] = $"تم حذف \"{med.DrugName}\"";
            }

            return RedirectToAction("MyMedications");
        }

        [HttpGet]
        public async Task<IActionResult> GetMedicationsJson()
        {
            var userId = CurrentUserId;
            if (userId == null) return Json(new List<object>());

            var meds = await _context.UserMedications
                .Where(m => m.UserId == userId && !m.IsStopped)
                .Select(m => new { m.Id, m.DrugName, m.Notes })
                .ToListAsync();

            return Json(meds);
        }

        // ─── AI Follow-up Chat (AJAX) ──────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AiChat(string question, string context)
        {
            var userId = CurrentUserId;
            if (userId == null) return Json(new { success = false, answer = "غير مصرح" });

            if (string.IsNullOrWhiteSpace(question))
                return Json(new { success = false, answer = "يرجى كتابة سؤالك" });

            var answer = await _claudeService.AskFollowUpAsync(question, context ?? "");
            return Json(new { success = true, answer });
        }

        [HttpPost, Authorize(Roles = "Patient"), EnableRateLimiting("aiChat")]
        public async Task<IActionResult> AssistantChat([FromBody] AssistantChatRequest request)
        {
            var userId = CurrentUserId;
            if (userId == null) return Unauthorized(new { success = false, answer = "انتهت الجلسة. سجّل الدخول مجدداً." });
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, answer = "تحقق من السؤال ثم حاول مجدداً." });

            var pageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Home/Index"] = "صفحة فحص التداخلات الدوائية",
                ["Home/Result"] = "صفحة نتيجة فحص التداخلات الدوائية",
                ["Home/DrugInfo"] = "صفحة معلومات الدواء",
                ["Home/MyMedications"] = "صفحة قائمة أدوية المريض",
                ["Doses/Index"] = "صفحة جرعات اليوم",
                ["Health/Index"] = "صفحة الملف الصحي",
                ["Care/Index"] = "صفحة الطبيب المتابع",
                ["Notifications/Index"] = "صفحة إشعارات العلاج"
            };

            var safePage = pageNames.TryGetValue(request.PageKey ?? "", out var pageName)
                ? pageName : "إحدى صفحات Digital Pharma";
            var contextParts = new List<string> { $"الصفحة الحالية: {safePage}." };

            if (request.Scope == "drug")
            {
                var drugName = (request.DrugName ?? "").Trim();
                if (drugName.Length is < 2 or > 120)
                    return BadRequest(new { success = false, answer = "تعذر تحديد الدواء الحالي." });

                contextParts.Add($"المحادثة مخصصة للدواء الحالي: {drugName}.");
                var fda = await _fdaService.GetDrugDataAsync(drugName);
                var labelInteractions = fda?.Results?
                    .SelectMany(item => item.DrugInteractions ?? [])
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                contextParts.Add(string.IsNullOrWhiteSpace(labelInteractions)
                    ? "لم يتوفر في نتيجة النشرة الحالية نص منظم للتداخلات؛ لا تستنتج الأمان من ذلك."
                    : $"مقتطف تداخلات من النشرة المرجعية: {labelInteractions[..Math.Min(900, labelInteractions.Length)]}");
            }
            else if (safePage == "صفحة معلومات الدواء" && !string.IsNullOrWhiteSpace(request.DrugName))
            {
                var visibleDrug = request.DrugName.Trim();
                if (visibleDrug.Length <= 120)
                    contextParts.Add($"الدواء الظاهر حالياً في الصفحة: {visibleDrug}.");
            }

            if (request.IncludeHealth)
            {
                var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId.Value);
                if (user != null)
                {
                    var medications = await _context.UserMedications.AsNoTracking()
                        .Where(m => m.UserId == userId && !m.IsStopped)
                        .Select(m => m.DrugName).Take(30).ToListAsync();
                    var conditions = await _context.PatientConditions.AsNoTracking()
                        .Where(c => c.UserId == userId).Select(c => c.Code).Take(30).ToListAsync();
                    var allergies = await _context.PatientAllergies.AsNoTracking()
                        .Where(a => a.UserId == userId).Select(a => a.Name).Take(30).ToListAsync();

                    contextParts.Add("وافق المستخدم على استخدام الحد الأدنى من معلوماته الصحية لهذا السؤال.");
                    contextParts.Add($"العمر: {user.Age}؛ الجنس: {user.Gender ?? "غير محدد"}؛ الحمل: {(user.IsPregnant == true ? "نعم" : "غير مثبت")}.");
                    contextParts.Add($"الحالات: {(conditions.Count > 0 ? string.Join("، ", conditions) : "غير مسجلة")}.");
                    contextParts.Add($"الحساسيات: {(allergies.Count > 0 ? string.Join("، ", allergies) : "غير مسجلة")}.");
                    contextParts.Add($"الأدوية الحالية: {(medications.Count > 0 ? string.Join("، ", medications) : "غير مسجلة")}.");
                }
            }
            else
            {
                contextParts.Add("لم يوافق المستخدم على إرسال معلومات ملفه الصحي في هذا السؤال؛ لا تفترض أي مرض أو دواء شخصي.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(28));
            var answer = await _claudeService.AskMedicationAssistantAsync(
                request.Question, string.Join("\n", contextParts), request.Conversation, timeout.Token);
            return Json(new { success = true, answer });
        }

        // ─── Share Result ──────────────────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ViewShared(string token)
        {
            if (!TryHashResetToken(token,out var hash)) { ViewBag.NotFound=true; return View("SharedResult",null); }
            var now=DateTimeOffset.UtcNow;
            var search = await _context.Searches.AsNoTracking().FirstOrDefaultAsync(s =>
                s.ShareTokenHash!=null && s.ShareTokenHash.SequenceEqual(hash) && s.ShareRevokedAt==null && s.ShareExpiresAt>now);

            if (search == null)
            {
                ViewBag.NotFound = true;
                return View("SharedResult", null);
            }

            return View("SharedResult", search);
        }

        [HttpGet,Authorize(Roles=AccountRoles.Patient)]
        public async Task<IActionResult> ManageShare(int id)
        {
            var search=await _context.Searches.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==id && s.UserId==CurrentUserId);
            if(search==null)return NotFound();
            return View(new ShareManagementPage{SearchId=search.Id,Drug1Name=search.Drug1Name,Drug2Name=search.Drug2Name,
                IsActive=search.ShareTokenHash!=null&&search.ShareRevokedAt==null&&search.ShareExpiresAt>DateTimeOffset.UtcNow,
                ExpiresAt=search.ShareExpiresAt,NewShareUrl=TempData["NewShareUrl"] as string});
        }

        [HttpPost,Authorize(Roles=AccountRoles.Patient)]
        public async Task<IActionResult> CreateShare(int id,int durationDays)
        {
            if(durationDays is not (1 or 7 or 30))return BadRequest();
            var search=await _context.Searches.SingleOrDefaultAsync(s=>s.Id==id && s.UserId==CurrentUserId);
            if(search==null)return NotFound();
            var raw=RandomNumberGenerator.GetBytes(32);var token=WebEncoders.Base64UrlEncode(raw);
            search.ShareToken=null;search.ShareTokenHash=SHA256.HashData(raw);search.ShareExpiresAt=DateTimeOffset.UtcNow.AddDays(durationDays);search.ShareRevokedAt=null;
            await _context.SaveChangesAsync();
            TempData["NewShareUrl"]=BuildPublicUrl(nameof(ViewShared),new{token})?.ToString();
            TempData["Success"]="تم إنشاء رابط جديد. انسخه الآن؛ لن نعرض الرمز نفسه مرة أخرى بعد مغادرة الصفحة.";
            return RedirectToAction(nameof(ManageShare),new{id});
        }

        [HttpPost,Authorize(Roles=AccountRoles.Patient)]
        public async Task<IActionResult> RevokeShare(int id)
        {
            var search=await _context.Searches.SingleOrDefaultAsync(s=>s.Id==id && s.UserId==CurrentUserId);
            if(search==null)return NotFound();
            search.ShareRevokedAt=DateTimeOffset.UtcNow;search.ShareTokenHash=null;search.ShareToken=null;
            await _context.SaveChangesAsync();TempData["Success"]="تم إلغاء رابط المشاركة فوراً.";
            return RedirectToAction(nameof(ManageShare),new{id});
        }

        // ─── Search History ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> History(int page=1)
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            page=Math.Clamp(page,1,100000);
            var searches = await _context.Searches.AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.SearchDate)
                .Take(1000)
                .ToListAsync();
            var grouped=searches.GroupBy(s=>s.CheckGroupId.HasValue?$"group:{s.CheckGroupId}":$"legacy:{s.Id}")
                .Select(g=>new InteractionHistoryGroup {
                    Key=g.Key,Kind=g.Any(s=>s.CheckKind=="multi")?"multi":"pair",Results=g.OrderByDescending(s=>s.SearchDate).ToList(),
                    Severity=OverallSeverity(g.Select(s=>s.Severity)),
                    EvidenceStatus=g.All(s=>s.EvidenceStatus=="complete")?"complete":g.Any(s=>s.EvidenceStatus=="unavailable")?"unavailable":g.Any(s=>s.EvidenceStatus=="partial")?"partial":"legacy",
                    EvaluatedAt=g.Max(s=>s.EvaluatedAt??new DateTimeOffset(s.SearchDate))
                }).OrderByDescending(g=>g.EvaluatedAt).ToList();
            const int size=20;var pageGroups=grouped.Skip((page-1)*size).Take(size+1).ToList();
            return View(new InteractionHistoryPage {Page=page,HasNext=pageGroups.Count>size,Groups=pageGroups.Take(size).ToList()});
        }

        // ─── Account / Dashboard ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Account()
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Auth");

            var accountSearches=await _context.Searches.AsNoTracking().Where(s=>s.UserId==userId).Select(s=>new{s.Id,s.CheckGroupId,s.Severity}).ToListAsync();
            var accountGroups=accountSearches.GroupBy(s=>s.CheckGroupId.HasValue?$"group:{s.CheckGroupId}":$"legacy:{s.Id}").ToList();
            ViewBag.SearchCount=accountGroups.Count;
            ViewBag.DangerCount=accountGroups.Count(g=>g.Any(s=>s.Severity is "serious" or "critical"));
            var activeMeds=await _context.UserMedications.AsNoTracking().Where(m=>m.UserId==userId && !m.IsStopped).ToListAsync();
            ViewBag.MedCount=activeMeds.Count;
            ViewBag.AlertCount=(await _patientAlerts.CurrentAsync(userId.Value)).Count;
            var since=DateTime.Today.AddDays(-6);
            var doseStates=await _context.DoseRecords.AsNoTracking().Where(d=>d.UserId==userId && d.Day>=since).Select(d=>d.Status).ToListAsync();
            ViewBag.TakenCount=doseStates.Count(s=>s=="taken");
            ViewBag.SkippedCount=doseStates.Count(s=>s=="skipped");
            ViewBag.DoctorName=await _context.CareLinks.AsNoTracking().Where(l=>l.PatientId==userId&&l.Status=="active").Select(l=>l.Doctor.FullName).FirstOrDefaultAsync();
            var now=DoseSchedule.Now;
            var recorded=await _context.DoseRecords.AsNoTracking().Where(d=>d.UserId==userId&&d.Day==now.Date).Select(d=>new{d.MedicationId,d.Time}).ToListAsync();
            var recordedKeys=recorded.Select(d=>(d.MedicationId,d.Time)).ToHashSet();
            ViewBag.NextDose=activeMeds.SelectMany(m=>DoseSchedule.Times(m).Select(t=>new{m.Id,m.DrugName,Time=t}))
                .Where(x=>string.CompareOrdinal(x.Time,now.ToString("HH:mm"))>=0&&!recordedKeys.Contains((x.Id,x.Time)))
                .OrderBy(x=>x.Time).Select(x=>$"{x.DrugName} · {x.Time}").FirstOrDefault();

            return View(user);
        }

        [HttpGet,Authorize(Roles=AccountRoles.Patient)]
        public async Task<IActionResult> Activity(string filter="all",int page=1)
        {
            if(filter is not ("all" or "medication" or "dose" or "treatment" or "care" or "check"))filter="all";
            page=Math.Clamp(page,1,10000);var id=CurrentUserId!.Value;var items=new List<AccountActivityItem>();
            if(filter is "all" or "medication")
            {
                var rows=await _context.MedicationChanges.AsNoTracking().Include(c=>c.Medication).Where(c=>c.Medication.UserId==id).OrderByDescending(c=>c.CreatedAt).Take(250).ToListAsync();
                items.AddRange(rows.Select(c=>new AccountActivityItem(c.CreatedAt,"medication","تغيير في دواء",c.Medication.DrugName+" — "+c.Reason,"fa-pills","#fbbf24")));
            }
            if(filter is "all" or "dose")
            {
                var rows=await _context.DoseChanges.AsNoTracking().Include(c=>c.Record).Where(c=>c.Record.UserId==id).OrderByDescending(c=>c.ChangedAt).Take(250).ToListAsync();
                items.AddRange(rows.Select(c=>new AccountActivityItem(c.ChangedAt,"dose","تصحيح تسجيل جرعة",c.Record.DrugName+" — "+c.Reason,"fa-clock-rotate-left","#60a5fa")));
            }
            if(filter is "all" or "treatment")
            {
                var rows=await _context.TreatmentChanges.AsNoTracking().Where(c=>c.PatientId==id).OrderByDescending(c=>c.CreatedAt).Take(250).ToListAsync();
                items.AddRange(rows.Select(c=>new AccountActivityItem(c.CreatedAt,"treatment","تعديل من الطبيب",c.DoctorName+" — "+c.Reason,"fa-user-doctor","#00c9a7")));
            }
            if(filter is "all" or "care")
            {
                var rows=await _context.CareLinkEvents.AsNoTracking().Include(e=>e.Link).Where(e=>e.Link.PatientId==id).OrderByDescending(e=>e.CreatedAt).Take(250).ToListAsync();
                items.AddRange(rows.Select(e=>new AccountActivityItem(e.CreatedAt,"care","تغيير في ربط الطبيب",CareStatus.Label(e.Status),"fa-link","#a78bfa")));
            }
            if(filter is "all" or "check")
            {
                var rows=await _context.Searches.AsNoTracking().Where(s=>s.UserId==id).OrderByDescending(s=>s.EvaluatedAt).Take(250).ToListAsync();
                items.AddRange(rows.Select(s=>new AccountActivityItem(s.EvaluatedAt??new DateTimeOffset(s.SearchDate),"check","فحص تداخلات",s.Drug1Name+" × "+s.Drug2Name,"fa-magnifying-glass","#fb7185")));
            }
            const int size=30;var ordered=items.OrderByDescending(i=>i.At).Skip((page-1)*size).Take(size+1).ToList();
            return View(new AccountActivityPage{Items=ordered.Take(size).ToList(),Filter=filter,Page=page,HasNext=ordered.Count>size});
        }

        [HttpPost]
        public IActionResult UpdateProfile() => RedirectToAction("Index","Health");

        // Compatibility route: old links lead to the reviewed application workflow.
        [HttpGet]
        public IActionResult DoctorInfo() => RedirectToAction("Apply", "Doctor");
        [HttpPost]
        public IActionResult DoctorInfoLegacy() => BadRequest("استخدم نموذج طلب اعتماد الطبيب الجديد.");

        // ─── Gemini API Diagnostic ─────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles="Admin")]
        public async Task<IActionResult> TestApi()
        {
            var userId = CurrentUserId;
            if (userId == null) return RedirectToAction("Auth");

            try
            {
                var answer = await _claudeService.AskFollowUpAsync(
                    "قل فقط: مرحبا، أنا أعمل بشكل صحيح.",
                    "اختبار الاتصال");
                return Content($"✅ خدمة التحليل تعمل.\nالرد: {answer}", "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Content($"❌ خطأ في خدمة التحليل:\n{ex.Message}", "text/plain; charset=utf-8");
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────
        private static string SourceSummary(string evidenceStatus)=>evidenceStatus switch
        {
            "complete"=>"التحليل المساعد + أقسام التداخلات المرجعية",
            "partial"=>"التحليل المساعد + بيانات مرجعية جزئية",
            _=>"مصادر غير مكتملة"
        };

        private static string OverallSeverity(IEnumerable<string?> values)
            => InteractionSeverity.Overall(values);

        private async Task<string> LoadClinicalContextAsync(int userId,User? user)
        {
            var conditionCodes=await _context.PatientConditions.AsNoTracking().Where(c=>c.UserId==userId).Select(c=>c.Code).ToListAsync();
            var conditions=HealthCatalog.Conditions.Where(c=>conditionCodes.Contains(c.Code)).Select(c=>c.Name).ToList();
            if(!string.IsNullOrWhiteSpace(user?.MedicalConditions))conditions.Add(user.MedicalConditions.Trim());
            var allergies=await _context.PatientAllergies.AsNoTracking().Where(a=>a.UserId==userId).Take(20).ToListAsync();
            var parts=new List<string>();
            if(conditions.Count>0)parts.Add("الحالات الصحية المسجلة: "+string.Join("، ",conditions.Distinct()));
            if(allergies.Count>0)parts.Add("الحساسيات المسجلة: "+string.Join("، ",allergies.Select(a=>$"{a.Name} (المادة المرتبطة: {a.SubstanceKey??"غير محددة"}؛ التفاعل: {a.Reaction}؛ الشدة المبلغ عنها: {a.ReportedSeverity})")));
            return parts.Count==0?"لا توجد حالات أو حساسيات مسجلة؛ لا يعني ذلك عدم وجودها.":string.Join(" | ",parts);
        }

        private static string BuildPatientSnapshot(User? user)
        {
            if (user == null) return "غير محدد";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(user.Gender))
                parts.Add(user.Gender == "female" ? "أنثى" : "ذكر");
            parts.Add($"العمر: {user.Age}");
            if (!string.IsNullOrEmpty(user.MedicalConditions))
                parts.Add(user.MedicalConditions);
            if (user.IsPregnant == true) parts.Add("حامل");
            return string.Join(" | ", parts);
        }

        private static string GetStr(JsonElement el, string prop, string fallback)
        {
            if (!el.TryGetProperty(prop, out var value)) return fallback;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Array => string.Join("، ", value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))),
                JsonValueKind.Null or JsonValueKind.Undefined => fallback,
                _ => value.ToString()
            };
        }

        private static string ExtractJsonBrace(string text)
        {
            text = text.Trim();
            var s = text.IndexOf('{');
            var e = text.LastIndexOf('}');
            return (s >= 0 && e > s) ? text[s..(e + 1)] : "{}";
        }

        private static string ExtractJsonArray(string text)
        {
            text = text.Trim();
            var s = text.IndexOf('[');
            var e = text.LastIndexOf(']');
            return (s >= 0 && e > s) ? text[s..(e + 1)] : "[]";
        }
    }
}
