using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<SpotSubmissionRepository>();
builder.Services.Configure<SpotSubmissionStorageOptions>(builder.Configuration.GetSection("SpotSubmissionStorage"));
builder.Services.AddScoped<PhotoUploadService>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var logger = app.Logger;
app.Use(async (context, next) =>
{
    var utcNow = DateTime.UtcNow.ToString("o");
    var method = context.Request.Method;
    var path = context.Request.Path;
    var headers = string.Join("; ", context.Request.Headers.Select(h => $"{h.Key}: {h.Value}"));

    logger.LogInformation("{UtcNow}\t{Method}\t{Path} | Headers: {Headers}",
        utcNow, method, path, headers);

    await next.Invoke();
});

// GET /spots/submissions/health
app.MapGet("/spots/submissions/health", () => Results.Ok(DateTime.Now));

// POST /spots/submissions/photos/presign
app.MapPost("/spots/submissions/photos/presign",
    (HttpContext httpContext, [FromBody] CreatePhotoUploadUrlRequest request, [FromServices] PhotoUploadService uploadService) =>
    {
        var subject = JwtSubjectResolver.ResolveUserId(httpContext);

        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentType))
        {
            return Results.BadRequest(new { message = "fileName and contentType are required." });
        }

        PhotoUploadDescriptor descriptor;
        try
        {
            descriptor = uploadService.CreateUploadUrl(request.FileName, request.ContentType, subject);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        return Results.Ok(new CreatePhotoUploadUrlResponse
        {
            UploadUrl = descriptor.UploadUrl,
            PhotoUrl = descriptor.FileUrl,
            StorageKey = descriptor.StorageKey,
            ExpiresAt = descriptor.ExpiresAt
        });
    });

// POST /spots/submissions
app.MapPost("/spots/submissions",
    async (HttpContext httpContext, CreateSpotSubmissionRequest request, [FromServices] SpotSubmissionRepository repo, [FromServices] PhotoUploadService uploadService) =>
{
    var subject = JwtSubjectResolver.ResolveUserId(httpContext);
    if (string.IsNullOrWhiteSpace(subject))
    {
        return Results.Json(
            new { message = "Unauthorized — login required before submitting a new spot" },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.Address) ||
        string.IsNullOrWhiteSpace(request.PlaceType) ||
        string.IsNullOrWhiteSpace(request.OpeningHours) ||
        string.IsNullOrWhiteSpace(request.District))
    {
        return Results.BadRequest(new
        {
            message = "name, address, placeType, openingHours, and district are required."
        });
    }

    if (request.PhotoUrls == null || request.PhotoStorageKeys == null ||
        request.PhotoUrls.Count == 0 || request.PhotoStorageKeys.Count == 0)
    {
        return Results.BadRequest(new { message = "At least one photo is required." });
    }

    if (request.PhotoUrls.Count != request.PhotoStorageKeys.Count)
    {
        return Results.BadRequest(new { message = "photoUrls and photoStorageKeys must have the same length." });
    }

    if (request.PhotoUrls.Any(string.IsNullOrWhiteSpace) ||
        request.PhotoStorageKeys.Any(string.IsNullOrWhiteSpace))
    {
        return Results.BadRequest(new { message = "photoUrls and photoStorageKeys cannot contain empty values." });
    }

    var placeType = request.PlaceType.Trim();
    var isCenter = request.IsCenter;
    var requiresParentCenter = placeType.Equals("Hawker Stall", StringComparison.OrdinalIgnoreCase)
        || placeType.Equals("Food Court Stall", StringComparison.OrdinalIgnoreCase);

    if (!isCenter && string.IsNullOrWhiteSpace(request.FoodType))
    {
        return Results.BadRequest(new { message = "foodType is required for non-center submissions." });
    }

    if (isCenter && !placeType.Equals("Food Center", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { message = "placeType must be 'Food Center' when isCenter is true." });
    }

    if (requiresParentCenter && request.ParentCenter == null)
    {
        return Results.BadRequest(new { message = "Hawker stalls and food court stalls must specify a parent centre." });
    }

    if (request.ParentCenter != null &&
        (string.IsNullOrWhiteSpace(request.ParentCenter.Id) ||
         string.IsNullOrWhiteSpace(request.ParentCenter.Name)))
    {
        return Results.BadRequest(new { message = "parentCenter.id and parentCenter.name are required when parentCenter is provided." });
    }

    if (double.IsNaN(request.Latitude) || double.IsInfinity(request.Latitude) ||
        request.Latitude < -90 || request.Latitude > 90)
    {
        return Results.BadRequest(new { message = "latitude must be between -90 and 90." });
    }

    if (double.IsNaN(request.Longitude) || double.IsInfinity(request.Longitude) ||
        request.Longitude < -180 || request.Longitude > 180)
    {
        return Results.BadRequest(new { message = "longitude must be between -180 and 180." });
    }

    var photoUrls = request.PhotoUrls.Select(url => url.Trim()).ToList();
    var photoStorageKeys = request.PhotoStorageKeys.Select(key => key.Trim()).ToList();
    var foodTypeValue = isCenter
        ? (string.IsNullOrWhiteSpace(request.FoodType) ? "Food Centre" : request.FoodType.Trim())
        : request.FoodType.Trim();

    try
    {
        for (var i = 0; i < photoStorageKeys.Count; i++)
        {
            await uploadService.ValidateUploadedObjectAsync(photoStorageKeys[i], photoUrls[i]);
        }
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (Amazon.S3.AmazonS3Exception)
    {
        return Results.BadRequest(new { message = "Uploaded photo is missing or inaccessible." });
    }

    ParentCenterSubmission? parentCenter = null;
    if (!isCenter && request.ParentCenter != null)
    {
        parentCenter = new ParentCenterSubmission
        {
            Id = request.ParentCenter.Id.Trim(),
            Name = request.ParentCenter.Name.Trim(),
            ThumbnailUrl = request.ParentCenter.ThumbnailUrl?.Trim() ?? string.Empty,
        };
    }

    var submission = new SpotSubmission
    {
        Name = request.Name.Trim(),
        Address = request.Address.Trim(),
        FoodType = foodTypeValue,
        PlaceType = placeType,
        OpeningHours = request.OpeningHours.Trim(),
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        District = request.District.Trim(),
        PhotoUrls = photoUrls,
        PhotoStorageKeys = photoStorageKeys,
        IsCenter = isCenter,
        ParentCenter = parentCenter,
        SubmittedBy = subject,
        Open = true
    };

    await repo.SaveAsync(submission);

    return Results.Ok(new CreateSpotSubmissionResponse { Id = submission.Id, Status = submission.Status });
});

// GET /moderation/submissions
app.MapGet("/moderation/submissions",
    async ([FromServices] SpotSubmissionRepository repo) =>
{
    var submissions = await repo.GetAllAsync();
    return Results.Ok(submissions);
});

// POST /moderation/submissions/{id}/approve
app.MapPost("/moderation/submissions/{id}/approve",
    async (string id, [FromServices] SpotSubmissionRepository repo) =>
{
    if (!await repo.ExistsAsync(id)) return Results.NotFound();
    var submission = await repo.GetByIdAsync(id);
    await repo.ApproveAsync(submission);
    return Results.Ok();
});

// POST /moderation/submissions/{id}/reject
app.MapPost("/moderation/submissions/{id}/reject",
    async (string id, [FromServices] SpotSubmissionRepository repo) =>
{
    if (!await repo.ExistsAsync(id)) return Results.NotFound();
    await repo.RejectAsync(id);
    return Results.Ok();
});

app.Run();

public partial class Program { }
