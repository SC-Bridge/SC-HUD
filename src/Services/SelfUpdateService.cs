namespace schud.Services;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;


internal static class SelfUpdateService
{
    private static readonly HttpClient _http = new();

    public static async Task ApplyAsync(string downloadUrl, Action quit, ILogger? log = null)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "schud.exe");

        log?.LogInformation("Self-update: current exe = {Exe}", currentExe);

        var tempDir = Path.Combine(Path.GetTempPath(), "schud_update");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, "schud.exe");

        log?.LogInformation("Downloading update from {Url}", downloadUrl);
        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(tempExe);
        await src.CopyToAsync(dest);
        log?.LogInformation("Download complete — saved to {Temp}", tempExe);

        var script = $"""
            Start-Sleep -Seconds 2
            Copy-Item -Force '{tempExe}' '{currentExe}'
            Start-Process '{currentExe}'
            """;

        var scriptPath = Path.Combine(tempDir, "update.ps1");
        await File.WriteAllTextAsync(scriptPath, script);
        log?.LogInformation("Update script written to {Script} — launching and quitting", scriptPath);

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        quit();
    }
}
