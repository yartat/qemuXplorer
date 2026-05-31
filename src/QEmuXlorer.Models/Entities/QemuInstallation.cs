namespace QEmuXlorer.Models.Entities;

public class QemuInstallation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string BinaryDirectory { get; set; } = string.Empty;
    public string DetectedVersion { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string Platform { get; set; } = string.Empty;
}
