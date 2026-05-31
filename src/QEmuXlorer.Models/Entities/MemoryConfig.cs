namespace QEmuXlorer.Models.Entities;

public class MemoryConfig
{
    public long SizeMB { get; set; } = 1024;
    public int HotplugSlots { get; set; } = 0;
}
