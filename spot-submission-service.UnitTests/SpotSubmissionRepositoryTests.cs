using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestFixture]
public class SpotSubmissionRepositoryTests
{
    private Mock<IAmazonDynamoDB> _mockDynamoDb = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private SpotSubmissionRepository _repository = null!;
    private const string TableName = "SpotSubmissionTable";

    [SetUp]
    public void SetUp()
    {
        _mockDynamoDb = new Mock<IAmazonDynamoDB>();
        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["DynamoDb"]).Returns(TableName);
        _repository = new SpotSubmissionRepository(_mockDynamoDb.Object, _mockConfig.Object);
    }

    [Test]
    public async Task SaveAsync_ShouldCallPutItemAsync_WithAllFields()
    {
        // Arrange
        var submission = new SpotSubmission
        {
            Id = "1",
            Name = "Food Place",
            Address = "123 Street",
            Status = "pending",
            PhotoUrl = "http://photo.jpg",
            PhotoStorageKey = "photos/key.jpg"
        };

        _mockDynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());

        // Act
        await _repository.SaveAsync(submission);

        // Assert
        _mockDynamoDb.Verify(d => d.PutItemAsync(It.Is<PutItemRequest>(r =>
            r.TableName == TableName &&
            r.Item["id"].S == submission.Id &&
            r.Item["name"].S == submission.Name &&
            r.Item["address"].S == submission.Address &&
            r.Item["status"].S == submission.Status &&
            r.Item["photoUrl"].S == submission.PhotoUrl &&
            r.Item["photoStorageKey"].S == submission.PhotoStorageKey
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SaveAsync_ShouldNotIncludeOptionalFields_WhenNull()
    {
        // Arrange
        var submission = new SpotSubmission
        {
            Id = "2",
            Name = "No Photo",
            Address = "456 Road",
            Status = "pending"
        };

        _mockDynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());

        // Act
        await _repository.SaveAsync(submission);

        // Assert
        _mockDynamoDb.Verify(d => d.PutItemAsync(It.Is<PutItemRequest>(r =>
            !r.Item.ContainsKey("photoUrl") &&
            !r.Item.ContainsKey("photoStorageKey")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllAsync_ShouldMapItemsCorrectly()
    {
        // Arrange
        var response = new ScanResponse
        {
            Items = new List<Dictionary<string, AttributeValue>>
            {
                new()
                {
                    ["id"] = new AttributeValue { S = "1" },
                    ["name"] = new AttributeValue { S = "Place 1" },
                    ["address"] = new AttributeValue { S = "Addr 1" },
                    ["photoUrl"] = new AttributeValue { S = "url1" },
                    ["photoStorageKey"] = new AttributeValue { S = "key1" },
                    ["status"] = new AttributeValue { S = "approved" }
                },
                new()
                {
                    ["id"] = new AttributeValue { S = "2" },
                    ["name"] = new AttributeValue { S = "Place 2" },
                    ["address"] = new AttributeValue { S = "Addr 2" },
                    ["status"] = new AttributeValue { S = "pending" }
                }
            }
        };

        _mockDynamoDb
            .Setup(d => d.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var results = await _repository.GetAllAsync();

        // Assert
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].PhotoUrl, Is.EqualTo("url1"));
        Assert.That(results[1].PhotoUrl, Is.Null);
    }

    [Test]
    public async Task ApproveAsync_ShouldCallUpdateStatusAsync_WithApproved()
    {
        // Arrange
        _mockDynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse());

        // Act
        await _repository.ApproveAsync("1");

        // Assert
        _mockDynamoDb.Verify(d => d.UpdateItemAsync(It.Is<UpdateItemRequest>(r =>
            r.TableName == TableName &&
            r.Key["id"].S == "1" &&
            r.ExpressionAttributeValues[":status"].S == "approved"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RejectAsync_ShouldCallUpdateStatusAsync_WithRejected()
    {
        // Arrange
        _mockDynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse());

        // Act
        await _repository.RejectAsync("2");

        // Assert
        _mockDynamoDb.Verify(d => d.UpdateItemAsync(It.Is<UpdateItemRequest>(r =>
            r.ExpressionAttributeValues[":status"].S == "rejected"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExistsAsync_ShouldReturnTrue_WhenItemExists()
    {
        // Arrange
        var response = new GetItemResponse
        {
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = "1" },
                ["name"] = new AttributeValue { S = "Test" }
            }
        };

        _mockDynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _repository.ExistsAsync("1");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ExistsAsync_ShouldReturnFalse_WhenItemDoesNotExist()
    {
        // Arrange
        var response = new GetItemResponse { Item = new Dictionary<string, AttributeValue>() };
        _mockDynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _repository.ExistsAsync("999");

        // Assert
        Assert.That(result, Is.False);
    }
}
