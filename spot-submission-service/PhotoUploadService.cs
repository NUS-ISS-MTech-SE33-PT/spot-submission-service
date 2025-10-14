using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Linq;

public class PhotoUploadService
{
    private readonly IAmazonS3 _s3;
    private readonly SpotSubmissionStorageOptions _options;

    public PhotoUploadService(IAmazonS3 s3, IOptions<SpotSubmissionStorageOptions> options)
    {
        _s3 = s3;
        _options = options.Value;
    }

    public PhotoUploadDescriptor CreateUploadUrl(string fileName, string contentType, string? userSubject)
    {
        if (string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("SpotSubmissionStorage:BucketName is not configured.");
        }

        var key = BuildObjectKey(fileName, userSubject);
        var expiryMinutes = _options.UrlExpiryMinutes <= 0 ? 15 : _options.UrlExpiryMinutes;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType
        };

        var uploadUrl = _s3.GetPreSignedURL(request);
        var baseUrl = ResolvePublicBaseUrl();
        var fileUrl = $"{baseUrl}/{key}";

        return new PhotoUploadDescriptor
        {
            UploadUrl = uploadUrl,
            FileUrl = fileUrl,
            StorageKey = key,
            ExpiresAt = expiresAt
        };
    }

    private string BuildObjectKey(string fileName, string? userSubject)
    {
        var prefix = _options.KeyPrefix?.Trim('/') ?? string.Empty;
        var subjectSegment = NormalizeSubject(userSubject);
        if (!string.IsNullOrEmpty(subjectSegment))
        {
            prefix = string.IsNullOrEmpty(prefix) ? subjectSegment : $"{prefix}/{subjectSegment}";
        }

        var guid = Guid.NewGuid().ToString("N");
        var extension = ExtractExtension(fileName);
        var key = extension.Length > 0 ? $"{guid}{extension}" : guid;
        if (string.IsNullOrEmpty(prefix))
        {
            return key;
        }

        return $"{prefix}/{key}";
    }

    private static string? NormalizeSubject(string? userSubject)
    {
        if (string.IsNullOrWhiteSpace(userSubject))
        {
            return null;
        }

        var filtered = new string(userSubject.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrEmpty(filtered) ? null : filtered;
    }

    private static string ExtractExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        if (extension.Length > 10)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[extension.Length];
        var hasInvalid = false;
        for (var i = 0; i < extension.Length; i++)
        {
            var c = extension[i];
            if (i == 0 && c != '.')
            {
                hasInvalid = true;
                break;
            }

            if (i > 0 && !char.IsLetterOrDigit(c))
            {
                hasInvalid = true;
                break;
            }

            buffer[i] = char.ToLowerInvariant(c);
        }

        return hasInvalid ? string.Empty : new string(buffer);
    }

    private string ResolvePublicBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/');
        }

        var region = _s3.Config.RegionEndpoint?.SystemName;
        if (string.IsNullOrWhiteSpace(region))
        {
            return $"https://{_options.BucketName}.s3.amazonaws.com";
        }

        return $"https://{_options.BucketName}.s3.{region}.amazonaws.com";
    }
}
