using System.ComponentModel.DataAnnotations;

namespace Digital_Pharma.Models;

public sealed class AssistantChatRequest
{
    [Required, StringLength(600, MinimumLength = 2)]
    public string Question { get; set; } = string.Empty;

    [Required, RegularExpression("^(global|drug)$")]
    public string Scope { get; set; } = "global";

    [StringLength(120)]
    public string? DrugName { get; set; }

    [StringLength(80)]
    public string? PageKey { get; set; }

    [StringLength(2400)]
    public string? Conversation { get; set; }

    public bool IncludeHealth { get; set; }
}
