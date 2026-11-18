namespace Digital_Pharma.Services;

public static class InteractionSeverity
{
    private static readonly string[] Ordered = ["safe","caution","moderate","serious","critical"];
    public static string Normalize(string? value)
    {
        var normalized=(value??"").Trim().ToLowerInvariant();
        return Ordered.Contains(normalized)?normalized:"unknown";
    }
    public static int Rank(string? value)=>Array.IndexOf(Ordered,Normalize(value));
    public static string Overall(IEnumerable<string?> values)
    {
        var normalized=values.Select(Normalize).ToList();
        var known=normalized.Where(v=>v!="unknown").OrderByDescending(Rank).FirstOrDefault();
        return normalized.Contains("unknown") && (known==null||Rank(known)<=Rank("caution"))?"unknown":known??"unknown";
    }
    public static string ArabicLabel(string? value)=>Normalize(value) switch
    {
        "safe"=>"لم تظهر إشارة خطورة ضمن الأدلة المكتملة",
        "caution"=>"يستدعي الانتباه",
        "moderate"=>"متوسط ويحتاج متابعة",
        "serious"=>"خطير ويحتاج مراجعة عاجلة",
        "critical"=>"خطر شديد",
        _=>"تعذر اكتمال التقييم"
    };
}
