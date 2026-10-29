using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using QemuXplorer.App.ViewModels;
using QemuXplorer.Core.Services;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

/// <summary>
/// The editor's CPU "Model" field: a picker over what the selected architecture's QEMU reports,
/// defaulting to "host", that never rewrites a saved value just because the editor was opened.
/// </summary>
public class CpuModelSelectionTests
{
    private const string X86 = "Available CPUs:\n  Broadwell-v1  Intel Core Processor\n  max  everything\n";
    private const string Arm = "Available CPUs:\n  cortex-a53\n  max\n";

    private static VmEditViewModel CreateSut(CpuModelService cpuModels, bool withQemu = true)
    {
        var vmRepo = Substitute.For<IVirtualMachineRepository>();
        var qemuRepo = Substitute.For<IQemuInstallationRepository>();
        qemuRepo.GetDefaultAsync(Arg.Any<CancellationToken>())
            .Returns(withQemu ? new QemuInstallation { BinaryDirectory = "/q" } : null);
        var argBuilder = Substitute.For<IQemuArgumentBuilder>();
        var service = new VirtualMachineService(vmRepo, qemuRepo, argBuilder, Substitute.For<IQemuProcessManager>());

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(VirtualMachineService)).Returns(service);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new VmEditViewModel(scopeFactory, argBuilder, cpuModels);
    }

    private static async Task<VmEditViewModel> OpenedAsync(
        CpuModelService cpuModels, GuestArchitecture arch, string cpu, bool withQemu = true)
    {
        var sut = CreateSut(cpuModels, withQemu);
        var vm = new VirtualMachine { Architecture = arch, MachineType = "q35" };
        vm.Cpu.Model = cpu;
        await sut.LoadVmAsync(vm);
        return sut;
    }

    [AvaloniaFact]
    public void ANewMachine_DefaultsToHost()
    {
        new VirtualMachine().Cpu.Model.Should().Be("host");
        CreateSut(new FakeCpuModels()).CpuModel.Should().Be("host");
    }

    [AvaloniaFact]
    public async Task OpeningAMachine_ListsTheModelsQemuReports_HostFirst()
    {
        var sut = await OpenedAsync(new FakeCpuModels { [GuestArchitecture.X86_64] = X86 }, GuestArchitecture.X86_64, "host");

        sut.CpuModels.Select(m => m.Name).Should().Equal("host", "Broadwell-v1", "max");
        sut.SelectedCpuModel!.Name.Should().Be("host");
        sut.HasCpuModelsNote.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ChoosingAModel_WritesItsNameToTheMachine()
    {
        var sut = await OpenedAsync(new FakeCpuModels { [GuestArchitecture.X86_64] = X86 }, GuestArchitecture.X86_64, "host");

        sut.SelectedCpuModel = sut.CpuModels.First(m => m.Name == "Broadwell-v1");

        sut.CpuModel.Should().Be("Broadwell-v1");
    }

    [AvaloniaFact]
    public async Task ChangingArchitecture_ReloadsTheList()
    {
        var fake = new FakeCpuModels { [GuestArchitecture.X86_64] = X86, [GuestArchitecture.Aarch64] = Arm };
        var sut = await OpenedAsync(fake, GuestArchitecture.X86_64, "host");

        sut.Architecture = GuestArchitecture.Aarch64;
        await sut.CpuModelsLoaded;

        sut.CpuModels.Select(m => m.Name).Should().Equal("host", "cortex-a53", "max");
    }

    [AvaloniaFact]
    public async Task ChangingArchitecture_ReplacesAModelTheNewEmulatorDoesNotList()
    {
        var fake = new FakeCpuModels { [GuestArchitecture.X86_64] = X86, [GuestArchitecture.Aarch64] = Arm };
        var sut = await OpenedAsync(fake, GuestArchitecture.X86_64, "Broadwell-v1");

        sut.Architecture = GuestArchitecture.Aarch64;
        await sut.CpuModelsLoaded;

        sut.CpuModel.Should().Be("host");
    }

    [AvaloniaFact]
    public async Task ChangingArchitecture_KeepsAModelBothEmulatorsOffer()
    {
        var fake = new FakeCpuModels { [GuestArchitecture.X86_64] = X86, [GuestArchitecture.Aarch64] = Arm };
        var sut = await OpenedAsync(fake, GuestArchitecture.X86_64, "max");

        sut.Architecture = GuestArchitecture.Aarch64;
        await sut.CpuModelsLoaded;

        sut.CpuModel.Should().Be("max");
    }

    [AvaloniaFact]
    public async Task OpeningAMachine_NeverRewritesASavedModelQemuDoesNotList()
    {
        var sut = await OpenedAsync(new FakeCpuModels { [GuestArchitecture.X86_64] = X86 }, GuestArchitecture.X86_64, "Skylake-Server");

        sut.CpuModel.Should().Be("Skylake-Server");
        sut.CpuModels.Should().Contain(m => m.Name == "Skylake-Server" && m.Description.Contains("not listed"));
        sut.SelectedCpuModel!.Name.Should().Be("Skylake-Server");
    }

    [AvaloniaFact]
    public async Task WithoutADefaultQemu_OffersHostAndExplainsWhy()
    {
        var sut = await OpenedAsync(new FakeCpuModels(), GuestArchitecture.X86_64, "host", withQemu: false);

        sut.CpuModels.Select(m => m.Name).Should().Equal("host");
        sut.HasCpuModelsNote.Should().BeTrue();
        sut.CpuModelsNote.Should().Contain("Installations");
    }

    [AvaloniaFact]
    public async Task WhenQemuCannotBeRun_KeepsTheSavedModelAndShowsTheError()
    {
        var fake = new FakeCpuModels { Fail = true };
        var sut = await OpenedAsync(fake, GuestArchitecture.X86_64, "Broadwell-v1");

        sut.CpuModel.Should().Be("Broadwell-v1", "a failed probe says nothing about the saved model");
        sut.CpuModelsNote.Should().Contain("Could not read");
        sut.CpuModels.Select(m => m.Name).Should().Contain(["host", "Broadwell-v1"]);
    }

    [AvaloniaFact]
    public async Task WhenQemuCannotBeRun_ChangingArchitectureDoesNotThrowAwayTheModel()
    {
        var fake = new FakeCpuModels { Fail = true };
        var sut = await OpenedAsync(fake, GuestArchitecture.X86_64, "Broadwell-v1");

        sut.Architecture = GuestArchitecture.Aarch64;
        await sut.CpuModelsLoaded;

        sut.CpuModel.Should().Be("Broadwell-v1");
    }

    [AvaloniaFact]
    public void LoadVm_WithoutTheAsyncProbe_StillShowsHostSoThePickerIsNeverEmpty()
    {
        var sut = CreateSut(new FakeCpuModels());
        var vm = new VirtualMachine();

        sut.LoadVm(vm);

        sut.CpuModels.Should().ContainSingle().Which.Name.Should().Be("host");
        sut.SelectedCpuModel!.Name.Should().Be("host");
    }

    private sealed class FakeCpuModels : CpuModelService
    {
        private readonly Dictionary<GuestArchitecture, string> _output = [];
        public bool Fail { get; set; }

        public string this[GuestArchitecture arch]
        {
            set => _output[arch] = value;
        }

        protected override Task<string> ReadCpuHelpAsync(string exe, CancellationToken ct)
        {
            if (Fail)
                return Task.FromException<string>(new InvalidOperationException("qemu would not start"));

            var arch = Enum.GetValues<GuestArchitecture>()
                .First(a => exe.EndsWith(QemuArgumentBuilder.GetBinaryName(a), StringComparison.Ordinal));
            return Task.FromResult(_output.GetValueOrDefault(arch, "Available CPUs:\n"));
        }
    }
}
