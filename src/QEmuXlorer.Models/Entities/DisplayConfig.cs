using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Models.Entities;

public class DisplayConfig
{
    public DisplayType Type { get; set; } = DisplayType.Default;
    public VgaType VgaModel { get; set; } = VgaType.Std;
    public string? VncAddress { get; set; }
    public int VncPort { get; set; } = 5900;
    public int SpicePort { get; set; } = 5930;
    public bool IsFullscreen { get; set; }
    public bool IsGLEnabled { get; set; }
}
