using Digital_Pharma.Models;

namespace Digital_Pharma.Services;

public sealed record InteractionEvidenceResult(string Severity, string Status, string Note)
{
    public bool IsComplete => Status == "complete";
}

public static class InteractionEvidence
{
    public static InteractionEvidenceResult Assess(
        ClaudeAnalysisResult ai, OpenFdaResult? first, OpenFdaResult? second)
    {
        if (!ai.Success)
            return new("unknown", "unavailable",
                "تعذر الحصول على التحليل المساعد؛ لا يمكن اعتبار غياب النتيجة دليلاً على الأمان.");

        var severity = InteractionSeverity.Normalize(ai.Severity);
        if (severity=="unknown")
            return new("unknown", "unavailable",
                "وصل تحليل دون مستوى خطورة صالح؛ يلزم التحقق من صيدلي أو طبيب.");

        var firstLabel = first?.OpenFda != null;
        var secondLabel = second?.OpenFda != null;
        var firstInteractions = first?.DrugInteractions?.Any(text => !string.IsNullOrWhiteSpace(text)) == true;
        var secondInteractions = second?.DrugInteractions?.Any(text => !string.IsNullOrWhiteSpace(text)) == true;

        if (!firstLabel || !secondLabel)
            return new(severity == "safe" ? "unknown" : severity, "partial",
                "لم يُعثر على نشرة FDA مطابقة لأحد الدواءين أو كليهما؛ النتيجة جزئية ولا تؤكد الأمان.");

        if (!firstInteractions || !secondInteractions)
            return new(severity == "safe" ? "unknown" : severity, "partial",
                "النشرة متاحة لكن قسم التداخلات غير مكتمل لأحد الدواءين أو كليهما؛ لا يمكن تأكيد عدم وجود تداخل.");

        return new(severity, "complete",
            "اكتملت مقارنة التحليل المساعد مع أقسام التداخلات المتاحة في النشرات المرجعية. تبقى النتيجة توعوية وليست قراراً علاجياً.");
    }
}
