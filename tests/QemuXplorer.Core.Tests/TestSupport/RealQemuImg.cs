using Xunit;

namespace QemuXplorer.Core.Tests.TestSupport;

/// <summary>Finds a real qemu-img on this machine, so tests can check what it actually does.</summary>
public static class RealQemuImg
{
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        var exe = OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img";
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat([@"C:\Program Files\qemu", "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"]);

        foreach (var directory in candidates)
        {
            try
            {
                var path = System.IO.Path.Combine(directory, exe);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped.
            }
        }

        return null;
    }
}

/// <summary>A fact that runs only where qemu-img is installed; otherwise it is reported as skipped, not passed.</summary>
public sealed class RealQemuImgFactAttribute : FactAttribute
{
    public RealQemuImgFactAttribute()
    {
        if (RealQemuImg.Path is null)
            Skip = "qemu-img is not installed on this machine";
    }
}

/// <summary>The qemu-system-x86_64 next to the qemu-img found above, if any.</summary>
public static class RealQemuSystem
{
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        if (RealQemuImg.Path is null) return null;
        var exe = OperatingSystem.IsWindows() ? "qemu-system-x86_64.exe" : "qemu-system-x86_64";
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(RealQemuImg.Path)!, exe);
        return File.Exists(path) ? path : null;
    }
}

public sealed class RealQemuSystemFactAttribute : FactAttribute
{
    public RealQemuSystemFactAttribute()
    {
        if (RealQemuSystem.Path is null)
            Skip = "qemu-system-x86_64 is not installed on this machine";
    }
}

public sealed class RealQemuSystemTheoryAttribute : TheoryAttribute
{
    public RealQemuSystemTheoryAttribute()
    {
        if (RealQemuSystem.Path is null)
            Skip = "qemu-system-x86_64 is not installed on this machine";
    }
}
