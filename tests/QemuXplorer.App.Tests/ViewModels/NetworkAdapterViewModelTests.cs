using Avalonia.Headless.XUnit;
using FluentAssertions;
using QemuXplorer.App.ViewModels;
using QemuXplorer.App.Views;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

public class NetworkAdapterViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-nic-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static NetworkAdapterViewModel Adding(params NetworkAdapter[] existing)
    {
        var sut = new NetworkAdapterViewModel();
        sut.InitializeForAdd(existing);
        return sut;
    }

    private static NetworkAdapterViewModel Editing(NetworkAdapter adapter)
    {
        var sut = new NetworkAdapterViewModel();
        sut.InitializeForEdit(adapter);
        return sut;
    }

    private static BackendChoice Choice(NetworkBackendType type) => NetworkAdapterViewModel.Backends.First(b => b.Type == type);

    // ---- the wizard ----

    [AvaloniaFact]
    public void Adding_StartsOnTheTypeStep_OfferingEveryBackend()
    {
        var sut = Adding();

        sut.IsStep0.Should().BeTrue();
        sut.IsEditing.Should().BeFalse();
        sut.BackendChoices.Select(b => b.Type).Should().BeEquivalentTo(Enum.GetValues<NetworkBackendType>());
        sut.StepTitle.Should().Contain("Step 1 of 3");
    }

    [AvaloniaFact]
    public void Adding_PrefillsAValidMacThatDoesNotClash()
    {
        var existing = new NetworkAdapter { MacAddress = "52:54:00:aa:bb:cc" };

        var sut = Adding(existing);

        sut.MacAddress.Should().MatchRegex("^52:54:00:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}$").And.NotBe(existing.MacAddress);
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public void TheWizard_WalksTypeSettingsReview_AndBack()
    {
        var sut = Adding();

        sut.NextCommand.Execute(null);
        sut.IsStep1.Should().BeTrue();
        sut.NextCommand.Execute(null);
        sut.IsStep2.Should().BeTrue();
        sut.IsLastStep.Should().BeTrue();
        sut.BackCommand.Execute(null);
        sut.IsStep1.Should().BeTrue();
    }

    [AvaloniaFact]
    public void NextIsRefused_WhileTheSettingsAreInvalid()
    {
        var sut = Adding();
        sut.NextCommand.Execute(null);
        sut.MacAddress = "not a mac";

        sut.NextCommand.Execute(null);

        sut.IsStep1.Should().BeTrue();
        sut.StatusMessage.Should().Contain("MAC");
        sut.CanGoNext.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Finishing_ReturnsTheAdapterBuiltFromTheForm()
    {
        var sut = Adding();
        sut.SelectedBackend = Choice(NetworkBackendType.User);
        sut.Model = NetworkModel.E1000;
        sut.UserHostname = "guest";
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;

        sut.FinishCommand.Execute(null);

        closed.Should().BeTrue();
        var result = sut.Result!;
        (result.BackendType, result.Model, result.UserHostname).Should().Be((NetworkBackendType.User, NetworkModel.E1000, "guest"));
        result.MacAddress.Should().Be(sut.MacAddress);
    }

    [AvaloniaFact]
    public void Cancel_ReturnsNothing()
    {
        var sut = Adding();

        sut.CancelCommand.Execute(null);

        sut.Result.Should().BeNull();
    }

    // ---- NAT ----

    [AvaloniaFact]
    public void Nat_PortForwardsReachThePreviewAndTheResult()
    {
        var sut = Adding();
        sut.SelectedBackend = Choice(NetworkBackendType.User);
        sut.AddForwardCommand.Execute(null);

        sut.Forwards.Should().ContainSingle();
        sut.CommandPreview.Should().Contain("hostfwd=tcp:127.0.0.1:2222-:22");
        sut.FinishCommand.Execute(null);
        PortForwardRules.Parse(sut.Result!.HostForwardingRules).Should().ContainSingle().Which.HostPort.Should().Be(2222);
    }

    [AvaloniaFact]
    public void Nat_BadForwards_AreNamedByNumber()
    {
        var sut = Adding();
        sut.AddForwardCommand.Execute(null);
        sut.Forwards[0].HostPort = 70000;
        sut.ValidationMessage.Should().Contain("Forward 1").And.Contain("host port");

        sut.Forwards[0].HostPort = 2222;
        sut.AddForwardCommand.Execute(null);
        sut.ValidationMessage.Should().Contain("Forward 2").And.Contain("already uses");

        sut.Forwards[1].HostPort = 3333;
        sut.Forwards[1].HostAddress = "not-an-ip";
        sut.ValidationMessage.Should().Contain("Forward 2").And.Contain("host address");

        sut.RemoveForwardCommand.Execute(sut.Forwards[1]);
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public void Nat_NetworkSettings_AreCheckedWhenGiven()
    {
        var sut = Adding();
        sut.UserSubnet = "10.0.2.0/24";
        sut.ValidationMessage.Should().BeNull();

        sut.UserSubnet = "banana";
        sut.ValidationMessage.Should().Contain("network");

        sut.UserSubnet = string.Empty;
        sut.UserDns = "1.2.3";
        sut.ValidationMessage.Should().Contain("DNS");

        sut.UserDns = string.Empty;
        sut.UserHostname = "bad host!";
        sut.ValidationMessage.Should().Contain("host name");
    }

    // ---- tap, bridge, socket ----

    [AvaloniaFact]
    public void Tap_ScriptsMustExist_WhenGiven()
    {
        var sut = Adding();
        sut.SelectedBackend = Choice(NetworkBackendType.Tap);
        sut.TapInterface = "tap0";
        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("tap,id=net0,ifname=tap0,script=no,downscript=no");

        sut.TapScript = Path.Combine(_dir, "missing.sh");
        sut.ValidationMessage.Should().Contain("up script");

        var script = Path.Combine(_dir, "up.sh");
        File.WriteAllText(script, "#!/bin/sh");
        sut.TapScript = script;
        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("script=" + script);
    }

    [AvaloniaFact]
    public void Bridge_WarnsOutsideLinux_AndOtherwiseNot()
    {
        var sut = Adding();
        sut.SelectedBackend = Choice(NetworkBackendType.Bridge);

        sut.HasWarning.Should().Be(!OperatingSystem.IsLinux());
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public void Socket_EachModeAsksForWhatItNeeds()
    {
        var sut = Adding();
        sut.SelectedBackend = Choice(NetworkBackendType.Socket);

        sut.SocketMode = NetworkSocketMode.Listen;
        sut.ValidationMessage.Should().BeNull();
        sut.IsSocketListen.Should().BeTrue();

        sut.SocketMode = NetworkSocketMode.Connect;
        sut.ValidationMessage.Should().Contain("host");
        sut.SocketHost = "10.0.0.5";
        sut.ValidationMessage.Should().BeNull();

        sut.SocketMode = NetworkSocketMode.Multicast;
        sut.SocketHost = "10.0.0.5";
        sut.ValidationMessage.Should().Contain("multicast");
        sut.SocketHost = "230.0.0.1";
        sut.ValidationMessage.Should().BeNull();

        sut.SocketMode = NetworkSocketMode.Udp;
        sut.SocketHost = "127.0.0.1";
        sut.SocketLocalHost = string.Empty;
        sut.ValidationMessage.Should().Contain("local");
        sut.SocketLocalHost = "127.0.0.1";
        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("udp=127.0.0.1:1234,localaddr=127.0.0.1:1235");
    }

    [AvaloniaFact]
    public void Socket_MulticastWarnsOnWindows() =>
        (Adding().Also(s => { s.SelectedBackend = Choice(NetworkBackendType.Socket); s.SocketMode = NetworkSocketMode.Multicast; }))
            .HasWarning.Should().Be(OperatingSystem.IsWindows());

    // ---- the model ----

    [AvaloniaFact]
    public void Model_OffersEveryCard_EachWithADescription()
    {
        var sut = Adding();

        sut.ModelChoices.Select(m => m.Model).Should().BeEquivalentTo(Enum.GetValues<NetworkModel>());
        sut.ModelChoices.Should().OnlyContain(m => m.Description.Length > 0);
    }

    [AvaloniaFact]
    public void Model_TheDescriptionFollowsTheSelection()
    {
        var sut = Adding();
        sut.ModelDescription.Should().Contain("VirtIO").And.Contain("driver");

        sut.SelectedModel = sut.ModelChoices.First(m => m.Model == NetworkModel.Rtl8139);

        sut.Model.Should().Be(NetworkModel.Rtl8139);
        sut.ModelDescription.Should().Contain("Realtek");
    }

    [AvaloniaFact]
    public void Model_SettingTheEnum_SelectsTheCard_AndEditingLoadsIt()
    {
        var sut = Adding();
        sut.Model = NetworkModel.Vmxnet3;
        sut.SelectedModel.QemuName.Should().Be("vmxnet3");
        sut.ModelDescription.Should().Contain("VMware");

        Editing(new NetworkAdapter { Model = NetworkModel.E1000E }).ModelDescription.Should().Contain("PCI Express");
    }

    // ---- the MAC ----

    [AvaloniaTheory]
    [InlineData("52:54:00:12:34:56", true)]
    [InlineData("", true)]
    [InlineData("52-54-00-12-34-56", false)]
    [InlineData("52:54:00:12:34", false)]
    [InlineData("53:54:00:12:34:56", false)]
    public void Mac_MustBeAUnicastAddress_OrEmpty(string mac, bool valid)
    {
        var sut = Adding();
        sut.MacAddress = mac;

        (sut.ValidationMessage is null).Should().Be(valid);
    }

    [AvaloniaFact]
    public void Generate_MakesAFreshValidMac()
    {
        var sut = Adding();
        var before = sut.MacAddress;

        sut.GenerateMacAddressCommand.Execute(null);

        sut.MacAddress.Should().NotBe(before);
        sut.ValidationMessage.Should().BeNull();
    }

    // ---- editing ----

    [AvaloniaFact]
    public void Editing_OpensOnTheSettingsOfTheAdaptersOwnType_AndCannotChangeIt()
    {
        var adapter = new NetworkAdapter
        {
            BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Connect, SocketHost = "10.0.0.5", SocketPort = 4000, MacAddress = "52:54:00:00:00:01"
        };

        var sut = Editing(adapter);

        sut.IsEditing.Should().BeTrue();
        sut.IsStep1.Should().BeTrue();
        (sut.IsSocket, sut.IsUser, sut.IsTap, sut.IsBridge).Should().Be((true, false, false, false));
        sut.CanEditType.Should().BeFalse();
        sut.CanGoBack.Should().BeFalse();
        sut.ShowNext.Should().BeFalse();
        sut.IsLastStep.Should().BeTrue();
        (sut.SocketHost, sut.SocketPort, sut.SocketMode).Should().Be(("10.0.0.5", 4000, NetworkSocketMode.Connect));
        sut.DialogTitle.Should().StartWith("Edit adapter");
    }

    [AvaloniaFact]
    public void Editing_LoadsPortForwards_AndSavingKeepsTheAdaptersIdentity()
    {
        var adapter = new NetworkAdapter
        {
            VmId = Guid.NewGuid(),
            HostForwardingRules = PortForwardRules.ToJson([new PortForward("tcp", "127.0.0.1", 8080, "", 80)])
        };
        var sut = Editing(adapter);
        sut.Forwards.Should().ContainSingle().Which.HostPort.Should().Be(8080);

        sut.Forwards[0].HostPort = 9090;
        sut.Model = NetworkModel.Vmxnet3;
        sut.FinishCommand.Execute(null);

        var result = sut.Result!;
        (result.Id, result.VmId, result.Model).Should().Be((adapter.Id, adapter.VmId, NetworkModel.Vmxnet3));
        PortForwardRules.Parse(result.HostForwardingRules).Single().HostPort.Should().Be(9090);
        PortForwardRules.Parse(adapter.HostForwardingRules).Single().HostPort.Should().Be(8080, "the original is not touched");
    }

    [AvaloniaTheory]
    [InlineData(NetworkBackendType.User)]
    [InlineData(NetworkBackendType.Tap)]
    [InlineData(NetworkBackendType.Bridge)]
    [InlineData(NetworkBackendType.Socket)]
    public void Editing_ShowsOnlyTheSettingsOfThatType(NetworkBackendType type)
    {
        var sut = Editing(new NetworkAdapter { BackendType = type, SocketPort = 1234 });

        new[] { sut.IsUser, sut.IsTap, sut.IsBridge, sut.IsSocket }.Count(shown => shown).Should().Be(1);
        sut.SelectedBackend.Type.Should().Be(type);
    }

    // ---- the window ----

    [AvaloniaFact]
    public void TheWindow_BindsInEveryStepAndType()
    {
        var sut = Adding();
        var window = new NetworkAdapterView { DataContext = sut };
        window.Show();

        foreach (var backend in NetworkAdapterViewModel.Backends)
        {
            sut.SelectedBackend = backend;
            sut.Step = 0;
            sut.NextCommand.Execute(null);
            sut.AddForwardCommand.Execute(null);
            sut.NextCommand.Execute(null);
            sut.BackCommand.Execute(null);
            sut.BackCommand.Execute(null);
        }

        window.IsVisible.Should().BeTrue();
        window.Close();
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
