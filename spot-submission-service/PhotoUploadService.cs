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

        ValidateUploadRequest(fileName, contentType);

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

    public void ValidateUploadRequest(string fileName, string contentType)
    {
        var extension = ExtractExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            throw new ArgumentException("Unsupported file extension.", nameof(fileName));
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (!IsAllowed(_options.AllowedContentTypes, normalizedContentType))
        {
            throw new ArgumentException("Unsupported content type.", nameof(contentType));
        }

        if (!IsAllowed(_options.AllowedExtensions, extension))
        {
            throw new ArgumentException("Unsupported file extension.", nameof(fileName));
        }

        if (!IsMimeExtensionMatch(normalizedContentType, extension))
        {
            throw new ArgumentException("File extension does not match content type.");
        }
    }

    public async Task ValidateUploadedObjectAsync(string storageKey, string photoUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("photoStorageKey is required.", nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            throw new ArgumentException("photoUrl is required.", nameof(photoUrl));
        }

        var expectedUrl = $"{ResolvePublicBaseUrl()}/{storageKey}";
        if (!string.Equals(photoUrl.Trim(), expectedUrl, StringComparison.Ordinal))
        {
            throw new ArgumentException("photoUrl does not match photoStorageKey.");
        }

        var extension = ExtractExtension(storageKey);
        if (string.IsNullOrEmpty(extension) || !IsAllowed(_options.AllowedExtensions, extension))
        {
            throw new ArgumentException("Unsupported file format.");
        }

        var metadata = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey
        }, cancellationToken);

        var maxUploadBytes = _options.MaxUploadBytes > 0 ? _options.MaxUploadBytes : 5 * 1024 * 1024;
        if (metadata.Headers.ContentLength > maxUploadBytes)
        {
            throw new ArgumentException($"File size exceeds limit ({maxUploadBytes} bytes).");
        }

        var contentType = NormalizeContentType(metadata.Headers.ContentType ?? metadata.ContentType);
        if (!IsAllowed(_options.AllowedContentTypes, contentType))
        {
            throw new ArgumentException("Unsupported content type.");
        }

        if (!IsMimeExtensionMatch(contentType, extension))
        {
            throw new ArgumentException("Uploaded file format does not match its content type.");
        }
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

    private static bool IsAllowed(IEnumerable<string>? values, string candidate)
    {
        if (values == null)
        {
            return false;
        }

        return values.Any(value => string.Equals(value?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var normalized = contentType.Trim().ToLowerInvariant();
        var separator = normalized.IndexOf(';');
        return separator >= 0 ? normalized[..separator].Trim() : normalized;
    }

    private static bool IsMimeExtensionMatch(string contentType, string extension)
    {
        return (contentType, extension) switch
        {
            ("image/jpeg", ".jpg") => true,
            ("image/jpeg", ".jpeg") => true,
            ("image/png", ".png") => true,
            ("image/webp", ".webp") => true,
            _ => false
        };
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
