using NUnit.Framework;

[TestFixture]
public class CreatePhotoUploadUrlRequestTests
{
    [Test]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var request = new CreatePhotoUploadUrlRequest();

        // Act
        request.FileName = "photo.jpg";
        request.ContentType = "image/jpeg";

        // Assert
        Assert.That(request.FileName, Is.EqualTo("photo.jpg"));
        Assert.That(request.ContentType, Is.EqualTo("image/jpeg"));
    }

    [Test]
    public void DefaultValues_ShouldBeEmptyStrings()
    {
        // Arrange & Act
        var request = new CreatePhotoUploadUrlRequest();

        // Assert
        Assert.That(request.FileName, Is.EqualTo(string.Empty));
        Assert.That(request.ContentType, Is.EqualTo(string.Empty));
    }
}
