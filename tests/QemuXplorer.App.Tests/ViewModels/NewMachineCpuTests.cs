using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using QemuXplorer.App.ViewModels;
using QemuXplorer.Core.Services;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models.Entities;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

/// <summary>A machine created with "New machine" starts with the default CPU for its architecture.</summary>
public class NewMachineCpuTests
{
    [AvaloniaFact]
    public async Task ANewMachine_StartsWithTheDefaultCpuForItsArchitecture()
    {
        VirtualMachine? saved = null;
        var vmRepo = Substitute.For<IVirtualMachineRepository>();
        vmRepo.AddAsync(Arg.Do<VirtualMachine>(v => saved = v), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = new VirtualMachineService(
            vmRepo,
            Substitute.For<IQemuInstallationRepository>(),
            Substitute.For<IQemuArgumentBuilder>(),
            Substitute.For<IQemuProcessManager>());
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(VirtualMachineService)).Returns(service);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var sut = new VirtualMachinesViewModel(scopeFactory);

        await sut.AddVmCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.Cpu.Model.Should().Be(CpuModelService.DefaultModelName(saved.Architecture));
    }
}
