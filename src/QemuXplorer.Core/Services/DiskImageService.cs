using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QemuXplorer.Models;
using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

/// <summary>What <c>qemu-img create</c> should make.</summary>
/// <param name="Options">Format-specific <c>-o</c> options, already filtered to non-defaults.</param>
/// <param name="KeySecretFile">LUKS passphrase file; required when the format is luks.</param>
public sealed record DiskCreateRequest(
    string Path,
    DiskFormat Format,
    long SizeMB,
    IReadOnlyDictionary<string, string>? Options = null,
    string? KeySecretFile = null);

/// <summary>One <c>qemu-img</c> invocation that changes or inspects an existing image.</summary>
/// <param name="Title">What it does, for the confirmation list.</param>
/// <param name="OutputPath">A file the command creates (convert); removed again if the command fails.</param>
/// <param name="Warning">Shown with the confirmation when the operation can lose data.</param>
public sealed record DiskImageOperation(
    string Title,
    IReadOnlyList<string> Arguments,
    string? Warning = null,
    string? OutputPath = null);

public sealed class DiskImageService
{
    private const string LuksSecretId = "sec0";

    private readonly ILogger<DiskImageService> _logger;

    public DiskImageService(ILogger<DiskImageService> logger) => _logger = logger;

    /// <summary>qemu-img lives next to the qemu-system binaries of the same installation.</summary>
    public static string ResolveQemuImg(string binaryDirectory)
        => Path.Combine(binaryDirectory, OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img");

    /// <summary>
    /// The argument list for <c>qemu-img create</c>. Pure, so the wizard can preview the exact
    /// command and tests can pin it down without spawning a process.
    /// </summary>
    public static IReadOnlyList<string> BuildCreateArguments(DiskCreateRequest request)
    {
        var args = new List<string> { "create" };
        var options = new List<string>();

        if (request.Format == DiskFormat.Luks)
        {
            if (string.IsNullOrWhiteSpace(request.KeySecretFile))
                throw new ArgumentException("A LUKS image needs a passphrase file.", nameof(request));

            args.Add("--object");
            args.Add($"secret,id={LuksSecretId},file={Escape(request.KeySecretFile)}");
            options.Add($"key-secret={LuksSecretId}");
        }

        args.Add("-f");
        args.Add(DiskFormatCatalog.QemuName(request.Format));

        foreach (var (key, value) in request.Options ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
                options.Add($"{key}={Escape(value)}");
        }

        if (options.Count > 0)
        {
            args.Add("-o");
            args.Add(string.Join(',', options));
        }

        args.Add(request.Path);
        args.Add($"{request.SizeMB}M");
        return args;
    }

    // qemu-img option strings use a doubled comma as the escape for a literal comma.
    private static string Escape(string value) => value.Replace(",", ",,");

    /// <summary>
    /// <c>qemu-img resize</c> to an absolute size. Shrinking needs <c>--shrink</c>, which qemu-img
    /// insists on because it discards everything past the new end.
    /// </summary>
    public static IReadOnlyList<string> BuildResizeArguments(
        string path, DiskFormat format, long sizeMB, bool shrink, string? preallocation = null)
    {
        var args = new List<string> { "resize", "-f", DiskFormatCatalog.QemuName(format) };
        if (shrink) args.Add("--shrink");
        if (!string.IsNullOrWhiteSpace(preallocation)) args.Add($"--preallocation={preallocation}");
        args.Add(path);
        args.Add($"{sizeMB}M");
        return args;
    }

    /// <summary>Copies an image into a new file of another format (or the same one, to compact it).</summary>
    public static IReadOnlyList<string> BuildConvertArguments(
        string source, DiskFormat sourceFormat, string destination, DiskFormat destinationFormat,
        IReadOnlyDictionary<string, string>? options = null)
    {
        var args = new List<string>
        {
            "convert", "-f", DiskFormatCatalog.QemuName(sourceFormat), "-O", DiskFormatCatalog.QemuName(destinationFormat)
        };
        AddOptions(args, options);
        args.Add(source);
        args.Add(destination);
        return args;
    }

    /// <summary>
    /// Points an image at another backing file, or with a null <paramref name="backingFile"/> copies
    /// the backing data in so the image stands alone. Unless <paramref name="unsafeMode"/> is set,
    /// qemu-img also rewrites clusters so the contents the guest sees stay the same.
    /// </summary>
    public static IReadOnlyList<string> BuildRebaseArguments(
        string path, DiskFormat format, string? backingFile, DiskFormat? backingFormat, bool unsafeMode)
    {
        var args = new List<string> { "rebase", "-f", DiskFormatCatalog.QemuName(format) };
        if (unsafeMode && backingFile is not null) args.Add("-u");
        args.Add("-b");
        args.Add(backingFile ?? string.Empty);
        if (backingFile is not null && backingFormat is { } bf)
        {
            args.Add("-F");
            args.Add(DiskFormatCatalog.QemuName(bf));
        }
        args.Add(path);
        return args;
    }

    /// <summary>Changes format settings in place (<c>qemu-img amend</c>).</summary>
    public static IReadOnlyList<string> BuildAmendArguments(
        string path, DiskFormat format, IReadOnlyDictionary<string, string> options)
    {
        var args = new List<string> { "amend", "-f", DiskFormatCatalog.QemuName(format) };
        AddOptions(args, options);
        args.Add(path);
        return args;
    }

    /// <summary>Checks consistency; <paramref name="repair"/> is <c>leaks</c> or <c>all</c> to also fix.</summary>
    public static IReadOnlyList<string> BuildCheckArguments(string path, DiskFormat format, string? repair = null)
    {
        var args = new List<string> { "check", "-f", DiskFormatCatalog.QemuName(format) };
        if (!string.IsNullOrWhiteSpace(repair))
        {
            args.Add("-r");
            args.Add(repair);
        }
        args.Add(path);
        return args;
    }

    private static void AddOptions(List<string> args, IReadOnlyDictionary<string, string>? options)
    {
        var parts = (options ?? new Dictionary<string, string>())
            .Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .Select(o => $"{o.Key}={Escape(o.Value)}")
            .ToList();
        if (parts.Count == 0) return;
        args.Add("-o");
        args.Add(string.Join(',', parts));
    }

    /// <summary>
    /// Runs one operation and returns what qemu-img printed. A failure throws with qemu-img's own
    /// message; a file the command was creating is deleted, since it did not exist before.
    /// </summary>
    public async Task<string> RunAsync(string qemuImgPath, DiskImageOperation operation, CancellationToken ct = default)
    {
        var psi = NewProcess(qemuImgPath);
        foreach (var arg in operation.Arguments)
            psi.ArgumentList.Add(arg);

        var succeeded = false;
        try
        {
            using var proc = StartProcess(psi);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stderr = (await stderrTask).Trim();
            var stdout = (await stdoutTask).Trim();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"qemu-img {operation.Arguments[0]} failed: {(stderr.Length > 0 ? stderr : stdout)}");

            succeeded = true;
            return stdout.Length > 0 ? stdout : stderr;
        }
        finally
        {
            if (!succeeded && operation.OutputPath is { } partial)
                DeletePartialImage(partial);
        }
    }

