namespace Digital_Pharma.Models;
public static class DoctorStatusText
{
    public static string Label(DoctorApplicationStatus status) => status switch {
        DoctorApplicationStatus.Pending=>"قيد المراجعة",
        DoctorApplicationStatus.NeedsInformation=>"مطلوب استكمال المعلومات",
        DoctorApplicationStatus.Approved=>"معتمد",
        DoctorApplicationStatus.Rejected=>"مرفوض",
        DoctorApplicationStatus.Suspended=>"الاعتماد معلّق",
        _=>"غير معروف"
    };
}

