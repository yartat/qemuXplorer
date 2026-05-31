using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Core.Services;

public sealed class QemuDiscoveryService
{
    private readonly ILogger<QemuDiscoveryService> _logger;

    public QemuDiscoveryService(ILogger<QemuDiscoveryService> logger) => _logger = logger;

    public async Task<IReadOnlyList<QemuInstallation>> DiscoverAsync(CancellationToken ct = default)
    {
        var searchDirs = GetSearchDirectories();
        var results = new List<QemuInstallation>();

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var binary = Path.Combine(dir, OperatingSystem.IsWindows()
                ? "qemu-system-x86_64.exe"
                : "qemu-system-x86_64");

            if (!File.Exists(binary)) continue;

            var version = await GetVersionAsync(binary, ct);
            if (version is null) continue;

            results.Add(new QemuInstallation
            {
                Name = $"QEMU {version} ({dir})",
                BinaryDirectory = dir,
                DetectedVersion = version,
                Platform = GetPlatformName()
            });
        }

        return results.AsReadOnly();
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "qemu");
            yield return @"C:\qemu";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/opt/local/bin";
        }
        else
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
            yield return "/usr/local/qemu/bin";
        }
    }

    private async Task<string?> GetVersionAsync(string binary, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(binary, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Format: "QEMU emulator version X.Y.Z ..."
            var match = System.Text.RegularExpressions.Regex.Match(output, @"version (\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : output.Split('\n')[0].Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get version from {Binary}", binary);
            return null;
        }
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return "Linux";
    }
}
