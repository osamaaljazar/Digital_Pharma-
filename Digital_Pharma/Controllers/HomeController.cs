using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalPharma.MVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly OpenFdaService _fdaService;
        private readonly GeminiAiService _claudeService;
        private readonly PharmaDbContext _context;

        public HomeController(PharmaDbContext context, OpenFdaService fdaService, GeminiAiService claudeService)
        {
            _context = context;
            _fdaService = fdaService;
            _claudeService = claudeService;
        }

        // ─── Landing / Auth ────────────────────────────────────────────────────
        public IActionResult Welcome() => View();

        [HttpGet]
        public IActionResult Auth()
        {
            if (HttpContext.Session.GetInt32("UserId") != null)
                return RedirectToAction("Index");
            return View();
        }

        // ─── Register ──────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SignUp(string userName, string email, string password,
            string phoneNumber, DateTime dateOfBirth)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(password))
            {
                TempData["SignupError"] = "يرجى ملء جميع الحقول المطلوبة";
                return RedirectToAction("Auth");
            }

            var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
            if (exists)
            {
                TempData["SignupError"] = "هذا البريد الإلكتروني مسجّل مسبقاً";
                return RedirectToAction("Auth");
            }

            var user = new User
            {
                UserName    = userName,
                Email       = email.Trim().ToLower(),
                Password    = password,
                PasswordHash = HashPassword(password),
                PhoneNumber = phoneNumber,
                DateOfBirth = dateOfBirth == default ? DateTime.Today.AddYears(-25) : dateOfBirth,
                ProfileCompleted = false
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserName", user.UserName);

            return RedirectToAction("ProfileSetup");
        }

        // ─── Login ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                TempData["LoginError"] = "يرجى إدخال البريد الإلكتروني وكلمة المرور";
                return RedirectToAction("Auth");
            }

            var hashed = HashPassword(password);
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower()
                                       && u.PasswordHash == hashed);

            if (user == null)
            {
                TempData["LoginError"] = "البريد الإلكتروني أو كلمة المرور غير صحيحة";
                return RedirectToAction("Auth");
            }

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserName", user.UserName);

            return user.ProfileCompleted
                ? RedirectToAction("Index")
                : RedirectToAction("ProfileSetup");
        }

        // ─── Logout ────────────────────────────────────────────────────────────
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Auth");
        }

        // ─── Profile Setup ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult ProfileSetup()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ProfileSetup(string gender, string medicalConditions,
            bool? isMarried, bool? isPregnant)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Auth");

            user.Gender            = gender;
            user.MedicalConditions = medicalConditions;
            user.IsMarried         = (gender == "female") ? isMarried : null;
            user.IsPregnant        = (gender == "female" && isMarried == true) ? isPregnant : null;
            user.ProfileCompleted  = true;

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        // ─── Main Drug Checker ─────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CheckInteraction(string drugName1, string drugName2)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            if (string.IsNullOrWhiteSpace(drugName1) || string.IsNullOrWhiteSpace(drugName2))
            {
                ViewBag.Error = "يرجى إدخال اسم الدوائين للفحص";
                return View("Index");
            }

            var user = await _context.Users.FindAsync(userId.Value);

            // Load saved medications
            var savedMeds = await _context.UserMedications
                .Where(m => m.UserId == userId)
                .Select(m => m.DrugName)
                .ToListAsync();

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
                user?.MedicalConditions, user?.IsPregnant,
                fdaInfo1, fdaInfo2,
                savedMeds.Count > 0 ? savedMeds : null);

            var shareToken = GenerateToken();
            var patientContext = BuildPatientSnapshot(user);

            var search = new Search
            {
                Drug1Name      = displayName1,
                Drug2Name      = displayName2,
                InteractionFound = aiResult.InteractionFound,
                ResultMessage  = aiResult.Summary,
                AiAnalysis     = aiResult.Details,
                Severity       = aiResult.Severity,
                Recommendation = aiResult.Recommendation,
                PatientContext = patientContext,
                SafeAlternatives = aiResult.SafeAlternatives,
                ShareToken     = shareToken,
                UserId         = userId
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
                InteractionFound    = aiResult.InteractionFound,
                AnalysisMessage     = aiResult.Summary,
                Severity            = aiResult.Severity,
                AiDetails           = aiResult.Details,
                AiRecommendation    = aiResult.Recommendation,
                AiPatientNotes      = aiResult.PatientSpecificNotes,
                AiSafeAlternatives  = aiResult.SafeAlternatives,
                AiSavedMedConflicts = aiResult.SavedMedConflicts,
                DisplayDrug1        = displayName1,
                DisplayDrug2        = displayName2,
                AiSuccess           = aiResult.Success,
                SearchId            = savedSearchId,
                ShareToken          = shareToken
            };

            return View("Result", viewModel);
        }

        // ─── Multi-Drug Interaction Checker ────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CheckMultiInteraction(List<string> drugNames)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var drugs = drugNames.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
            if (drugs.Count < 2)
            {
                TempData["Error"] = "يرجى إدخال دوائين على الأقل";
                return RedirectToAction("Index");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            var vm = new MultiDrugCheckViewModel { DrugNames = drugs };

            for (int i = 0; i < drugs.Count; i++)
            {
                for (int j = i + 1; j < drugs.Count; j++)
                {
                    var d1 = drugs[i];
                    var d2 = drugs[j];

                    var t1 = _fdaService.GetDrugDataAsync(d1);
                    var t2 = _fdaService.GetDrugDataAsync(d2);
                    await Task.WhenAll(t1, t2);

                    var r1 = (await t1)?.Results?.FirstOrDefault();
                    var r2 = (await t2)?.Results?.FirstOrDefault();

                    string dn1 = r1?.OpenFda?.BrandName?.FirstOrDefault() ?? r1?.OpenFda?.GenericName?.FirstOrDefault() ?? d1;
                    string dn2 = r2?.OpenFda?.BrandName?.FirstOrDefault() ?? r2?.OpenFda?.GenericName?.FirstOrDefault() ?? d2;

                    string? fi1 = r1?.DrugInteractions != null ? string.Join(" ", r1.DrugInteractions) : null;
                    string? fi2 = r2?.DrugInteractions != null ? string.Join(" ", r2.DrugInteractions) : null;

                    var ai = await _claudeService.AnalyzeDrugInteractionAsync(
                        dn1, dn2,
                        user?.Gender, user?.Age,
                        user?.MedicalConditions, user?.IsPregnant,
                        fi1, fi2);

                    vm.Results.Add(new DrugPairResult
                    {
                        Drug1           = dn1,
                        Drug2           = dn2,
                        Severity        = ai.Severity,
                        Summary         = ai.Summary,
                        Details         = ai.Details,
                        Recommendation  = ai.Recommendation,
                        SafeAlternatives = ai.SafeAlternatives,
                        InteractionFound = ai.InteractionFound
                    });
                }
            }

            var sevOrder = new[] { "safe", "caution", "moderate", "serious", "critical" };
            vm.OverallSeverity = vm.Results
                .Select(r => r.Severity?.ToLower() ?? "safe")
                .OrderByDescending(s => Array.IndexOf(sevOrder, s))
                .FirstOrDefault() ?? "safe";

            return View("MultiResult", vm);
        }

        // ─── Drug Info Page ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DrugInfo(string name)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            if (string.IsNullOrWhiteSpace(name))
                return View(new DrugInfoResult { QueryName = "", Success = false, ErrorMessage = "يرجى إدخال اسم دواء" });

            var user = await _context.Users.FindAsync(userId.Value);
            var savedMeds = await _context.UserMedications
                .Where(m => m.UserId == userId)
                .Select(m => m.DrugName)
                .ToListAsync();

            var jsonText = await _claudeService.GetDrugInfoAsync(name, user?.MedicalConditions, savedMeds);

            var result = new DrugInfoResult { QueryName = name };
            try
            {
                var jsonStr = ExtractJsonBrace(jsonText);
                using var doc = JsonDocument.Parse(jsonStr);
                var r = doc.RootElement;

                // Check for error returned by the service
                if (r.TryGetProperty("__error", out var errProp))
                {
                    result.Success      = false;
                    result.ErrorMessage = errProp.GetString() ?? "خطأ غير معروف من Gemini API";
                    return View(result);
                }

                result.Name               = GetStr(r, "name",              name);
                result.Category           = GetStr(r, "category",          "");
                result.Purpose            = GetStr(r, "purpose",           "");
                result.Mechanism          = GetStr(r, "mechanism",         "");
                result.SideEffects        = GetStr(r, "sideEffects",       "");
                result.Contraindications  = GetStr(r, "contraindications", "");
                result.PregnancySafety    = GetStr(r, "pregnancySafety",   "");
                result.KeyInteractions    = GetStr(r, "keyInteractions",   "");
                result.SavedMedConflicts  = GetStr(r, "savedMedConflicts", "");
                result.ConditionWarnings  = GetStr(r, "conditionWarnings", "");
                result.DosageNotes        = GetStr(r, "dosageNotes",       "");

                // If all main content fields are empty → API call silently failed
                bool isEmpty = string.IsNullOrEmpty(result.Purpose) &&
                               string.IsNullOrEmpty(result.Mechanism) &&
                               string.IsNullOrEmpty(result.SideEffects);
                result.Success = !isEmpty;
                if (isEmpty)
                    result.ErrorMessage = "لم تصل استجابة مكتملة من الذكاء الاصطناعي. تحقق من مفتاح API أو حاول لاحقاً.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

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
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var jsonText = await _claudeService.GetPregnancyDangerousDrugsAsync();

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
            catch { /* show empty */ }

            return View(drugs);
        }

        // ─── My Medications (saved list) ───────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> MyMedications()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var meds = await _context.UserMedications
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            return View(meds);
        }

        [HttpPost]
        public async Task<IActionResult> AddMedication(string drugName, string? notes)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            if (string.IsNullOrWhiteSpace(drugName))
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
            var userId = HttpContext.Session.GetInt32("UserId");
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
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new List<object>());

            var meds = await _context.UserMedications
                .Where(m => m.UserId == userId)
                .Select(m => new { m.Id, m.DrugName, m.Notes })
                .ToListAsync();

            return Json(meds);
        }

        // ─── AI Follow-up Chat (AJAX) ──────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AiChat(string question, string context)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, answer = "غير مصرح" });

            if (string.IsNullOrWhiteSpace(question))
                return Json(new { success = false, answer = "يرجى كتابة سؤالك" });

            var answer = await _claudeService.AskFollowUpAsync(question, context ?? "");
            return Json(new { success = true, answer });
        }

        // ─── Share Result ──────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewShared(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return RedirectToAction("Index");

            var search = await _context.Searches
                .FirstOrDefaultAsync(s => s.ShareToken == token);

            if (search == null)
            {
                ViewBag.NotFound = true;
                return View("SharedResult", null);
            }

            return View("SharedResult", search);
        }

        // ─── Search History ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var searches = await _context.Searches
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.SearchDate)
                .ToListAsync();

            return View(searches);
        }

        // ─── Account / Dashboard ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Account()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Auth");

            ViewBag.SearchCount  = await _context.Searches.CountAsync(s => s.UserId == userId);
            ViewBag.DangerCount  = await _context.Searches
                .CountAsync(s => s.UserId == userId && (s.Severity == "serious" || s.Severity == "critical"));
            ViewBag.MedCount     = await _context.UserMedications.CountAsync(m => m.UserId == userId);

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string gender, string medicalConditions,
            bool? isMarried, bool? isPregnant)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Auth");

            user.Gender            = gender;
            user.MedicalConditions = medicalConditions;
            user.IsMarried         = (gender == "female") ? isMarried : null;
            user.IsPregnant        = (gender == "female" && isMarried == true) ? isPregnant : null;

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تحديث الملف الشخصي بنجاح";
            return RedirectToAction("Account");
        }

        // ─── Doctor ────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult DoctorInfo()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DoctorInfo(Doctor doctor)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            if (await _context.Doctors.AnyAsync(d => d.UserId == userId))
                return RedirectToAction("Index");

            if (!ModelState.IsValid)
                return View(doctor);

            doctor.UserId     = userId.Value;
            doctor.IsVerified = false;

            _context.Doctors.Add(doctor);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ─── Gemini API Diagnostic ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> TestApi()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Auth");

            try
            {
                var answer = await _claudeService.AskFollowUpAsync(
                    "قل فقط: مرحبا، أنا أعمل بشكل صحيح.",
                    "اختبار الاتصال");
                return Content($"✅ Gemini API يعمل.\nالرد: {answer}", "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Content($"❌ خطأ في Gemini API:\n{ex.Message}", "text/plain; charset=utf-8");
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────
        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        private static string GenerateToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .Replace('+', '-').Replace('/', '_').Replace('=', ' ').Trim();

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
            return el.TryGetProperty(prop, out var v) ? v.GetString() ?? fallback : fallback;
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
