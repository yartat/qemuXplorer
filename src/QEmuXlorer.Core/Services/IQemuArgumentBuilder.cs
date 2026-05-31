using QEmuXlorer.Models.Dtos;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Core.Services;

public interface IQemuArgumentBuilder
{
    QemuLaunchArgs Build(VirtualMachine vm, QemuInstallation qemu);
}
