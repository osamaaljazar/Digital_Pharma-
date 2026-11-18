namespace Digital_Pharma.Models;

public sealed record AccountActivityItem(DateTimeOffset At,string Kind,string Title,string Detail,string Icon,string Color);
public sealed class AccountActivityPage
{
    public List<AccountActivityItem> Items { get; set; } = [];
    public string Filter { get; set; } = "all";
    public int Page { get; set; } = 1;
    public bool HasNext { get; set; }
}
