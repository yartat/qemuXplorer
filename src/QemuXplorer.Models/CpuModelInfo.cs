namespace QemuXplorer.Models;

/// <summary>One CPU model as reported by <c>qemu-system-&lt;arch&gt; -cpu help</c>.</summary>
public sealed record CpuModelInfo(string Name, string Description = "")
{
    /// <summary>What the picker shows. The "machine default" entry has an empty name.</summary>
    public string DisplayName => Name.Length == 0 ? "(machine default)" : Name;

    public override string ToString() => Name;
}
