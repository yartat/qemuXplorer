using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QEmuXlorer.Models.Dtos;
using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Core.Services;

public sealed class DiskImageService
{
    private readonly ILogger<DiskImageService> _logger;

    public DiskImageService(ILogger<DiskImageService> logger) => _logger = logger;

    public async Task CreateImageAsync(
        string qemuImgPath,
        string outputPath,
        DiskFormat format,
        long sizeMB,
        CancellationToken ct = default)
    {
        var formatStr = format switch
        {
            DiskFormat.QCow2 => "qcow2",
            DiskFormat.Raw   => "raw",
            DiskFormat.Vmdk  => "vmdk",
            DiskFormat.Vdi   => "vdi",
            _                => "qcow2"
        };

        var psi = new ProcessStartInfo(qemuImgPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("create");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(formatStr);
        psi.ArgumentList.Add(outputPath);
        psi.ArgumentList.Add($"{sizeMB}M");

        using var proc = Process.Start(psi)!;
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"qemu-img create failed: {stderr}");
    }

    public async Task<DiskImageInfo?> GetImageInfoAsync(
        string qemuImgPath,
        string imagePath,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(qemuImgPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("info");
        psi.ArgumentList.Add("--output=json");
        psi.ArgumentList.Add(imagePath);

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var formatStr = root.GetProperty("format").GetString() ?? "qcow2";
            var virtualSize = root.GetProperty("virtual-size").GetInt64();
            var actualSize = root.TryGetProperty("actual-size", out var actual) ? actual.GetInt64() : virtualSize;

            var fmt = formatStr switch
            {
                "qcow2" => DiskFormat.QCow2,
                "raw"   => DiskFormat.Raw,
                "vmdk"  => DiskFormat.Vmdk,
                "vhd"   => DiskFormat.Vhd,
                "vhdx"  => DiskFormat.Vhdx,
                "vdi"   => DiskFormat.Vdi,
                _       => DiskFormat.QCow2
            };

            return new DiskImageInfo(imagePath, fmt, virtualSize, actualSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse qemu-img info output");
            return null;
        }
    }
}
