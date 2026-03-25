namespace schud.Services;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;


internal static class SelfUpdateService
{
    private static readonly HttpClient _http = new();

    public static async Task ApplyAsync(UpdateInfo info, Action quit, ILogger? log = null)
    {
        if (IsMsiInstall() && !string.IsNullOrEmpty(info.MsiUrl))
        {
            log?.LogInformation("MSI install detected — updating via msiexec");
            await ApplyMsiAsync(info.MsiUrl, quit, log);
        }
        else
        {
            log?.LogInformation("Portable install detected — updating via exe-swap");
            await ApplyPortableAsync(info.DownloadUrl, quit, log);
        }
    }

    private static bool IsMsiInstall()
    {
        var exe  = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return exe.StartsWith(pf,   StringComparison.OrdinalIgnoreCase)
            || exe.StartsWith(pf86, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ApplyMsiAsync(string msiUrl, Action quit, ILogger? log)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "schud_update");
        Directory.CreateDirectory(tempDir);
        var tempMsi = Path.Combine(tempDir, "schud_update.msi");

        log?.LogInformation("Downloading MSI from {Url}", msiUrl);
        using var response = await _http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(tempMsi);
        await src.CopyToAsync(dest);
        log?.LogInformation("MSI download complete — saved to {Msi}", tempMsi);

        // /passive shows a progress bar; Windows handles UAC elevation for the perMachine install
        Process.Start(new ProcessStartInfo
        {
            FileName        = "msiexec.exe",
            Arguments       = $"/i \"{tempMsi}\" /passive /norestart",
            UseShellExecute = true,
        });

        quit();
    }

    private static async Task ApplyPortableAsync(string exeUrl, Action quit, ILogger? log)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "schud.exe");

        log?.LogInformation("Self-update: current exe = {Exe}", currentExe);

        var tempDir = Path.Combine(Path.GetTempPath(), "schud_update");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, "schud.exe");

        log?.LogInformation("Downloading update from {Url}", exeUrl);
        using var response = await _http.GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead);
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
