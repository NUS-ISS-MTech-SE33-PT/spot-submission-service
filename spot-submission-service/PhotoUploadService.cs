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
        var normalizedContentType = NormalizeContentType(contentType);
        var request = new CreatePresignedPostRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Expires = expiresAt
        };
        request.Fields["Content-Type"] = normalizedContentType;
        request.Conditions.Add(S3PostCondition.ExactMatch("Content-Type", normalizedContentType));
        request.Conditions.Add(S3PostCondition.ContentLengthRange(1, ResolveMaxUploadBytes()));

        var presignedPost = _s3.CreatePresignedPost(request);
        var baseUrl = ResolvePublicBaseUrl();
        var fileUrl = $"{baseUrl}/{key}";

        return new PhotoUploadDescriptor
        {
            UploadUrl = presignedPost.Url,
            UploadFields = new Dictionary<string, string>(presignedPost.Fields, StringComparer.Ordinal),
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

        var maxUploadBytes = ResolveMaxUploadBytes();
        if (metadata.Headers.ContentLength > maxUploadBytes)
        {
            throw new ArgumentException($"File size exceeds limit ({maxUploadBytes} bytes).");
        }

        var contentType = NormalizeContentType(metadata.Headers.ContentType ?? metadata.ContentType);
        if (!IsAllowed(_options.AllowedContentTypes, contentType))
        {
            throw new ArgumentException("Unsupported content type.");
        }

        var fileHeader = await ReadFileHeaderAsync(storageKey, cancellationToken);
        var detectedContentType = DetectContentTypeFromHeader(fileHeader);
        if (!IsAllowed(_options.AllowedContentTypes, detectedContentType))
        {
            throw new ArgumentException("Unsupported file header.");
        }

        if (!string.Equals(detectedContentType, contentType, StringComparison.Ordinal))
        {
            throw new ArgumentException("Uploaded file header does not match its content type.");
        }

        if (!IsMimeExtensionMatch(detectedContentType, extension))
        {
            throw new ArgumentException("Uploaded file header does not match its file extension.");
        }

        if (!IsMimeExtensionMatch(contentType, extension))
        {
            throw new ArgumentException("Uploaded file format does not match its content type.");
        }

        await ValidateScanStatusAsync(storageKey, cancellationToken);
    }

    public bool IsOwnedByUser(string storageKey, string photoUrl, string userSubject)
    {
        var normalizedSubject = NormalizeSubject(userSubject);
        if (string.IsNullOrWhiteSpace(normalizedSubject))
        {
            return false;
        }

        var key = storageKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var prefix = _options.KeyPrefix?.Trim('/') ?? string.Empty;
        var ownerPrefix = string.IsNullOrEmpty(prefix)
            ? $"{normalizedSubject}/"
            : $"{prefix}/{normalizedSubject}/";

        if (!key.StartsWith(ownerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expectedUrl = $"{ResolvePublicBaseUrl()}/{key}";
        return string.Equals(photoUrl?.Trim(), expectedUrl, StringComparison.Ordinal);
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

    private async Task<byte[]> ReadFileHeaderAsync(string storageKey, CancellationToken cancellationToken)
    {
        const int headerLength = 16;
        using var response = await _s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            ByteRange = new ByteRange(0, headerLength - 1)
        }, cancellationToken);

        var buffer = new byte[headerLength];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await response.ResponseStream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead == buffer.Length ? buffer : buffer[..totalRead];
    }

    private static string DetectContentTypeFromHeader(byte[] headerBytes)
    {
        if (headerBytes.Length >= 3 &&
            headerBytes[0] == 0xFF &&
            headerBytes[1] == 0xD8 &&
            headerBytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (headerBytes.Length >= 8 &&
            headerBytes[0] == 0x89 &&
            headerBytes[1] == 0x50 &&
            headerBytes[2] == 0x4E &&
            headerBytes[3] == 0x47 &&
            headerBytes[4] == 0x0D &&
            headerBytes[5] == 0x0A &&
            headerBytes[6] == 0x1A &&
            headerBytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (headerBytes.Length >= 12 &&
            headerBytes[0] == 0x52 &&
            headerBytes[1] == 0x49 &&
            headerBytes[2] == 0x46 &&
            headerBytes[3] == 0x46 &&
            headerBytes[8] == 0x57 &&
            headerBytes[9] == 0x45 &&
            headerBytes[10] == 0x42 &&
            headerBytes[11] == 0x50)
        {
            return "image/webp";
        }

        return string.Empty;
    }

    private async Task ValidateScanStatusAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (!_options.EnforceScanStatus)
        {
            return;
        }

        var tagKey = _options.ScanStatusTagKey?.Trim();
        if (string.IsNullOrWhiteSpace(tagKey))
        {
            throw new InvalidOperationException("SpotSubmissionStorage:ScanStatusTagKey is not configured.");
        }

        var requiredStatus = NormalizeTagValue(_options.RequiredScanStatus);
        if (string.IsNullOrWhiteSpace(requiredStatus))
        {
            throw new InvalidOperationException("SpotSubmissionStorage:RequiredScanStatus is not configured.");
        }

        var taggingResponse = await _s3.GetObjectTaggingAsync(new GetObjectTaggingRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey
        }, cancellationToken);

        var scanStatus = taggingResponse.Tagging?
            .FirstOrDefault(tag => string.Equals(tag.Key, tagKey, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(scanStatus))
        {
            throw new ArgumentException("Uploaded photo is still being scanned.");
        }

        var normalizedStatus = NormalizeTagValue(scanStatus);
        if (string.Equals(normalizedStatus, requiredStatus, StringComparison.Ordinal))
        {
            return;
        }

        throw normalizedStatus switch
        {
            "infected" => new ArgumentException("Uploaded photo failed malware scan."),
            "error" => new ArgumentException("Uploaded photo could not be scanned."),
            _ => new ArgumentException("Uploaded photo is still being scanned.")
        };
    }

    private static string NormalizeTagValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
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

    private long ResolveMaxUploadBytes()
    {
        return _options.MaxUploadBytes > 0 ? _options.MaxUploadBytes : 5 * 1024 * 1024;
    }
}
