using System.Net;
using System.Security.Cryptography;
using VRCX.Vrchat.Models;

namespace VRCX.Vrchat;

/// <summary>
/// The original's uploadImageLegacy (coordinators/imageUploadCoordinator.js with api/image.ts and
/// the desktop's MD5File/SignFile): a new version of the image file the avatar or world already
/// has, its file and rsync signature each started, PUT to the address VRChat hands out and
/// finished, and then the avatar or world pointed at it.
/// </summary>
public static class VrchatContentImage
{
    public const string EndpointDomain = "https://api.vrchat.cloud/api/1";

    /// <summary>Uploads the PNG (already fitted to VRChat's limits) and sets it as the image of the "avatar" or "world". Answers with the new image URL.</summary>
    public static async Task<string> ChangeAsync(VrchatApiClient client, string kind, string entityId, string? currentImageUrl, byte[] png, CancellationToken ct)
    {
        var fileId = VrchatApiClient.ExtractFileId(currentImageUrl)
            ?? throw new VrchatApiException(HttpStatusCode.BadRequest, "POST", "file", $"the {kind} has no image file to add a version to");
        var signature = RsyncSignature.Compute(png);
        var fileMd5 = Convert.ToBase64String(MD5.HashData(png));
        var signatureMd5 = Convert.ToBase64String(MD5.HashData(signature));

        // imageInit
        VrchatFile created;
        try
        {
            created = await client.CreateFileVersionAsync(fileId, fileMd5, png.Length, signatureMd5, signature.Length, ct);
        }
        catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
        {
            await client.AbandonFileUploadAsync(fileId, ct);
            throw;
        }
        var uploadedFileId = created.Id;
        var version = created.Versions.Count > 0 ? created.Versions[^1].Version
            : throw new VrchatApiException(HttpStatusCode.BadGateway, "POST", "file", "VRChat made no file version");

        // imageFileStart, uploadImageFileAWS, imageFileFinish
        var fileStart = await StartAsync(client, uploadedFileId, version, "file", ct);
        await client.UploadFilePartAsync(fileStart.Url, png, "image/png", fileMd5, ct);
        await client.FinishFileUploadAsync(uploadedFileId, version, "file", ct);

        // imageSigStart, uploadImageSigAWS, imageSigFinish
        var signatureStart = await StartAsync(client, uploadedFileId, version, "signature", ct);
        await client.UploadFilePartAsync(signatureStart.Url, signature, "application/x-rsync-signature", signatureMd5, ct);
        await client.FinishFileUploadAsync(uploadedFileId, version, "signature", ct);

        // imageSet
        var imageUrl = $"{EndpointDomain}/file/{uploadedFileId}/{version}/file";
        var change = new Dictionary<string, object> { ["id"] = entityId, ["imageUrl"] = imageUrl };
        var setUrl = kind == "avatar"
            ? (await client.SaveAvatarAsync(entityId, change, ct)).ImageUrl
            : (await client.SaveWorldAsync(entityId, change, ct)).ImageUrl;
        if (setUrl != imageUrl)
            throw new VrchatApiException(HttpStatusCode.BadGateway, "PUT", kind == "avatar" ? "avatars" : "worlds", $"{kind} image change failed");
        return imageUrl;
    }

    private static async Task<FileUploadUrl> StartAsync(VrchatApiClient client, string fileId, int version, string part, CancellationToken ct)
    {
        try
        {
            return await client.StartFileUploadAsync(fileId, version, part, ct);
        }
        catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
        {
            await client.AbandonFileUploadAsync(fileId, ct);
            throw;
        }
    }
}
