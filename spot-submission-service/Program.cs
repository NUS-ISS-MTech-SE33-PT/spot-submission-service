using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        var subject = httpContext.Request.Headers["x-user-sub"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Results.Json(
                new { message = "Unauthorized — login required before uploading a submission photo" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentType))
        {
            return Results.BadRequest(new { message = "fileName and contentType are required." });
        }

        var descriptor = uploadService.CreateUploadUrl(request.FileName, request.ContentType, subject);
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
    async (CreateSpotSubmissionRequest request, [FromServices] SpotSubmissionRepository repo) =>
{
    var submission = new SpotSubmission
    {
        Name = request.Name,
        Address = request.Address,
        PhotoUrl = request.PhotoUrl,
        PhotoStorageKey = request.PhotoStorageKey
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
    await repo.ApproveAsync(id);
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
