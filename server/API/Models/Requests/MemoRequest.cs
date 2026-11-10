using System.ComponentModel.DataAnnotations;

namespace VRCX.API.Models.Requests;

public sealed class MemoRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(16384, MinimumLength = 1)]
    public required string Memo { get; set; }
}
