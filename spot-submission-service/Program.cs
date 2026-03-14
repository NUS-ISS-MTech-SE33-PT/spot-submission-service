using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var allowedClientIds = new HashSet<string>(StringComparer.Ordinal)
{
    "47d5aql1gg87e093dfoqv8tbqs",
    "oirif86fvv6eddccs4d37ccgb"
};

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<SpotSubmissionRepository>();
builder.Services.Configure<SpotSubmissionStorageOptions>(builder.Configuration.GetSection("SpotSubmissionStorage"));
builder.Services.AddScoped<PhotoUploadService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        string awsUrl = "https://cognito-idp.ap-southeast-1.amazonaws.com/ap-southeast-1_5KbPo5kdU";
        options.Authority = awsUrl;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidIssuer = awsUrl,
            ValidateLifetime = true
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal == null)
                {
                    context.Fail("No principal found in token.");
                }
                else if (!context.Principal.Claims.Any(c =>
                    c.Type == "client_id" &&
                    allowedClientIds.Contains(c.Value)))
                {
                    context.Fail("Invalid client_id");
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy =>
    {
        policy.RequireClaim("cognito:groups", "admin");
    });
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste your JWT token here"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });
};

var app = builder.Build();

IResult ApiError(HttpContext context, int statusCode, string code, string message)
{
    return Results.Json(new
    {
        code,
        message,
        traceId = context.TraceIdentifier
    }, statusCode: statusCode);
}

static string SanitizeHeaders(IHeaderDictionary headers)
{
    var maskedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "x-user-sub"
    };

    return string.Join("; ", headers.Select(header =>
    {
        var value = maskedHeaders.Contains(header.Key) ? "***" : header.Value.ToString();
        return $"{header.Key}: {value}";
    }));
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(exception, "Unhandled exception while processing request.");

        var message = app.Environment.IsDevelopment()
            ? (exception?.Message ?? "Unhandled server error.")
            : "An unexpected error occurred.";

        var result = ApiError(context, StatusCodes.Status500InternalServerError, "internal_error", message);
        await result.ExecuteAsync(context);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["Strict-Transport-Security"] =
            "max-age=31536000; includeSubDomains";
        await next.Invoke();
    });
}

var logger = app.Logger;
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var utcNow = DateTime.UtcNow.ToString("o");
    var method = context.Request.Method;
    var path = context.Request.Path;
    var headers = SanitizeHeaders(context.Request.Headers);

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
            return ApiError(httpContext, StatusCodes.Status400BadRequest, "validation_error", "fileName and contentType are required.");
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
    }).RequireAuthorization();

// POST /spots/submissions
app.MapPost("/spots/submissions",
    async (HttpContext httpContext, CreateSpotSubmissionRequest request, [FromServices] SpotSubmissionRepository repo, [FromServices] PhotoUploadService uploadService) =>
{
    var subject = JwtSubjectResolver.ResolveUserId(httpContext);
    if (string.IsNullOrWhiteSpace(subject))
    {
        return ApiError(httpContext, StatusCodes.Status401Unauthorized, "unauthorized", "Login required before submitting a new spot.");
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
            if (!uploadService.IsOwnedByUser(photoStorageKeys[i], photoUrls[i], subject))
            {
                return Results.BadRequest(new { message = "photoUrls/photoStorageKeys must belong to the authenticated user." });
            }
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
}).RequireAuthorization();

// GET /moderation/submissions
app.MapGet("/moderation/submissions",
    async ([FromServices] SpotSubmissionRepository repo) =>
{
    var submissions = await repo.GetAllAsync();
    return Results.Ok(submissions);
}).RequireAuthorization("AdminOnly");

// POST /moderation/submissions/{id}/approve
app.MapPost("/moderation/submissions/{id}/approve",
    async (string id, HttpContext ctx, [FromServices] SpotSubmissionRepository repo) =>
{
    if (!await repo.ExistsAsync(id))
    {
        return ApiError(ctx, StatusCodes.Status404NotFound, "not_found", "Submission not found.");
    }
    var submission = await repo.GetByIdAsync(id);
    await repo.ApproveAsync(submission);
    return Results.Ok();
}).RequireAuthorization("AdminOnly");

// POST /moderation/submissions/{id}/reject
app.MapPost("/moderation/submissions/{id}/reject",
    async (string id, HttpContext ctx, [FromServices] SpotSubmissionRepository repo) =>
{
    if (!await repo.ExistsAsync(id))
    {
        return ApiError(ctx, StatusCodes.Status404NotFound, "not_found", "Submission not found.");
    }
    await repo.RejectAsync(id);
    return Results.Ok();
}).RequireAuthorization("AdminOnly");

app.Run();

public partial class Program { }
