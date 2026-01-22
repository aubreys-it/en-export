
using Microsoft.Playwright;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "EN Export Service Running");

// Triggered by Dapr Cron (POST /scheduled)
app.MapPost("/scheduled", async (ILoggerFactory lf, CancellationToken ct) =>
{
    var logger = lf.CreateLogger("EN-Export");
    try
    {
        var uri = await ReportExporter.RunAsync(logger, ct);
        return Results.Ok(new { status = "ok", blob = uri, at = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "EN export failed");
        return Results.Problem(ex.Message);
    }
});

// Optional manual trigger (useful for ADF or testing)
app.MapPost("/run-now", async (ILoggerFactory lf, CancellationToken ct) =>
{
    var logger = lf.CreateLogger("EN-Export");
    try
    {
        var uri = await ReportExporter.RunAsync(logger, ct);
        return Results.Ok(new { status = "ok", blob = uri, at = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "EN export failed");
        return Results.Problem(ex.Message);
    }
});

await app.RunAsync();
