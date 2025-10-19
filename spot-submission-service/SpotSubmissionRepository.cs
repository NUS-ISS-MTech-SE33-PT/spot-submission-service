using System.Globalization;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

public class SpotSubmissionRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly string _spotTableName;

    public SpotSubmissionRepository(IAmazonDynamoDB dynamoDb, IConfiguration configuration)
    {
        _dynamoDb = dynamoDb;
        _tableName = configuration["DynamoDb"]
            ?? throw new InvalidOperationException("DynamoDb configuration missing.");
        _spotTableName = configuration["SpotsTable"]
            ?? throw new InvalidOperationException("SpotsTable configuration missing.");
    }

    public async Task SaveAsync(SpotSubmission submission)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = submission.Id },
            ["name"] = new AttributeValue { S = submission.Name },
            ["address"] = new AttributeValue { S = submission.Address },
            ["foodType"] = new AttributeValue { S = submission.FoodType },
            ["placeType"] = new AttributeValue { S = submission.PlaceType },
            ["openingHours"] = new AttributeValue { S = submission.OpeningHours },
            ["latitude"] = new AttributeValue
            {
                N = submission.Latitude.ToString(CultureInfo.InvariantCulture)
            },
            ["longitude"] = new AttributeValue
            {
                N = submission.Longitude.ToString(CultureInfo.InvariantCulture)
            },
            ["district"] = new AttributeValue { S = submission.District },
            ["status"] = new AttributeValue { S = submission.Status },
            ["submittedBy"] = new AttributeValue { S = submission.SubmittedBy },
            ["isCenter"] = new AttributeValue { BOOL = submission.IsCenter },
            ["open"] = new AttributeValue { BOOL = submission.Open },
        };

        var photoValues = submission.PhotoUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => new AttributeValue { S = url })
            .ToList();
        if (photoValues.Count > 0)
        {
            item["photoUrls"] = new AttributeValue { L = photoValues };
            item["photoUrl"] = new AttributeValue { S = submission.ThumbnailUrl };
        }

        var storageValues = submission.PhotoStorageKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => new AttributeValue { S = key })
            .ToList();
        if (storageValues.Count > 0)
        {
            item["photoStorageKeys"] = new AttributeValue { L = storageValues };
            item["photoStorageKey"] = new AttributeValue { S = submission.ThumbnailStorageKey };
        }

        if (submission.ParentCenter != null)
        {
            item["parentCenter"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = submission.ParentCenter.Id },
                    ["name"] = new AttributeValue { S = submission.ParentCenter.Name },
                    ["thumbnailUrl"] = new AttributeValue { S = submission.ParentCenter.ThumbnailUrl }
                }
            };
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
        return response.Items.Select(MapSubmission).ToList();
    }

    public async Task<SpotSubmission?> GetByIdAsync(string id)
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
        if (response.Item == null || response.Item.Count == 0)
        {
            return null;
        }

        return MapSubmission(response.Item);
    }

    public async Task ApproveAsync(SpotSubmission submission)
    {
        await MoveToSpotTableAsync(submission);
        await DeleteAsync(submission.Id);
    }

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

    public async Task DeleteAsync(string id)
    {
        var request = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id }
            }
        };

        await _dynamoDb.DeleteItemAsync(request);
    }

    private async Task MoveToSpotTableAsync(SpotSubmission submission)
    {
        var photoValues = submission.PhotoUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => new AttributeValue { S = url })
            .ToList();

        if (photoValues.Count == 0)
        {
            throw new InvalidOperationException("At least one photo is required to approve a submission.");
        }

        var item = new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = submission.Id },
            ["name"] = new AttributeValue { S = submission.Name },
            ["address"] = new AttributeValue { S = submission.Address },
            ["foodType"] = new AttributeValue { S = submission.FoodType },
            ["rating"] = new AttributeValue { N = "0" },
            ["openingHours"] = new AttributeValue { S = submission.OpeningHours },
            ["photos"] = new AttributeValue { L = photoValues },
            ["placeType"] = new AttributeValue { S = submission.PlaceType },
            ["avgPrice"] = new AttributeValue { N = "0" },
            ["open"] = new AttributeValue { BOOL = submission.Open },
            ["isCenter"] = new AttributeValue { BOOL = submission.IsCenter },
            ["latitude"] = new AttributeValue
            {
                N = submission.Latitude.ToString(CultureInfo.InvariantCulture)
            },
            ["longitude"] = new AttributeValue
            {
                N = submission.Longitude.ToString(CultureInfo.InvariantCulture)
            },
            ["tasteAvg"] = new AttributeValue { N = "0" },
            ["serviceAvg"] = new AttributeValue { N = "0" },
            ["environmentAvg"] = new AttributeValue { N = "0" },
            ["district"] = new AttributeValue { S = submission.District },
            ["thumbnailUrl"] = new AttributeValue { S = submission.ThumbnailUrl }
        };

        if (submission.ParentCenter != null)
        {
            item["parentCenter"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = submission.ParentCenter.Id },
                    ["name"] = new AttributeValue { S = submission.ParentCenter.Name },
                    ["thumbnailUrl"] = new AttributeValue { S = submission.ParentCenter.ThumbnailUrl }
                }
            };
        }

        var request = new PutItemRequest
        {
            TableName = _spotTableName,
            Item = item
        };

        await _dynamoDb.PutItemAsync(request);
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

    private static SpotSubmission MapSubmission(Dictionary<string, AttributeValue> item)
    {
        var photoUrls = new List<string>();
        if (item.TryGetValue("photoUrls", out var photoUrlsValue) && photoUrlsValue.L != null)
        {
            foreach (var attribute in photoUrlsValue.L)
            {
                if (!string.IsNullOrWhiteSpace(attribute.S))
                {
                    photoUrls.Add(attribute.S);
                }
            }
        }
        if (photoUrls.Count == 0 &&
            item.TryGetValue("photoUrl", out var legacyPhoto) &&
            !string.IsNullOrWhiteSpace(legacyPhoto.S))
        {
            photoUrls.Add(legacyPhoto.S);
        }

        var photoStorageKeys = new List<string>();
        if (item.TryGetValue("photoStorageKeys", out var storageValue) && storageValue.L != null)
        {
            foreach (var attribute in storageValue.L)
            {
                if (!string.IsNullOrWhiteSpace(attribute.S))
                {
                    photoStorageKeys.Add(attribute.S);
                }
            }
        }
        if (photoStorageKeys.Count == 0 &&
            item.TryGetValue("photoStorageKey", out var legacyStorage) &&
            !string.IsNullOrWhiteSpace(legacyStorage.S))
        {
            photoStorageKeys.Add(legacyStorage.S);
        }

        var submission = new SpotSubmission
        {
            Id = item["id"].S,
            Name = item["name"].S,
            Address = item["address"].S,
            Status = item.TryGetValue("status", out var statusValue) ? statusValue.S ?? "pending" : "pending",
            SubmittedBy = item.TryGetValue("submittedBy", out var submittedByValue) ? submittedByValue.S ?? string.Empty : string.Empty,
            FoodType = item.TryGetValue("foodType", out var foodTypeValue) ? foodTypeValue.S ?? string.Empty : string.Empty,
            PlaceType = item.TryGetValue("placeType", out var placeTypeValue) ? placeTypeValue.S ?? string.Empty : string.Empty,
            OpeningHours = item.TryGetValue("openingHours", out var openingHoursValue) ? openingHoursValue.S ?? string.Empty : string.Empty,
            District = item.TryGetValue("district", out var districtValue) ? districtValue.S ?? string.Empty : string.Empty,
            PhotoUrls = photoUrls,
            PhotoStorageKeys = photoStorageKeys,
            IsCenter = item.TryGetValue("isCenter", out var isCenterValue) &&
                (isCenterValue.BOOL.HasValue
                    ? isCenterValue.BOOL.Value
                    : bool.TryParse(isCenterValue.S, out var parsed) && parsed),
            ParentCenter = item.TryGetValue("parentCenter", out var parentCenterValue) && parentCenterValue.M != null
                ? new ParentCenterSubmission
                {
                    Id = parentCenterValue.M.TryGetValue("id", out var parentId)
                        ? parentId.S ?? string.Empty
                        : string.Empty,
                    Name = parentCenterValue.M.TryGetValue("name", out var parentName)
                        ? parentName.S ?? string.Empty
                        : string.Empty,
                    ThumbnailUrl = parentCenterValue.M.TryGetValue("thumbnailUrl", out var parentThumb)
                        ? parentThumb.S ?? string.Empty
                        : string.Empty,
                }
                : null,
            Open = item.TryGetValue("open", out var openValue) && openValue.BOOL.HasValue ? openValue.BOOL.Value : true,
        };

        if (item.TryGetValue("latitude", out var latitudeValue) &&
            !string.IsNullOrWhiteSpace(latitudeValue.N))
        {
            submission.Latitude = double.Parse(latitudeValue.N, CultureInfo.InvariantCulture);
        }

        if (item.TryGetValue("longitude", out var longitudeValue) &&
            !string.IsNullOrWhiteSpace(longitudeValue.N))
        {
            submission.Longitude = double.Parse(longitudeValue.N, CultureInfo.InvariantCulture);
        }

        return submission;
    }
}
