namespace QemuXplorer.Models;

/// <summary>One CPU model as reported by <c>qemu-system-&lt;arch&gt; -cpu help</c>.</summary>
public sealed record CpuModelInfo(string Name, string Description = "")
{
    public override string ToString() => Name;
}
