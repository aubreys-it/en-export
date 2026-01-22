
using Azure.Storage.Blobs;
using Microsoft.Playwright;
using System.Globalization;

public static class ReportExporter
{
    public static async Task<string> RunAsync(ILogger logger, CancellationToken ct = default)
    {
        var baseUrl    = Get("EN_BASE_URL");
        var username   = Get("EN_USERNAME");
        var password   = Get("EN_PASSWORD");
        var totpSecret = Get("EN_TOTP_SECRET", required: false);
        var container  = Get("AZURE_BLOB_CONTAINER");
        var connString = Get("AZURE_BLOB_CONNSTRING");
        var hint       = Get("REPORT_PATH_HINT", required: false);

        // Create Playwright and a temp download dir inside the container
        using var pw = await Playwright.CreateAsync();
        var downloadDir = Path.Combine("/tmp", "en-downloads", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(downloadDir);

        // ✅ Set default downloads directory on BROWSER launch (not on context)
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox" },
            DownloadsPath = downloadDir
        });

        // Create context + page
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,     // keep this
            ViewportSize = new() { Width = 1280, Height = 900 }
        });
        var page = await context.NewPageAsync();

        // 1) Login (selectors are placeholders — refine for your EN tenant)
        await page.GotoAsync(baseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.FillAsync("input[name='username']", username);
        await page.FillAsync("input[name='password']", password);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 2) Optional TOTP
        if (!string.IsNullOrEmpty(totpSecret))
        {
            if (await page.Locator("input[name='totp']").IsVisibleAsync())
            {
                var totp = GenerateTotp(totpSecret);
                await page.FillAsync("input[name='totp']", totp);
                await page.ClickAsync("button[type='submit']");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
        }

        // 3) Navigate to report page & trigger export
        await page.ClickAsync("a#reportsMenu");
        await page.ClickAsync("a#dailyBenefitsExport");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (await page.Locator("input[name='startDate']").IsVisibleAsync())
            await page.FillAsync("input[name='startDate']", yesterday);
        if (await page.Locator("input[name='endDate']").IsVisibleAsync())
            await page.FillAsync("input[name='endDate']", yesterday);

        // 4) Wait for the download and save it explicitly
        var downloadTask = page.WaitForDownloadAsync();
        await page.ClickAsync("button#exportCsv");
        var download = await downloadTask;

        var filePath = Path.Combine(downloadDir, download.SuggestedFilename ?? "report.csv");
        await download.SaveAsAsync(filePath); // recommended pattern
        logger.LogInformation("Downloaded file: {filePath}", filePath);

        // 5) Upload to Blob
        var blobClient = new BlobContainerClient(connString, container);
        await blobClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobName = $"EmployeeNavigator/{DateTime.UtcNow:yyyy/MM/dd}/benefits_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        var client = blobClient.GetBlobClient(blobName);

        await using (var fs = File.OpenRead(filePath))
        {
            await client.UploadAsync(fs, overwrite: true, cancellationToken: ct);
        }
        logger.LogInformation("Uploaded to blob: {blobName}", blobName);

        return client.Uri.ToString();
    }

    // --- Helpers ---
    private static string Get(string key, bool required = true)
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (required && string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"Missing env var: {key}");
        return v ?? "";
    }

    // RFC 6238 TOTP if you have a base32 secret (optional MFA)
    private static string GenerateTotp(string base32Secret)
    {
        byte[] secret = Base32ToBytes(base32Secret);
        long timestep = (long)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var data = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(timestep));
        using var hmac = new System.Security.Cryptography.HMACSHA1(secret);
        var hash = hmac.ComputeHash(data);
        int offset = hash[^1] & 0x0F;
        int bin = ((hash[offset] & 0x7F) << 24) |
                  ((hash[offset + 1] & 0xFF) << 16) |
                  ((hash[offset + 2] & 0FF) << 8) |
                   (hash[offset + 3] & 0xFF);
        int code = bin % 1_000_000;
        return code.ToString("D6");
    }

    private static byte[] Base32ToBytes(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = base32.Trim().ToUpperInvariant().Replace(" ", "").Replace("=", "");
        var bits = new List<bool>(cleaned.Length * 5);
        foreach (var c in cleaned)
        {
            int v = alphabet.IndexOf(c);
            if (v < 0) throw new ArgumentException("Invalid base32 char.");
            for (int i = 4; i >= 0; i--) bits.Add(((v >> i) & 1) == 1);
        }
        var bytes = new List<byte>();
        for (int i = 0; i + 7 < bits.Count; i += 8)
        {
            byte b = 0;
            for (int j = 0; j < 8; j++) if (bits[i + j]) b |= (byte)(1 << (7 - j));
            bytes.Add(b);
        }
        return bytes.ToArray();
    }
}
