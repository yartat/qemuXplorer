using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.Core.Services;

public interface IQemuArgumentBuilder
{
    QemuLaunchArgs Build(VirtualMachine vm, QemuInstallation qemu);
}
