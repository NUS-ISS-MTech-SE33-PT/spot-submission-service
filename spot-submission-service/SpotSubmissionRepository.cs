using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

public class SpotSubmissionRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public SpotSubmissionRepository(IAmazonDynamoDB dynamoDb, IConfiguration configuration)
    {
        _dynamoDb = dynamoDb;
        _tableName = configuration["DynamoDb"]!;
    }

    public async Task SaveAsync(SpotSubmission submission)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = submission.Id },
            ["name"] = new AttributeValue { S = submission.Name },
            ["address"] = new AttributeValue { S = submission.Address },
            ["status"] = new AttributeValue { S = submission.Status },
            ["submittedBy"] = new AttributeValue { S = submission.SubmittedBy }
        };

        if (!string.IsNullOrWhiteSpace(submission.PhotoUrl))
        {
            item["photoUrl"] = new AttributeValue { S = submission.PhotoUrl };
        }

        if (!string.IsNullOrWhiteSpace(submission.PhotoStorageKey))
        {
            item["photoStorageKey"] = new AttributeValue { S = submission.PhotoStorageKey };
        }

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        };

        await _dynamoDb.PutItemAsync(request);
    }

    public async Task<List<SpotSubmission>> GetAllAsync()
    {
        var request = new ScanRequest
        {
            TableName = _tableName
        };

        var response = await _dynamoDb.ScanAsync(request);

        return response.Items.Select(item => new SpotSubmission
        {
            Id = item["id"].S,
            Name = item["name"].S,
            Address = item["address"].S,
            PhotoUrl = item.TryGetValue("photoUrl", out var photoUrl) ? photoUrl.S : null,
            PhotoStorageKey = item.TryGetValue("photoStorageKey", out var photoStorageKey) ? photoStorageKey.S : null,
            Status = item["status"].S,
            SubmittedBy = item.TryGetValue("submittedBy", out var submittedBy) ? submittedBy.S : string.Empty
        }).ToList();
    }

    private async Task UpdateStatusAsync(string id, string status)
    {
        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id }
            },
            UpdateExpression = "SET #s = :status",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#s"] = "status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new AttributeValue { S = status }
            }
        };

        await _dynamoDb.UpdateItemAsync(request);
    }

    public Task ApproveAsync(string id) => UpdateStatusAsync(id, "approved");
    public Task RejectAsync(string id) => UpdateStatusAsync(id, "rejected");

    public async Task<bool> ExistsAsync(string id)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        return response.Item != null && response.Item.Count > 0;
    }
}
