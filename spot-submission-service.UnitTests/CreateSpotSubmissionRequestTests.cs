using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class CreateSpotSubmissionRequestTests
{
    [Test]
    public void CanInstantiateAndAssignProperties()
    {
        // Arrange
        var parentCenter = new ParentCenterSubmissionRequest
        {
            Id = "pc1",
            Name = "Parent Center",
            ThumbnailUrl = "thumbUrl"
        };

        var request = new CreateSpotSubmissionRequest
        {
            Name = "Test Spot",
            Address = "123 Street",
            FoodType = "Pizza",
            PlaceType = "Restaurant",
            OpeningHours = "9-22",
            Latitude = 1.23,
            Longitude = 4.56,
            District = "Central",
            PhotoUrls = new List<string> { "url1", "url2" },
            PhotoStorageKeys = new List<string> { "key1", "key2" },
            IsCenter = true,
            ParentCenter = parentCenter
        };

        // Assert
        Assert.That(request.Name, Is.EqualTo("Test Spot"));
        Assert.That(request.Address, Is.EqualTo("123 Street"));
        Assert.That(request.FoodType, Is.EqualTo("Pizza"));
        Assert.That(request.PlaceType, Is.EqualTo("Restaurant"));
        Assert.That(request.OpeningHours, Is.EqualTo("9-22"));
        Assert.That(request.Latitude, Is.EqualTo(1.23));
        Assert.That(request.Longitude, Is.EqualTo(4.56));
        Assert.That(request.District, Is.EqualTo("Central"));
        Assert.That(request.PhotoUrls.Count, Is.EqualTo(2));
        Assert.That(request.PhotoStorageKeys.Count, Is.EqualTo(2));
        Assert.That(request.IsCenter, Is.True);
        Assert.That(request.ParentCenter, Is.Not.Null);
        Assert.That(request.ParentCenter!.Id, Is.EqualTo("pc1"));
    }

    [Test]
    public void DefaultValues_ShouldBeEmptyOrFalse()
    {
        var request = new CreateSpotSubmissionRequest();

        Assert.That(request.Name, Is.EqualTo(string.Empty));
        Assert.That(request.Address, Is.EqualTo(string.Empty));
        Assert.That(request.FoodType, Is.EqualTo(string.Empty));
        Assert.That(request.PlaceType, Is.EqualTo(string.Empty));
        Assert.That(request.OpeningHours, Is.EqualTo(string.Empty));
        Assert.That(request.Latitude, Is.EqualTo(0));
        Assert.That(request.Longitude, Is.EqualTo(0));
        Assert.That(request.District, Is.EqualTo(string.Empty));
        Assert.That(request.PhotoUrls, Is.Empty);
        Assert.That(request.PhotoStorageKeys, Is.Empty);
        Assert.That(request.IsCenter, Is.False);
        Assert.That(request.ParentCenter, Is.Null);
    }
}
