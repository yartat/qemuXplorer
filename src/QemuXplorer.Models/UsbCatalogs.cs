using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models;

/// <summary>A USB host controller QEMU can emulate: its device name and when to pick it.</summary>
public sealed record UsbControllerInfo(UsbControllerModel Model, string QemuName, string Label, string Description)
{
    public override string ToString() => Label;
}

public static class UsbControllerCatalog
{
    public static IReadOnlyList<UsbControllerInfo> All { get; } =
    [
        new(UsbControllerModel.Xhci, "qemu-xhci", "USB 3 (xHCI)",
            "Emulates a USB 3 controller (up to 5 Gbit/s), which also takes USB 2 and 1.1 devices. The usual choice; "
            + "recent Windows and Linux guests have the driver built in, older ones may not."),
        new(UsbControllerModel.Ehci, "usb-ehci", "USB 2 (EHCI)",
            "Emulates a USB 2 controller (480 Mbit/s). Pick it for a guest too old for xHCI that still has USB 2 support."),
        new(UsbControllerModel.Uhci, "piix3-usb-uhci", "USB 1.1 (UHCI)",
            "Emulates the original USB 1.1 controller (12 Mbit/s) of the PIIX3 chipset. Slow; only for very old guests, "
            + "and only on PC-type machines.")
    ];

    private static readonly Dictionary<UsbControllerModel, UsbControllerInfo> ByModel = All.ToDictionary(c => c.Model);

    public static UsbControllerInfo Get(UsbControllerModel model) => ByModel[model];

    /// <summary>The name <c>-device</c> expects.</summary>
    public static string QemuName(UsbControllerModel model) => ByModel[model].QemuName;
}

/// <summary>A QEMU audio driver: how the guest's sound reaches (or does not reach) this computer.</summary>
public sealed record AudioDriverInfo(string Name, string Label, string Description)
{
    public override string ToString() => Label;
}

public static class AudioDriverCatalog
{
    public static IReadOnlyList<AudioDriverInfo> All { get; } =
    [
        new("dsound", "DirectSound (dsound)", "Plays through Windows' DirectSound. The normal choice on Windows."),
        new("coreaudio", "Core Audio (coreaudio)", "Plays through macOS' Core Audio. The normal choice on macOS."),
        new("pa", "PulseAudio (pa)",
            "Plays through the PulseAudio sound server, which most Linux desktops run (PipeWire offers a compatible one). "
            + "The normal choice on Linux."),
        new("pipewire", "PipeWire (pipewire)", "Plays straight through PipeWire, the sound server of recent Linux desktops."),
        new("alsa", "ALSA (alsa)",
            "Plays through the Linux kernel's sound layer directly. Simple, but the guest may not get the card while "
            + "another program is using it."),
        new("sdl", "SDL (sdl)",
            "Plays through the SDL library, which works on every system. Needs a QEMU built with SDL."),
        new("wav", "WAV file (wav)",
            "Writes everything the guest plays to a file (qemu.wav in QEMU's working folder) and plays nothing. "
            + "Useful for checking that a guest makes sound at all."),
        new("none", "None (none)", "The device exists in the guest but plays nowhere. Use it when a guest needs a sound card but you do not want sound.")
    ];

    /// <summary>The drivers worth offering on this computer, its usual one first.</summary>
    public static IReadOnlyList<AudioDriverInfo> ForThisSystem { get; } = Pick(
        OperatingSystem.IsWindows() ? ["dsound", "sdl", "wav", "none"]
        : OperatingSystem.IsMacOS() ? ["coreaudio", "sdl", "wav", "none"]
        : ["pa", "pipewire", "alsa", "sdl", "wav", "none"]);

    private static IReadOnlyList<AudioDriverInfo> Pick(string[] names) => names.Select(Get).ToList();

    /// <summary>The entry for a driver name; a name the catalog does not know (typed into an old row) still gets one.</summary>
    public static AudioDriverInfo Get(string name)
        => All.FirstOrDefault(d => d.Name == name)
           ?? new AudioDriverInfo(name, name, "A driver QEMU may offer on some systems; the app has no description for it.");
}
