using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

[TestFixture]
public class PhotoUploadServiceTests
{
    private Mock<IAmazonS3> _mockS3 = null!;
    private SpotSubmissionStorageOptions _options = null!;
    private PhotoUploadService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockS3 = new Mock<IAmazonS3>();
        _options = new SpotSubmissionStorageOptions
        {
            BucketName = "test-bucket",
            UrlExpiryMinutes = 10,
            KeyPrefix = "uploads",
            PublicBaseUrl = "https://cdn.example.com"
        };

        var mockOptions = new Mock<IOptions<SpotSubmissionStorageOptions>>();
        mockOptions.Setup(o => o.Value).Returns(_options);

        _service = new PhotoUploadService(_mockS3.Object, mockOptions.Object);
    }

    [Test]
    public void CreateUploadUrl_ShouldThrow_WhenBucketNameMissing()
    {
        _options.BucketName = string.Empty;
        var mockOptions = new Mock<IOptions<SpotSubmissionStorageOptions>>();
        mockOptions.Setup(o => o.Value).Returns(_options);

        var service = new PhotoUploadService(_mockS3.Object, mockOptions.Object);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateUploadUrl("file.jpg", "image/jpeg", "user123"));
    }

    [Test]
    public void CreateUploadUrl_ShouldReturnExpectedValues()
    {
        CreatePresignedPostRequest? capturedRequest = null;
        _mockS3.Setup(s3 => s3.CreatePresignedPost(It.IsAny<CreatePresignedPostRequest>()))
               .Callback<CreatePresignedPostRequest>(request => capturedRequest = request)
               .Returns(new CreatePresignedPostResponse
               {
                   Url = "https://presigned.example.com/upload",
                   Fields = new Dictionary<string, string>(StringComparer.Ordinal)
                   {
                       ["key"] = "uploads/user123/photo.jpg",
                       ["Content-Type"] = "image/jpeg"
                   }
               });

        var result = _service.CreateUploadUrl("photo.jpg", "image/jpeg", "user123");

        Assert.That(result.UploadUrl, Is.EqualTo("https://presigned.example.com/upload"));
        Assert.That(result.UploadFields["Content-Type"], Is.EqualTo("image/jpeg"));
        Assert.That(result.FileUrl, Does.StartWith("https://cdn.example.com/uploads/user123/"));
        Assert.That(result.StorageKey, Does.EndWith(".jpg"));
        Assert.That(result.ExpiresAt, Is.GreaterThan(DateTime.UtcNow));
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.BucketName, Is.EqualTo("test-bucket"));
        Assert.That(capturedRequest.Key, Does.StartWith("uploads/user123/"));

        var sizeCondition = capturedRequest.Conditions.OfType<ContentLengthRangeCondition>().Single();
        Assert.That(sizeCondition.MinimumLength, Is.EqualTo(1));
        Assert.That(sizeCondition.MaximumLength, Is.EqualTo(5 * 1024 * 1024));

        var contentTypeCondition = capturedRequest.Conditions
            .OfType<ExactMatchCondition>()
            .Single(condition => condition.FieldName == "Content-Type");
        Assert.That(contentTypeCondition.ExpectedValue, Is.EqualTo("image/jpeg"));
    }

    [Test]
    public void CreateUploadUrl_ShouldHandleNullUserSubject()
    {
        _mockS3.Setup(s3 => s3.CreatePresignedPost(It.IsAny<CreatePresignedPostRequest>()))
               .Returns(new CreatePresignedPostResponse
               {
                   Url = "https://presigned.example.com/upload",
                   Fields = new Dictionary<string, string>(StringComparer.Ordinal)
                   {
                       ["key"] = "uploads/photo.png",
                       ["Content-Type"] = "image/png"
                   }
               });

        var result = _service.CreateUploadUrl("photo.png", "image/png", null);

        Assert.That(result.FileUrl, Does.StartWith("https://cdn.example.com/uploads/"));
    }

    [Test]
    public void NormalizeSubject_ShouldRemoveInvalidCharacters()
    {
        var method = typeof(PhotoUploadService)
            .GetMethod("NormalizeSubject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (string?)method.Invoke(null, new object?[] { "user!@#_123" });

        Assert.That(result, Is.EqualTo("user_123"));
    }

    [Test]
    public void NormalizeSubject_ShouldReturnNull_WhenEmpty()
    {
        var method = typeof(PhotoUploadService)
            .GetMethod("NormalizeSubject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (string?)method.Invoke(null, new object?[] { "!!!" });

        Assert.That(result, Is.Null);
    }

    [TestCase("photo.jpg", ExpectedResult = ".jpg")]
    [TestCase("photo.JPG", ExpectedResult = ".jpg")]
    [TestCase("noextension", ExpectedResult = "")]
    [TestCase("", ExpectedResult = "")]
    [TestCase(".invalidexttoolongname", ExpectedResult = "")]
    public string ExtractExtension_ShouldHandleVariousCases(string fileName)
    {
        var method = typeof(PhotoUploadService)
            .GetMethod("ExtractExtension", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return (string)method.Invoke(null, new object?[] { fileName })!;
    }

    [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [TestCase(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [TestCase(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [TestCase(new byte[] { 0x01, 0x02, 0x03, 0x04 }, "")]
    public void DetectContentTypeFromHeader_ShouldIdentifySupportedFormats(byte[] header, string expectedContentType)
    {
        var method = typeof(PhotoUploadService)
            .GetMethod("DetectContentTypeFromHeader", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, new object?[] { header })!;

        Assert.That(result, Is.EqualTo(expectedContentType));
    }

    [Test]
    public void ValidateUploadedObjectAsync_ShouldRejectMismatchedFileHeader()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.ValidateUploadedObjectAsync(storageKey, photoUrl));

        Assert.That(ex!.Message, Is.EqualTo("Uploaded file header does not match its content type."));
    }

    [Test]
    public async Task ValidateUploadedObjectAsync_ShouldAcceptMatchingFileHeader()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        await _service.ValidateUploadedObjectAsync(storageKey, photoUrl);
    }

    [Test]
    public async Task ValidateUploadedObjectAsync_ShouldRequireCleanScanStatus_WhenEnforced()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        _options.EnforceScanStatus = true;
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        SetupObjectTags(storageKey, "clean");

        await _service.ValidateUploadedObjectAsync(storageKey, photoUrl);
    }

    [Test]
    public void ValidateUploadedObjectAsync_ShouldRejectMissingScanStatus_WhenEnforced()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        _options.EnforceScanStatus = true;
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        SetupObjectTags(storageKey);

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.ValidateUploadedObjectAsync(storageKey, photoUrl));

        Assert.That(ex!.Message, Is.EqualTo("Uploaded photo is still being scanned."));
    }

    [Test]
    public void ValidateUploadedObjectAsync_ShouldRejectInfectedScanStatus_WhenEnforced()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        _options.EnforceScanStatus = true;
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        SetupObjectTags(storageKey, "infected");

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.ValidateUploadedObjectAsync(storageKey, photoUrl));

        Assert.That(ex!.Message, Is.EqualTo("Uploaded photo failed malware scan."));
    }

    [Test]
    public void ValidateUploadedObjectAsync_ShouldRejectScanErrors_WhenEnforced()
    {
        const string storageKey = "uploads/user123/photo.png";
        const string photoUrl = "https://cdn.example.com/uploads/user123/photo.png";
        _options.EnforceScanStatus = true;
        SetupMetadata(storageKey, "image/png", 1024);
        SetupObjectHeader(storageKey, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        SetupObjectTags(storageKey, "error");

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.ValidateUploadedObjectAsync(storageKey, photoUrl));

        Assert.That(ex!.Message, Is.EqualTo("Uploaded photo could not be scanned."));
    }

    [Test]
    public void ResolvePublicBaseUrl_ShouldUseConfiguredValue()
    {
        var method = typeof(PhotoUploadService)
            .GetMethod("ResolvePublicBaseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (string)method.Invoke(_service, null)!;

        Assert.That(result, Is.EqualTo("https://cdn.example.com"));
    }

    [Test]
    public void ResolvePublicBaseUrl_ShouldFallbackToDefault_WhenRegionMissing()
    {
        _options.PublicBaseUrl = null;
        _mockS3.Setup(s => s.Config.RegionEndpoint).Returns((Amazon.RegionEndpoint)null!);

        var method = typeof(PhotoUploadService)
            .GetMethod("ResolvePublicBaseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (string)method.Invoke(_service, null)!;

        Assert.That(result, Is.EqualTo("https://test-bucket.s3.amazonaws.com"));
    }

    private void SetupMetadata(string storageKey, string contentType, long contentLength)
    {
        var response = new GetObjectMetadataResponse();
        response.Headers.ContentType = contentType;
        response.Headers.ContentLength = contentLength;

        _mockS3.Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == _options.BucketName &&
                    request.Key == storageKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private void SetupObjectHeader(string storageKey, byte[] headerBytes)
    {
        _mockS3.Setup(s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(request =>
                    request.BucketName == _options.BucketName &&
                    request.Key == storageKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(headerBytes)
            });
    }

    private void SetupObjectTags(string storageKey, string? scanStatus = null)
    {
        var response = new GetObjectTaggingResponse
        {
            Tagging = new List<Tag>()
        };
        if (scanStatus != null)
        {
            response.Tagging.Add(new Tag
            {
                Key = _options.ScanStatusTagKey,
                Value = scanStatus
            });
        }

        _mockS3.Setup(s3 => s3.GetObjectTaggingAsync(
                It.Is<GetObjectTaggingRequest>(request =>
                    request.BucketName == _options.BucketName &&
                    request.Key == storageKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }
}
