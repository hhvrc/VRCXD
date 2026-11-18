using System.ComponentModel.DataAnnotations;

namespace VRCX.API.Controller;

public sealed class ContentImageForm
{
    /// <summary>The picture, already cropped and fitted to VRChat's limits by the client (the original's ResizeImageToFitLimits).</summary>
    [Required] public required IFormFile Image { get; set; }
    /// <summary>The image URL the avatar or world has now: its file gets the new version.</summary>
    [Required] public required string ImageUrl { get; set; }
}