    public async Task CreateImageAsync(
        string qemuImgPath,
        DiskCreateRequest request,
        CancellationToken ct = default)
    {
        // qemu-img create silently overwrites, which would destroy someone's existing disk.
        if (File.Exists(request.Path))
            throw new InvalidOperationException($"'{request.Path}' already exists; refusing to overwrite it.");

        var psi = NewProcess(qemuImgPath);
        foreach (var arg in BuildCreateArguments(request))
            psi.ArgumentList.Add(arg);

        var created = false;
        try
        {
            using var proc = StartProcess(psi);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"qemu-img create failed: {(stderr.Length > 0 ? stderr : stdout).Trim()}");

            created = true;
        }
        finally
        {
            // qemu-img can leave a partial file behind when it fails after creating it, and the next
            // attempt would then be refused as "already exists". The file did not exist before this call.
            if (!created)
                DeletePartialImage(request.Path);
        }
    }

    private void DeletePartialImage(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not remove the partial image {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not remove the partial image {Path}", path);
        }
    }

    /// <summary>
    /// Block drivers this QEMU build can open (qcow2, nbd, ssh, iscsi...). Used to warn up front
    /// when a source type needs a driver the build lacks, instead of failing at VM start.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetSupportedDriversAsync(
        string qemuImgPath,
        CancellationToken ct = default)
    {
        var psi = NewProcess(qemuImgPath);
        psi.ArgumentList.Add("--help");

        using var proc = StartProcess(psi);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return ParseSupportedDrivers(await stdoutTask + "\n" + await stderrTask);
    }

    public static IReadOnlySet<string> ParseSupportedDrivers(string helpText)
    {
        var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;

        foreach (var raw in helpText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Supported image formats", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Supported formats", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (!inSection)
                continue;

            // The list ends at the first prose line after it ("See <url> for how to report...").
            if (line.Length > 0 && !line.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'))
                break;
            if (line.StartsWith("See ", StringComparison.Ordinal))
                break;

            foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                drivers.Add(token);
        }

        return drivers;
    }

    /// <param name="format">
    /// The format to open the image as. Pass it whenever it is known: probing misreports a fixed VHD as raw.
    /// </param>
    public async Task<DiskImageInfo?> GetImageInfoAsync(
        string qemuImgPath,
        string imagePath,
        CancellationToken ct = default,
        DiskFormat? format = null)
    {
        var psi = NewProcess(qemuImgPath);
        psi.ArgumentList.Add("info");
        if (format is { } known)
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(DiskFormatCatalog.QemuName(known));
        }
        psi.ArgumentList.Add("--output=json");
        psi.ArgumentList.Add(imagePath);

        using var proc = StartProcess(psi);
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var formatStr = root.GetProperty("format").GetString();
            var virtualSize = root.GetProperty("virtual-size").GetInt64();
            var actualSize = root.TryGetProperty("actual-size", out var actual) ? actual.GetInt64() : virtualSize;

            var fmt = DiskFormatCatalog.FromQemuName(formatStr) ?? DiskFormat.QCow2;
            var backing = root.TryGetProperty("full-backing-filename", out var full) ? full.GetString()
                : root.TryGetProperty("backing-filename", out var rel) ? rel.GetString()
                : null;
            return new DiskImageInfo(imagePath, fmt, virtualSize, actualSize, backing);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse qemu-img info output");
            return null;
        }
    }

    private static ProcessStartInfo NewProcess(string qemuImgPath) => new(qemuImgPath)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    private static Process StartProcess(ProcessStartInfo psi)
    {
        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException("qemu-img did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not run qemu-img at '{psi.FileName}': {ex.Message}", ex);
        }
    }
}
