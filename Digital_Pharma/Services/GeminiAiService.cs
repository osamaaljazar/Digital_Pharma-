using Digital_Pharma.Models;
using System.Text;
using System.Text.Json;

namespace Digital_Pharma.Services
{
    public class GeminiAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        private const string BaseUrl =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent";

        public GeminiAiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        }

        // ── Drug Interaction Analysis ──────────────────────────────────────────
        public async Task<ClaudeAnalysisResult> AnalyzeDrugInteractionAsync(
            string drug1, string drug2,
            string? gender, int? age,
            string? medicalConditions, bool? isPregnant,
            string? fdaDrug1Interactions, string? fdaDrug2Interactions,
            List<string>? savedMedications = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) return NoApiKey();

            var patientInfo      = BuildPatientContext(gender, age, medicalConditions, isPregnant);
            var fdaContext       = BuildFdaContext(drug1, drug2, fdaDrug1Interactions, fdaDrug2Interactions);
            var savedMedsContext = BuildSavedMedsContext(drug1, drug2, savedMedications);

            var prompt =
                "انت صيدلاني سريري خبير متخصص في التداخلات الدوائية. مهمتك سلامة المريض.\n\n" +
                "قواعد التصنيف الثابتة:\n" +
                "Warfarin + Aspirin = critical\n" +
                "Warfarin + Ibuprofen = critical\n" +
                "Warfarin + Naproxen = critical\n" +
                "Warfarin + Clopidogrel = serious\n" +
                "MAOIs + SSRIs = critical\n" +
                "MAOIs + SNRIs = critical\n" +
                "Methotrexate + NSAIDs = serious\n" +
                "ACE inhibitors + Potassium = serious\n" +
                "Digoxin + Amiodarone = serious\n" +
                "Statins + Macrolides = moderate\n\n" +
                "معلومات المريض: " + patientInfo + "\n" +
                "الدواء الاول: " + drug1 + "\n" +
                "الدواء الثاني: " + drug2 + "\n\n" +
                fdaContext +
                savedMedsContext +
                "اعطني JSON بالحقول التالية فقط بالعربية:\n" +
                "severity (safe او caution او moderate او serious او critical)\n" +
                "interactionFound (true او false)\n" +
                "summary (ملخص موجز)\n" +
                "details (شرح تفصيلي)\n" +
                "recommendation (التوصية)\n" +
                "patientSpecificNotes (ملاحظات للمريض)\n" +
                "safeAlternatives (بديل آمن او فارغ)\n" +
                "savedMedConflicts (تداخلات مع ادوية المريض او فارغ)";

            try
            {
                return await CallGeminiForJsonAsync<ClaudeAnalysisResult>(prompt, 1400, ParseInteractionResult);
            }
            catch (Exception ex)
            {
                return new ClaudeAnalysisResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // ── Drug Information ───────────────────────────────────────────────────
        public async Task<string> GetDrugInfoAsync(
            string drugName, string? medicalConditions, List<string>? savedMedications)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "{\"__error\":\"لم يتم تكوين مفتاح Gemini API\"}";

            var savedMedsList = savedMedications?.Count > 0
                ? string.Join("، ", savedMedications)
                : "لا توجد ادوية محفوظة";

            var prompt =
                "انت صيدلاني سريري خبير. قدم معلومات شاملة عن الدواء التالي بالعربية.\n\n" +
                "الدواء: " + drugName + "\n" +
                "الحالات الصحية: " + (string.IsNullOrEmpty(medicalConditions) ? "غير محدد" : medicalConditions) + "\n" +
                "ادوية المريض الحالية: " + savedMedsList + "\n\n" +
                "اعطني JSON بالحقول التالية بالعربية:\n" +
                "name (اسم الدواء)\n" +
                "category (التصنيف الدوائي)\n" +
                "purpose (الاستخدامات)\n" +
                "mechanism (آلية العمل)\n" +
                "sideEffects (الآثار الجانبية)\n" +
                "contraindications (موانع الاستخدام)\n" +
                "pregnancySafety (الامان اثناء الحمل)\n" +
                "keyInteractions (التداخلات المعروفة)\n" +
                "savedMedConflicts (التداخل مع ادوية المريض)\n" +
                "conditionWarnings (تحذيرات الحالات الصحية)\n" +
                "dosageNotes (ملاحظات الجرعات)";

            try
            {
                var raw = await SendGeminiRequestAsync(prompt, 1500, forceJson: true);
                return CleanAndExtractJson(raw ?? "{}", '{', '}');
            }
            catch (Exception ex)
            {
                var safeMsg = ex.Message.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
                return $"{{\"__error\":\"{safeMsg}\",\"name\":\"{drugName}\"}}";
            }
        }

        // ── Pregnancy Dangerous Drugs ──────────────────────────────────────────
        public async Task<string> GetPregnancyDangerousDrugsAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return "[]";

            var prompt =
                "انت صيدلاني سريري خبير. قدم قائمة بأخطر الادوية الممنوعة خلال الحمل.\n\n" +
                "اعطني JSON مصفوفة تحتوي كائنات بالحقول التالية بالعربية:\n" +
                "name (اسم الدواء)\n" +
                "category (FDA Category X او D او C)\n" +
                "risk (وصف الخطر على الجنين)\n" +
                "alternative (بديل اكثر امانا)\n" +
                "severity (critical او serious او moderate)\n\n" +
                "اذكر 15 دواء على الاقل.";

            try
            {
                var raw = await SendGeminiRequestAsync(prompt, 2000, forceJson: true);
                return CleanAndExtractJson(raw ?? "[]", '[', ']');
            }
            catch
            {
                return "[]";
            }
        }

        // ── Follow-up Chat ─────────────────────────────────────────────────────
        public async Task<string> AskFollowUpAsync(string question, string context)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "لم يتم تكوين مفتاح Gemini API.";

            var prompt =
                "انت صيدلاني سريري خبير. اجب على السؤال التالي بالعربية في 3 الى 5 جمل مختصرة وواضحة.\n\n" +
                "السياق:\n" + context + "\n\n" +
                "السؤال:\n" + question;

            try
            {
                var response = await SendGeminiRequestAsync(prompt, 600, forceJson: false);
                return response ?? "لم تصل استجابة من الخادم.";
            }
            catch (Exception ex)
            {
                return $"حدث خطأ: {ex.Message}";
            }
        }

        // ── Generic JSON caller with custom parser ─────────────────────────────
        private async Task<T> CallGeminiForJsonAsync<T>(string prompt, int maxTokens,
            Func<string, T> parser) where T : new()
        {
            try
            {
                var raw = await SendGeminiRequestAsync(prompt, maxTokens, forceJson: true);
                var json = CleanAndExtractJson(raw ?? "{}", '{', '}');
                return parser(json);
            }
            catch (Exception ex)
            {
                // Return a default T with error — caller handles it
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        private ClaudeAnalysisResult ParseInteractionResult(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                return new ClaudeAnalysisResult
                {
                    Success              = true,
                    Severity             = GetStrEl(r, "severity")             ?? "unknown",
                    InteractionFound     = r.TryGetProperty("interactionFound", out var f) && f.GetBoolean(),
                    Summary              = GetStrEl(r, "summary")              ?? "",
                    Details              = GetStrEl(r, "details")              ?? "",
                    Recommendation       = GetStrEl(r, "recommendation")       ?? "",
                    PatientSpecificNotes = GetStrEl(r, "patientSpecificNotes") ?? "",
                    SafeAlternatives     = GetStrEl(r, "safeAlternatives")     ?? "",
                    SavedMedConflicts    = GetStrEl(r, "savedMedConflicts")    ?? "",
                };
            }
            catch (Exception ex)
            {
                return new ClaudeAnalysisResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // ── Core HTTP + error handling ─────────────────────────────────────────
        private async Task<string?> SendGeminiRequestAsync(string prompt, int maxTokens, bool forceJson)
        {
            var url = $"{BaseUrl}?key={_apiKey}";

            // Single body format — JSON sanitization is done client-side
            var temperature = forceJson ? 0.2 : 0.5;
            string bodyJson = JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    maxOutputTokens = maxTokens,
                    temperature     = temperature
                }
            });

            var content      = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            var response     = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errMsg;
                try
                {
                    using var errDoc = JsonDocument.Parse(responseText);
                    errMsg = errDoc.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString() ?? responseText;
                }
                catch
                {
                    errMsg = responseText[..Math.Min(300, responseText.Length)];
                }
                throw new InvalidOperationException($"Gemini API Error ({(int)response.StatusCode}): {errMsg}");
            }

            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }

        // ── JSON cleaning: strip fences + sanitize control chars ───────────────
        /// <summary>
        /// Two-pass clean:
        ///  1. Strip markdown code fences (```json ... ```)
        ///  2. Extract the outermost { } or [ ] block
        ///  3. Char-by-char: escape bare control chars inside JSON string values
        /// </summary>
        private static string CleanAndExtractJson(string text, char open, char close)
        {
            if (string.IsNullOrWhiteSpace(text)) return open == '{' ? "{}" : "[]";

            text = text.Trim();

            // Strip markdown fences
            if (text.StartsWith("```"))
            {
                var nl = text.IndexOf('\n');
                if (nl >= 0) text = text[(nl + 1)..];
                var endFence = text.TrimEnd().LastIndexOf("```");
                if (endFence >= 0) text = text[..endFence].TrimEnd();
                text = text.Trim();
            }

            // Extract outermost block
            var s = text.IndexOf(open);
            var e = text.LastIndexOf(close);
            if (s >= 0 && e > s)
                text = text[s..(e + 1)];

            // Sanitize: escape bare control characters inside JSON string values
            return EscapeControlCharsInStrings(text);
        }

        private static string EscapeControlCharsInStrings(string text)
        {
            var sb       = new StringBuilder(text.Length + 32);
            bool inStr   = false;
            bool escaped = false;

            foreach (char c in text)
            {
                if (escaped)                   // char after backslash — pass through raw
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inStr)        // start of an escape sequence
                {
                    escaped = true;
                    sb.Append(c);
                    continue;
                }

                if (c == '"')                  // toggle string mode
                {
                    inStr = !inStr;
                    sb.Append(c);
                    continue;
                }

                if (inStr && c < 0x20)         // bare control char inside a string
                {
                    switch (c)
                    {
                        case '\n': sb.Append("\\n");  break;
                        case '\r': sb.Append("\\r");  break;
                        case '\t': sb.Append("\\t");  break;
                        case '\b': sb.Append("\\b");  break;
                        case '\f': sb.Append("\\f");  break;
                        default:   sb.Append($"\\u{(int)c:x4}"); break;
                    }
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static ClaudeAnalysisResult NoApiKey() => new()
        {
            Success      = false,
            ErrorMessage = "لم يتم تكوين مفتاح Gemini API"
        };

        private static string? GetStrEl(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) ? v.GetString() : null;

        public static string BuildPatientContext(
            string? gender, int? age, string? medicalConditions, bool? isPregnant)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(gender))
                parts.Add(gender == "female" ? "انثى" : "ذكر");
            if (age.HasValue)
                parts.Add($"العمر {age} سنة");
            if (!string.IsNullOrEmpty(medicalConditions))
                parts.Add($"الامراض: {medicalConditions}");
            if (isPregnant == true)
                parts.Add("حامل - راع سلامة الجنين");
            return parts.Count > 0 ? string.Join("، ", parts) : "لا توجد معلومات";
        }

        private static string BuildFdaContext(string d1, string d2, string? i1, string? i2)
        {
            if (string.IsNullOrEmpty(i1) && string.IsNullOrEmpty(i2)) return "";
            var sb = new StringBuilder("بيانات FDA:\n");
            if (!string.IsNullOrEmpty(i1)) sb.AppendLine($"{d1}: {i1[..Math.Min(300, i1.Length)]}");
            if (!string.IsNullOrEmpty(i2)) sb.AppendLine($"{d2}: {i2[..Math.Min(300, i2.Length)]}");
            return sb + "\n";
        }

        private static string BuildSavedMedsContext(string d1, string d2, List<string>? meds)
        {
            if (meds == null || meds.Count == 0) return "";
            var list = string.Join("، ", meds.Take(20));
            return $"ادوية المريض المحفوظة: {list}\n" +
                   $"تحقق من التداخل بين ({d1} او {d2}) وهذه الادوية.\n\n";
        }
    }
}
