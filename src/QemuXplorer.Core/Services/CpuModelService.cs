using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

/// <summary>
/// Lists the CPU models a QEMU build offers for an architecture by running
/// <c>qemu-system-&lt;arch&gt; -cpu help</c>.
/// </summary>
/// <remarks>
/// The list depends on the architecture and the build, not on the machine type: the output is
/// byte-identical for aarch64 whether <c>-machine virt</c>, <c>raspi3b</c> or <c>sbsa-ref</c> is
/// given. Whether a given machine accepts a given CPU is only decided when QEMU starts.
///
/// Results are cached per binary for the life of the process, so reopening the editor does not
/// spawn QEMU again. Failures are never cached.
/// </remarks>
public class CpuModelService
{
    private static readonly Regex Entry = new(@"^(?<name>\S+)(?:\s{2,}(?<desc>.*))?$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<CpuModelInfo>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Always offered and always first. It is not in <c>-cpu help</c> for most architectures,
    /// yet it is the right default: it means "the host's own CPU" under KVM, HVF or WHPX.
    /// </summary>
    public static CpuModelInfo Host { get; } =
        new("host", "Same CPU as this computer. Needs KVM, HVF or WHPX");

    public async Task<IReadOnlyList<CpuModelInfo>> GetModelsAsync(
        string binaryDirectory,
        GuestArchitecture architecture,
        CancellationToken ct = default)
    {
        var exe = Path.Combine(binaryDirectory, QemuArgumentBuilder.GetBinaryName(architecture));

        // The shared load must not be tied to one caller's token, or cancelling that caller
        // would fail everyone waiting on the same probe.
        var load = _cache.GetOrAdd(exe, path => LoadAsync(path));
        try
        {
            return await load.WaitAsync(ct);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _cache.TryRemove(exe, out _);
            throw;
        }
    }

    private async Task<IReadOnlyList<CpuModelInfo>> LoadAsync(string exe)
        => WithHost(Parse(await ReadCpuHelpAsync(exe, CancellationToken.None)));

    /// <summary>Runs <c>-cpu help</c>. Virtual so tests can supply output without a QEMU install.</summary>
    protected virtual async Task<string> ReadCpuHelpAsync(string exe, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-cpu");
        psi.ArgumentList.Add("help");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("QEMU did not start.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not run '{exe}': {ex.Message}", ex);
        }

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                // Some builds print the list to stderr, so read both.
                return await stdout + "\n" + await stderr;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new TimeoutException($"'{exe} -cpu help' did not finish in time.");
            }
        }
    }

    /// <summary>
    /// Reads the "Available CPUs:" block: one model per indented line, an optional description
    /// after two or more spaces, ending at the first blank or unindented line. (s390x continues
    /// with feature-flag sections that must not be mistaken for models.)
    /// </summary>
    public static IReadOnlyList<CpuModelInfo> Parse(string helpText)
    {
        var models = new List<CpuModelInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inList = false;

        foreach (var raw in helpText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (!inList)
            {
                inList = line.StartsWith("Available CPUs", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Length == 0 || !char.IsWhiteSpace(line[0]))
                break;

            var text = line.Trim();
            // QEMU before 5.0 printed x86 entries as "x86 <name>".
            if (text.StartsWith("x86 ", StringComparison.Ordinal))
                text = text[4..].TrimStart();

            var match = Entry.Match(text);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            if (seen.Add(name))
                models.Add(new CpuModelInfo(name, match.Groups["desc"].Value.Trim()));
        }

        return models;
    }

    private static IReadOnlyList<CpuModelInfo> WithHost(IReadOnlyList<CpuModelInfo> models)
        => [Host, .. models.Where(m => m.Name != Host.Name)];
}
