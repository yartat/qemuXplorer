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
        new("host", "Passes this computer's CPU to the guest. Recommended for a guest of the same architecture unless you need live migration; needs KVM, HVF or WHPX");

    /// <summary>
    /// "No <c>-cpu</c> argument": QEMU uses the CPU the selected machine type is built around.
    /// It is the right default when <c>host</c> cannot apply, and a forced CPU can clash with a
    /// board that expects a particular one. The empty name is what is stored and what the
    /// argument builder treats as "omit -cpu".
    /// </summary>
    public static CpuModelInfo MachineDefault { get; } =
        new(string.Empty, "Let QEMU choose the CPU of the selected machine type (no -cpu argument)");

    /// <summary>
    /// The model a machine of this architecture starts with: <c>host</c> when this computer has
    /// the same architecture as the guest, otherwise the machine's own default.
    /// </summary>
    public static string DefaultModelName(GuestArchitecture architecture)
        => DefaultModelName(architecture, HostPlatform.Current);

    public static string DefaultModelName(GuestArchitecture architecture, System.Runtime.InteropServices.Architecture host)
        => HostPlatform.CanUseHostCpu(architecture, host) ? Host.Name : MachineDefault.Name;

    public async Task<IReadOnlyList<CpuModelInfo>> GetModelsAsync(
        string binaryDirectory,
        GuestArchitecture architecture,
        CancellationToken ct = default)
    {
        var exe = Path.Combine(binaryDirectory, QemuArgumentBuilder.GetBinaryName(architecture));

        // The shared load must not be tied to one caller's token, or cancelling that caller
        // would fail everyone waiting on the same probe.
        var load = _cache.GetOrAdd(exe, path => LoadAsync(path, architecture));
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

    private async Task<IReadOnlyList<CpuModelInfo>> LoadAsync(string exe, GuestArchitecture architecture)
        => WithDefaults(CpuModelDescriptions.Enrich(Parse(await ReadCpuHelpAsync(exe, CancellationToken.None)), architecture));

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
                models.Add(new CpuModelInfo(name, Regex.Replace(match.Groups["desc"].Value, @"\s{2,}", " ").Trim()));
        }

        return models;
    }

    // host first, the machine default second, then everything QEMU lists.
    private static IReadOnlyList<CpuModelInfo> WithDefaults(IReadOnlyList<CpuModelInfo> models)
        => [Host, MachineDefault, .. models.Where(m => m.Name != Host.Name && m.Name.Length > 0)];
}
