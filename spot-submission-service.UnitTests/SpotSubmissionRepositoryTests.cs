using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestFixture]
public class SpotSubmissionRepositoryTests
{
    private Mock<IAmazonDynamoDB> _mockDynamoDb = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private SpotSubmissionRepository _repository = null!;
    private const string TableName = "Submissions";
    private const string SpotTableName = "Spots";

    [SetUp]
    public void SetUp()
    {
        _mockDynamoDb = new Mock<IAmazonDynamoDB>();
        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["DynamoDb"]).Returns(TableName);
        _mockConfig.Setup(c => c["SpotsTable"]).Returns(SpotTableName);

        _repository = new SpotSubmissionRepository(_mockDynamoDb.Object, _mockConfig.Object);
    }

    private SpotSubmission CreateTestSubmission() =>
        new SpotSubmission
        {
            Id = "1",
            Name = "Test Spot",
            Address = "123 Street",
            Status = "pending",
            SubmittedBy = "user1",
            FoodType = "Pizza",
            PlaceType = "Restaurant",
            OpeningHours = "9-22",
            Latitude = 1.23,
            Longitude = 4.56,
            District = "Central",
            PhotoUrls = new List<string> { "url1", "url2" },
            PhotoStorageKeys = new List<string> { "key1", "key2" },
            IsCenter = false,
            Open = true,
            ParentCenter = new ParentCenterSubmission
            {
                Id = "pc1",
                Name = "Parent Center",
                ThumbnailUrl = "pcThumb"
            }
        };

    [Test]
    public async Task SaveAsync_ShouldCallPutItemAsync_WithAllFields()
    {
        var submission = CreateTestSubmission();

        _mockDynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());

        await _repository.SaveAsync(submission);

        _mockDynamoDb.Verify(d => d.PutItemAsync(It.Is<PutItemRequest>(r =>
            r.TableName == TableName &&
            r.Item["id"].S == submission.Id &&
            r.Item["photoUrls"].L.Count == 2 &&
            r.Item["photoStorageKeys"].L.Count == 2 &&
            r.Item["parentCenter"].M["id"].S == "pc1"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllAsync_ShouldReturnMappedSubmissions()
    {
        var response = new ScanResponse
        {
            Items = new List<Dictionary<string, AttributeValue>>
            {
                new()
                {
                    ["id"] = new AttributeValue { S = "1" },
                    ["name"] = new AttributeValue { S = "Spot1" },
                    ["address"] = new AttributeValue { S = "Addr1" },
                    ["status"] = new AttributeValue { S = "pending" },
                    ["photoUrls"] = new AttributeValue
                    {
                        L = new List<AttributeValue> { new() { S = "url1" } }
                    },
                    ["photoStorageKeys"] = new AttributeValue
                    {
                        L = new List<AttributeValue> { new() { S = "key1" } }
                    },
                    ["isCenter"] = new AttributeValue { BOOL = true },
                    ["open"] = new AttributeValue { BOOL = true }
                }
            }
        };

        _mockDynamoDb.Setup(d => d.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var results = await _repository.GetAllAsync();

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].PhotoUrls.Count, Is.EqualTo(1));
        Assert.That(results[0].IsCenter, Is.True);
        Assert.That(results[0].ThumbnailUrl, Is.EqualTo("url1"));
        Assert.That(results[0].ThumbnailStorageKey, Is.EqualTo("key1"));
    }

    [Test]
    public async Task GetByIdAsync_ShouldReturnSubmission_WhenFound()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = "1" },
            ["name"] = new AttributeValue { S = "Spot1" },
            ["address"] = new AttributeValue { S = "Addr1" },
            ["status"] = new AttributeValue { S = "pending" }
        };

        _mockDynamoDb.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = item });

        var result = await _repository.GetByIdAsync("1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Spot1"));
    }

    [Test]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotFound()
    {
        _mockDynamoDb.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        var result = await _repository.GetByIdAsync("999");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ApproveAsync_ShouldMoveToSpotTable_ThenDeleteOriginal()
    {
        var submission = CreateTestSubmission();

        _mockDynamoDb.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        _mockDynamoDb.Setup(d => d.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteItemResponse());

        await _repository.ApproveAsync(submission);

        _mockDynamoDb.Verify(d => d.PutItemAsync(It.Is<PutItemRequest>(r => r.TableName == SpotTableName), It.IsAny<CancellationToken>()), Times.Once);
        _mockDynamoDb.Verify(d => d.DeleteItemAsync(It.Is<DeleteItemRequest>(r => r.TableName == TableName), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ApproveAsync_ShouldThrow_WhenNoPhotos()
    {
        var submission = CreateTestSubmission();
        submission.PhotoUrls.Clear();

        Assert.ThrowsAsync<InvalidOperationException>(() => _repository.ApproveAsync(submission));
    }

    [Test]
    public async Task RejectAsync_ShouldCallUpdateStatusAsync()
    {
        _mockDynamoDb.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse());

        await _repository.RejectAsync("1");

        _mockDynamoDb.Verify(d => d.UpdateItemAsync(It.Is<UpdateItemRequest>(r =>
            r.ExpressionAttributeValues[":status"].S == "rejected"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExistsAsync_ShouldReturnTrue_WhenItemExists()
    {
        _mockDynamoDb.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = "1" } }
            });

        var result = await _repository.ExistsAsync("1");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ExistsAsync_ShouldReturnFalse_WhenItemDoesNotExist()
    {
        _mockDynamoDb.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        var result = await _repository.ExistsAsync("999");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_ShouldCallDeleteItemAsync()
    {
        _mockDynamoDb.Setup(d => d.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteItemResponse());

        await _repository.DeleteAsync("1");

        _mockDynamoDb.Verify(d => d.DeleteItemAsync(It.Is<DeleteItemRequest>(r =>
            r.TableName == TableName &&
            r.Key["id"].S == "1"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
