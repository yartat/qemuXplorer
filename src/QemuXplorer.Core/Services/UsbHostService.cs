using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QemuXplorer.Models.Dtos;

namespace QemuXplorer.Core.Services;

/// <summary>What the host's USB listing found, and why it may be incomplete.</summary>
public sealed record UsbHostListing(IReadOnlyList<HostUsbDevice> Devices, string Note = "");

/// <summary>
/// Lists the USB devices plugged into this computer, so passthrough can be picked from a list instead
/// of typed as hexadecimal ids. Each platform has its own source: sysfs on Linux, the PnP registry
/// through PowerShell on Windows, <c>system_profiler</c> on macOS. A failure to list is a note, never an
/// exception: the user can still type the ids.
/// </summary>
public sealed class UsbHostService
{
    private readonly ILogger<UsbHostService> _logger;

    public UsbHostService(ILogger<UsbHostService> logger) => _logger = logger;

    public async Task<UsbHostListing> ListAsync(CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return new UsbHostListing(ParseLinux("/sys/bus/usb/devices"));

            if (OperatingSystem.IsWindows())
            {
                var json = await RunAsync("powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-Command", WindowsScript], ct);
                return new UsbHostListing(ParseWindows(json));
            }

            if (OperatingSystem.IsMacOS())
            {
                var json = await RunAsync("system_profiler", ["SPUSBDataType", "-json"], ct);
                return new UsbHostListing(ParseMac(json),
                    "macOS lists devices but not their bus and address; passthrough is by vendor and product.");
            }

            return new UsbHostListing([], "Listing USB devices is not supported on this system.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not list USB devices");
            return new UsbHostListing([], $"Could not list USB devices: {ex.Message}");
        }
    }

    // Composite devices also appear once per interface ("&MI_00"); the interfaces are not devices to pass through.
    internal const string WindowsScript =
        "$d = @(Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPDeviceID -like 'USB\\VID_*' -and $_.PNPDeviceID -notlike '*&MI_*' } | Select-Object Name,PNPDeviceID,Manufacturer); " +
        "ConvertTo-Json -InputObject $d -Compress";

    /// <summary>Reads <c>/sys/bus/usb/devices</c> (or a copy of its layout).</summary>
    public static IReadOnlyList<HostUsbDevice> ParseLinux(string root)
    {
        var devices = new List<HostUsbDevice>();
        if (!Directory.Exists(root))
            return devices;

        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!IsLinuxDeviceEntry(Path.GetFileName(directory)))
                continue;

            if (!TryReadHex(Path.Combine(directory, "idVendor"), out var vendor)
                || !TryReadHex(Path.Combine(directory, "idProduct"), out var product))
                continue;

            var manufacturer = ReadText(Path.Combine(directory, "manufacturer"));
            var product_ = ReadText(Path.Combine(directory, "product"));
            var title = string.Join(' ', new[] { manufacturer, product_ }.Where(t => t.Length > 0));

            _ = int.TryParse(ReadText(Path.Combine(directory, "busnum")), out var bus);
            _ = int.TryParse(ReadText(Path.Combine(directory, "devnum")), out var address);

            devices.Add(new HostUsbDevice(vendor, product, title.Length > 0 ? title : "USB device", manufacturer, bus, address));
        }

        return devices;
    }

    /// <summary>"1-2:1.0" is an interface of device "1-2"; "usb1" is a root hub. Neither is something to hand over.</summary>
    public static bool IsLinuxDeviceEntry(string name)
        => !name.Contains(':') && !name.StartsWith("usb", StringComparison.Ordinal);

    /// <summary>Reads the JSON the PowerShell script prints. A single device arrives as an object, not a list.</summary>
    public static IReadOnlyList<HostUsbDevice> ParseWindows(string json)
    {
        var devices = new List<HostUsbDevice>();
        if (string.IsNullOrWhiteSpace(json))
            return devices;

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : [doc.RootElement];

        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("PNPDeviceID", out var idElement))
                continue;

            var match = Regex.Match(idElement.GetString() ?? string.Empty, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
            if (!match.Success)
                continue;

            var name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            var manufacturer = item.TryGetProperty("Manufacturer", out var m) ? m.GetString() ?? string.Empty : string.Empty;
            devices.Add(new HostUsbDevice(
                ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                ushort.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                name.Length > 0 ? name : "USB device",
                manufacturer, 0, 0));
        }

        return devices;
    }

    /// <summary>Reads <c>system_profiler SPUSBDataType -json</c>, whose devices nest under their hubs.</summary>
    public static IReadOnlyList<HostUsbDevice> ParseMac(string json)
    {
        var devices = new List<HostUsbDevice>();
        if (string.IsNullOrWhiteSpace(json))
            return devices;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("SPUSBDataType", out var buses))
            foreach (var bus in buses.EnumerateArray())
                CollectMac(bus, devices);

        return devices;
    }

    private static void CollectMac(JsonElement node, List<HostUsbDevice> devices)
    {
        if (node.TryGetProperty("vendor_id", out var vendorElement) && node.TryGetProperty("product_id", out var productElement))
        {
            var vendor = MacId(vendorElement.GetString(), appleVendor: true);
            var product = MacId(productElement.GetString(), appleVendor: false);
            if (vendor is { } v && product is { } p)
            {
                var name = node.TryGetProperty("_name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var manufacturer = node.TryGetProperty("manufacturer", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                devices.Add(new HostUsbDevice(v, p, name.Length > 0 ? name : "USB device", manufacturer, 0, 0));
            }
        }

        if (node.TryGetProperty("_items", out var children))
            foreach (var child in children.EnumerateArray())
                CollectMac(child, devices);
    }

    // system_profiler writes ids like "0x046d" or "0x05ac  (Apple Inc.)", and "apple_vendor_id" for Apple parts.
    private static ushort? MacId(string? text, bool appleVendor)
    {
        if (appleVendor && string.Equals(text?.Trim(), "apple_vendor_id", StringComparison.OrdinalIgnoreCase))
            return 0x05AC;

        var match = Regex.Match(text ?? string.Empty, @"0x([0-9A-Fa-f]{1,4})");
        return match.Success
            ? ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryReadHex(string path, out ushort value)
    {
        value = 0;
        return ushort.TryParse(ReadText(path), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static async Task<string> RunAsync(string file, string[] arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{file} did not start.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{file} failed: {(await error).Trim()}");
        return await output;
    }
}
