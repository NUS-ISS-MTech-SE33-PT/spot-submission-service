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
        _mockS3.Setup(s => s.Config.RegionEndpoint).Returns((Amazon.RegionEndpoint?)null);

        var method = typeof(PhotoUploadService)
            .GetMethod("ResolvePublicBaseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (string)method.Invoke(_service, null)!;

        Assert.That(result, Is.EqualTo("https://test-bucket.s3.amazonaws.com"));
    }
}
