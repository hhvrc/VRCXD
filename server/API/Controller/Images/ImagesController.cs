using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Services.Images;

namespace VRCX.API.Controller.Images;

/// <summary>
/// VRChat's images, kept on this server's disk.
/// </summary>
[ApiController]
[Tags("Images")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/images")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class ImagesController(ImageCache images) : VrcxControllerBase
{
    /// <summary>
    /// An image by its VRChat URL, from disk when it has been seen before. An img tag cannot set headers, so this also takes ?token=.
    /// </summary>
    /// <param name="url">The image's URL on one of VRChat's hosts</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The image</response>
    /// <response code="302">It could not be stored, so here is VRChat's own copy</response>
    /// <response code="400">Not a VRChat image URL</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetImage([FromQuery] string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !ImageCache.IsAllowed(uri)) return BadRequest();

        var image = await images.GetAsync(url, cancellationToken);
        if (image is null) return Redirect(url);

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ETag = $"\"{image.Sha256}\"";
        return PhysicalFile(image.Path, image.ContentType, enableRangeProcessing: false);
    }
}
