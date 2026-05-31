using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Models.Entities;

public class CpuConfig
{
    public string Model { get; set; } = "host";
    public int Count { get; set; } = 1;
    public int Sockets { get; set; } = 1;
    public int Cores { get; set; } = 1;
    public int Threads { get; set; } = 1;
}
