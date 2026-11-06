using Digital_Pharma.Models;
using System.Text;
using System.Text.Json;

namespace Digital_Pharma.Services
{
    public class ClaudeAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string Model = "claude-sonnet-4-5";

        public ClaudeAiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Claude:ApiKey"] ?? string.Empty;
        }

        // ── Main drug interaction analysis ──────────────────────────────────────
        public async Task<ClaudeAnalysisResult> AnalyzeDrugInteractionAsync(
            string drug1, string drug2,
            string? gender, int? age,
            string? medicalConditions, bool? isPregnant,
            string? fdaDrug1Interactions, string? fdaDrug2Interactions,
            List<string>? savedMedications = null)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return NoApiKey();

            var patientInfo = BuildPatientContext(gender, age, medicalConditions, isPregnant);
            var fdaContext  = BuildFdaContext(drug1, drug2, fdaDrug1Interactions, fdaDrug2Interactions);
            var savedMedsContext = BuildSavedMedsContext(drug1, drug2, savedMedications);

            var jsonTemplate =
                "{\n" +
                "  \"severity\": \"safe|caution|moderate|serious|critical\",\n" +
                "  \"interactionFound\": true,\n" +
                "  \"summary\": \"ملخص موجز\",\n" +
                "  \"details\": \"شرح تفصيلي للتفاعل وآلية حدوثه\",\n" +
                "  \"recommendation\": \"التوصية العملية للمريض\",\n" +
                "  \"patientSpecificNotes\": \"ملاحظات خاصة بحالة هذا المريض\",\n" +
                "  \"safeAlternatives\": \"اقتراح بديل آمن إذا وُجد تداخل، أو فارغ إن كان آمناً\",\n" +
                "  \"savedMedConflicts\": \"أي تداخلات مع الأدوية المحفوظة للمريض، أو فارغ إن لم توجد\"\n" +
                "}";

            var prompt =
                "أنت صيدلاني سريري خبير عالمي متخصص في التداخلات الدوائية. مهمتك الأولى هي سلامة المريض.\n\n" +

                "⚠️ قاعدة ذهبية لا تُخالَف: استخدم معرفتك الطبية الكاملة بشكل مستقل، ولا تعتمد فقط على بيانات FDA.\n" +
                "التداخلات المعروفة علمياً يجب أن تُصنَّف بدقة دائماً، مثل:\n" +
                "• Warfarin + Aspirin → critical (خطر نزيف شديد قد يهدد الحياة)\n" +
                "• Warfarin + NSAIDs (Ibuprofen, Naproxen) → critical\n" +
                "• Warfarin + Clopidogrel → serious إلى critical\n" +
                "• MAOIs + SSRIs/SNRIs → critical (متلازمة السيروتونين)\n" +
                "• Methotrexate + NSAIDs → serious\n" +
                "• ACE inhibitors + Potassium supplements → serious\n" +
                "• Digoxin + Amiodarone → serious\n" +
                "• Statins + Macrolide antibiotics → moderate إلى serious\n\n" +

                "## معلومات المريض:\n" + patientInfo + "\n\n" +
                "## الأدوية المراد فحصها:\n" +
                $"- الدواء الأول: {drug1}\n" +
                $"- الدواء الثاني: {drug2}\n\n" +
                fdaContext +
                savedMedsContext +
                "## المطلوب (JSON فقط بالعربية، بدون أي نص خارجه):\n" +
                jsonTemplate + "\n\n" +
                "درجات الخطورة: safe=آمن | caution=انتباه بسيط | moderate=متوسط يحتاج مراقبة | serious=خطير يحتاج طبيباً | critical=خطر شديد يهدد الحياة\n" +
                "في safeAlternatives: اذكر دواءً بديلاً آمناً مع سببه إذا كان هناك تداخل، أو اتركه فارغاً.\n" +
                "في savedMedConflicts: اذكر أي تداخل بين الدوائين المفحوصَين وأدوية المريض المحفوظة، أو اتركه فارغاً.";

            return await CallClaudeAsync(prompt);
        }

        // ── Drug Information (single drug) ─────────────────────────────────────
        public async Task<string> GetDrugInfoAsync(string drugName, string? medicalConditions, List<string>? savedMedications)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "لم يتم تكوين مفتاح Claude API.";

            var savedMedsList = savedMedications != null && savedMedications.Count > 0
                ? string.Join("، ", savedMedications)
                : "لا توجد أدوية محفوظة";

            var jsonTemplate =
                "{\n" +
                "  \"name\": \"الاسم الكامل للدواء\",\n" +
                "  \"category\": \"التصنيف الدوائي\",\n" +
                "  \"purpose\": \"الاستخدامات الطبية الرئيسية\",\n" +
                "  \"mechanism\": \"آلية عمل الدواء\",\n" +
                "  \"sideEffects\": \"الآثار الجانبية الشائعة والخطيرة\",\n" +
                "  \"contraindications\": \"موانع الاستخدام والحالات الخطرة\",\n" +
                "  \"pregnancySafety\": \"مستوى الأمان أثناء الحمل\",\n" +
                "  \"keyInteractions\": \"أهم التداخلات الدوائية المعروفة\",\n" +
                "  \"savedMedConflicts\": \"تحليل التداخل مع أدوية المريض المحفوظة تحديداً\",\n" +
                "  \"conditionWarnings\": \"تحذيرات خاصة بالحالات الصحية المذكورة\",\n" +
                "  \"dosageNotes\": \"ملاحظات عامة عن الجرعات\"\n" +
                "}";

            var prompt =
                "أنت صيدلاني سريري خبير. قدّم معلومات شاملة عن الدواء التالي بالعربية.\n\n" +
                $"## الدواء: {drugName}\n\n" +
                $"## الحالات الصحية للمريض: {(string.IsNullOrEmpty(medicalConditions) ? "غير محدد" : medicalConditions)}\n\n" +
                $"## أدوية المريض الحالية (للكشف عن التداخلات): {savedMedsList}\n\n" +
                "## المطلوب (JSON فقط بالعربية، بدون أي نص خارجه):\n" +
                jsonTemplate;

            try
            {
                var body = new { model = Model, max_tokens = 1500, messages = new[] { new { role = "user", content = prompt } } };
                var response = await PostToClaudeAsync(body);
                return response ?? "{}";
            }
            catch (Exception ex)
            {
                // Return a JSON with error so the controller can surface it
                return $"{{\"__error\":\"{ex.Message.Replace("\"","'")}\",\"name\":\"{drugName}\"}}";
            }
        }

        // ── Pregnancy dangerous drugs ──────────────────────────────────────────
        public async Task<string> GetPregnancyDangerousDrugsAsync()
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "[]";

            var prompt =
                "أنت صيدلاني سريري خبير. قدّم قائمة بأخطر الأدوية الممنوعة أو الخطرة خلال الحمل.\n\n" +
                "أعطِ JSON فقط بالشكل التالي (لا نص خارج JSON):\n" +
                "[\n" +
                "  {\n" +
                "    \"name\": \"اسم الدواء\",\n" +
                "    \"category\": \"FDA Pregnancy Category (X, D, C)\",\n" +
                "    \"risk\": \"وصف الخطر على الجنين\",\n" +
                "    \"alternative\": \"بديل أكثر أماناً إن وُجد\",\n" +
                "    \"severity\": \"critical|serious|moderate\"\n" +
                "  }\n" +
                "]\n\n" +
                "اذكر على الأقل 15 دواء مهم. ركّز على الأشهر الأولى والخطر الأشد. لا تضع أي نص خارج JSON.";

            try
            {
                var body = new { model = Model, max_tokens = 2000, messages = new[] { new { role = "user", content = prompt } } };
                var response = await PostToClaudeAsync(body);
                return ExtractJson(response ?? "[]", '[', ']');
            }
            catch
            {
                return "[]";
            }
        }

        // ── Follow-up AI chat question ─────────────────────────────────────────
        public async Task<string> AskFollowUpAsync(string question, string context)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "⚠️ لم يتم تكوين مفتاح Claude API.";

            var prompt =
                "أنت صيدلاني سريري خبير. أجب على السؤال التالي بالعربية بشكل مختصر وواضح (3-5 جمل).\n\n" +
                "## السياق:\n" + context + "\n\n" +
                "## السؤال:\n" + question;

            try
            {
                var body = new { model = Model, max_tokens = 600, messages = new[] { new { role = "user", content = prompt } } };
                var response = await PostToClaudeAsync(body);
                return response ?? "لم تصل استجابة من الخادم.";
            }
            catch (Exception ex)
            {
                return $"⚠️ {ex.Message}";
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private async Task<ClaudeAnalysisResult> CallClaudeAsync(string prompt)
        {
            try
            {
                var body = new { model = Model, max_tokens = 1400, messages = new[] { new { role = "user", content = prompt } } };
                var text = await PostToClaudeAsync(body);
                if (text == null) return new ClaudeAnalysisResult { Success = false, ErrorMessage = "لا توجد استجابة من الخادم" };

                text = ExtractJson(text, '{', '}');
                using var doc = JsonDocument.Parse(text);
                var r = doc.RootElement;

                return new ClaudeAnalysisResult
                {
                    Success              = true,
                    Severity             = r.TryGetProperty("severity",             out var s)  ? s.GetString()  ?? "unknown" : "unknown",
                    InteractionFound     = r.TryGetProperty("interactionFound",     out var f)  && f.GetBoolean(),
                    Summary              = r.TryGetProperty("summary",              out var su) ? su.GetString() ?? "" : "",
                    Details              = r.TryGetProperty("details",              out var d)  ? d.GetString()  ?? "" : "",
                    Recommendation       = r.TryGetProperty("recommendation",       out var rc) ? rc.GetString() ?? "" : "",
                    PatientSpecificNotes = r.TryGetProperty("patientSpecificNotes", out var pn) ? pn.GetString() ?? "" : "",
                    SafeAlternatives     = r.TryGetProperty("safeAlternatives",     out var sa) ? sa.GetString() ?? "" : "",
                    SavedMedConflicts    = r.TryGetProperty("savedMedConflicts",    out var sm) ? sm.GetString() ?? "" : "",
                };
            }
            catch (Exception ex)
            {
                return new ClaudeAnalysisResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<string?> PostToClaudeAsync(object body)
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Content = content;
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request);

            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Extract error message from Claude API response
                try
                {
                    using var errDoc = JsonDocument.Parse(responseText);
                    var errMsg = errDoc.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString();
                    throw new InvalidOperationException($"Claude API Error ({(int)response.StatusCode}): {errMsg}");
                }
                catch (InvalidOperationException) { throw; }
                catch
                {
                    throw new InvalidOperationException($"Claude API Error ({(int)response.StatusCode}): {responseText[..Math.Min(200, responseText.Length)]}");
                }
            }

            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        }

        private static ClaudeAnalysisResult NoApiKey() => new()
        {
            Success = false,
            ErrorMessage = "لم يتم تكوين مفتاح Claude API — أضف المفتاح في appsettings.json"
        };

        public static string BuildPatientContext(string? gender, int? age, string? medicalConditions, bool? isPregnant)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(gender))
                parts.Add($"الجنس: {(gender == "female" ? "أنثى" : "ذكر")}");
            if (age.HasValue)
                parts.Add($"العمر: {age} سنة");
            if (!string.IsNullOrEmpty(medicalConditions))
                parts.Add($"الأمراض المزمنة: {medicalConditions}");
            if (isPregnant == true)
                parts.Add("⚠️ المريضة حامل — راعِ سلامة الجنين في تحليلك");
            return parts.Count > 0
                ? string.Join("\n", parts.Select(p => $"- {p}"))
                : "- لا توجد معلومات محددة";
        }

        private static string BuildFdaContext(string d1, string d2, string? i1, string? i2)
        {
            if (string.IsNullOrEmpty(i1) && string.IsNullOrEmpty(i2)) return "";
            var sb = new StringBuilder("## بيانات FDA (مرجعية — استخدم معرفتك الطبية أيضاً):\n");
            if (!string.IsNullOrEmpty(i1)) sb.AppendLine($"- {d1}: {i1[..Math.Min(400, i1.Length)]}...");
            if (!string.IsNullOrEmpty(i2)) sb.AppendLine($"- {d2}: {i2[..Math.Min(400, i2.Length)]}...");
            return sb.ToString() + "\n";
        }

        private static string BuildSavedMedsContext(string d1, string d2, List<string>? savedMeds)
        {
            if (savedMeds == null || savedMeds.Count == 0) return "";
            var medsList = string.Join("، ", savedMeds.Take(20)); // max 20 meds
            return
                "## أدوية المريض المحفوظة (افحص التداخل مع كل واحد منها):\n" +
                $"- {medsList}\n" +
                $"تحقق هل يوجد تداخل بين ({d1} أو {d2}) وأي دواء من القائمة أعلاه.\n\n";
        }

        private static string ExtractJson(string text, char open, char close)
        {
            text = text.Trim();
            var s = text.IndexOf(open);
            var e = text.LastIndexOf(close);
            return (s >= 0 && e > s) ? text[s..(e + 1)] : text;
        }
    }
}
