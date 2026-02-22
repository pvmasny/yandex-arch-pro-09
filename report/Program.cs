using Microsoft.AspNetCore.Mvc;
using report.Dal;
using report.Dal.Models;
using report.Infrastructure;
using report.Middlewares;
using report.Models;
using report.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "BionicPro Reports API",
        Description = "API для получения отчетов по протезам из ClickHouse",
        Version = "v1"
    });
});

// JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",  // Фронтенд на localhost
                "http://frontend:3000"     // Фронтенд в Docker сети
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()  // Важно для cookies
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); // Кэшируем preflight запросы
    });
});

// Register services
builder.Services.AddSingleton<IClickHouseRepository, ClickHouseRepository>();
builder.Services.AddScoped<IMinioService, MinioService>();
builder.Services.AddHealthChecks();

builder.Services.AddHttpClient<AuthServiceClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8000");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();

// Health check
app.MapHealthChecks("/health");



app.MapGet("/api/reports/user", async (
    HttpContext context,
   [AsParameters] ReportRequest request,
   IClickHouseRepository clickHouse,
   ILogger<Program> logger,
   CancellationToken cancellationToken) =>
{
    string userId = null;
    try
    {
        userId = context.Items["CrmId"]?.ToString();
        logger.LogInformation("Fetching reports for user {UserId}", userId);

        var reports = await clickHouse.GetUserReportsAsync(
            userId,
            request.StartDate,
            request.EndDate,
            request.ProsthesisType,
            request.MuscleGroup,
            cancellationToken);

        if (!reports.Any())
        {
            return Results.NotFound(new
            {
                Message = $"No reports found for user {userId}",
                UserId = userId
            });
        }

        // Handle different output formats
        var format = request.Format?.ToLower() ?? "json";

        return format switch
        {
            "csv" => Results.Text(ToCsv(reports), "text/csv"),
            "summary" => Results.Ok(new
            {
                UserId = userId,
                ReportCount = reports.Count,
                Summary = await clickHouse.GetUserSummaryAsync(userId, cancellationToken),
                RecentReports = reports.Take(5)
            }),
            _ => Results.Ok(reports)
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing request for user {UserId}", userId);
        return Results.Problem(
            title: "Internal Server Error",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("GetUserReports");

app.MapGet("/api/reports/user2", async (
    HttpContext context,
   [AsParameters] ReportRequest request,
   [FromServices] IClickHouseRepository clickHouse,
   [FromServices] IMinioService minioService,
   [FromServices] ILogger<Program> logger,
   CancellationToken cancellationToken) =>
{
    string userId = null;
    try
    {
        //userId = context.Items["CrmId"]?.ToString();
        userId = "1";
        logger.LogInformation("Fetching reports for user {UserId}", userId);

        var reportKey = ReportKeyGenerator.GenerateKey(userId, request, request.Format ?? "json");
        var isExists = await minioService.IsExistsAsync(userId, reportKey);

        if (isExists)
        {
            var cdnUrl = await minioService.GeneratePresignedUrlAsync(userId, reportKey);
            logger.LogInformation("Report found in cache: {CdnUrl}", cdnUrl);

            return Results.Ok(new
            {
                cached = true,
                url = cdnUrl,
                expiresIn = "1h",
                message = "Report retrieved from cache"
            });
        }

        var reports = await clickHouse.GetUserReportsAsync(
            userId,
            request.StartDate,
            request.EndDate,
            request.ProsthesisType,
            request.MuscleGroup,
            cancellationToken);

        if (!reports.Any())
        {
            return Results.NotFound(new
            {
                Message = $"No reports found for user {userId}",
                UserId = userId
            });
        }

        await minioService.SaveReportAsync(userId, reportKey, reports, request.Format ?? "json");

        var url = await minioService.GeneratePresignedUrlAsync(userId, reportKey);

        return Results.Ok(new
        {
            cached = false,
            url = url,
            message = "Report generated and cached"
        });

    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing request for user {UserId}", userId);
        return Results.Problem(
            title: "Internal Server Error",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("GetUserReports2");

app.MapGet("/api/reports/user/{userId}/summary", async (
    string userId,
    IClickHouseRepository clickHouse,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        logger.LogInformation("Generating summary for user {UserId}", userId);

        var summary = await clickHouse.GetUserSummaryAsync(userId, cancellationToken);

        if (summary.Count == 0)
        {
            return Results.NotFound(new
            {
                Message = $"No data found for user {userId}",
                UserId = userId
            });
        }

        return Results.Ok(new
        {
            UserId = userId,
            GeneratedAt = DateTime.UtcNow,
            Data = summary
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error generating summary for user {UserId}", userId);
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/reports/table-status", async (
    IClickHouseRepository clickHouse,
    CancellationToken cancellationToken) =>
{
    var exists = await clickHouse.TableExistsAsync(cancellationToken);
    return Results.Ok(new { TableExists = exists, TableName = ClickHouseRepository.TableName });
})
.WithName("CheckTableStatus");

// Helper method to convert reports to CSV
static string ToCsv(List<ProsthesisReport> reports)
{
    var csv = new StringBuilder();
    csv.AppendLine("UserId,Date,CrmName,CrmAge,CrmGender,ProsthesisType,MuscleGroup,SignalCount,AvgFrequency,AvgDuration,AvgAmplitude,TotalDuration");

    foreach (var report in reports)
    {
        csv.AppendLine($"{report.UserId},{report.Date:yyyy-MM-dd},{report.CrmName},{report.CrmAge},{report.CrmGender},{report.ProsthesisType},{report.MuscleGroup},{report.SignalsCount},{report.SignalFrequencyAvg:F2},{report.SignalDurationAvg:F2},{report.SignalAmplitudeAvg:F2},{report.TotalDurationAsInt}");
    }

    return csv.ToString();
}

// Ensure table exists on startup
using (var scope = app.Services.CreateScope())
{
    var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseRepository>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Checking ClickHouse table on startup...");
        await clickHouse.EnsureTableExistsAsync();
        logger.LogInformation("ClickHouse table check completed");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ensure ClickHouse table exists");
    }
}

app.UseMiddleware<TokenAuthMiddleware>();
//app.UseMiddleware<UserAccessMiddleware>();

app.Run();