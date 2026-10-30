using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QemuXplorer.Data;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Data;

public sealed class NetworkAndUsbPersistenceTests : IDisposable
{
    private const string PreviousMigration = "20261027040053_DiskConnectionJson";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<QemuXplorerDbContext> _options;

    public NetworkAndUsbPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<QemuXplorerDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(DbContextOptions<QemuXplorerDbContext> options) : IDbContextFactory<QemuXplorerDbContext>
    {
        public QemuXplorerDbContext CreateDbContext() => new(options);
    }

    private static string Key(Guid id) => id.ToString().ToUpperInvariant();

    // Inserts one row using the columns the table has right now, filling every column that is not given
    // with a neutral value (empty text, zero), so the test does not depend on a column list that moves.
    private void InsertRow(string table, Dictionary<string, object> values)
    {
        var names = new List<string>();
        var parameters = new List<SqliteParameter>();
        using (var info = _connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table})";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2).ToUpperInvariant();
                names.Add(name);
                var nullable = reader.GetInt32(3) == 0;
                object value = values.TryGetValue(name, out var given) ? given
                    : nullable ? DBNull.Value
                    : name.EndsWith("At", StringComparison.Ordinal) ? "2026-01-01 00:00:00"
                    : type.Contains("TEXT") ? string.Empty
                    : 0L;
                parameters.Add(new SqliteParameter("@" + name, value));
            }
        }

        using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(n => "@" + n))})";
        foreach (var parameter in parameters) insert.Parameters.Add(parameter);
        insert.ExecuteNonQuery();
    }

    [Fact]
    public async Task EveryNewField_SurvivesASaveAndALoad()
    {
        using (var db = new QemuXplorerDbContext(_options)) db.Database.Migrate();
        var sut = new VirtualMachineRepository(new TestFactory(_options));
        var vm = new VirtualMachine
        {
            Name = "m",
            NetworkAdapters =
            [
                new NetworkAdapter
                {
                    BackendType = NetworkBackendType.User, Model = NetworkModel.Vmxnet3, UserSubnet = "10.1.0.0/24", UserDhcpStart = "10.1.0.9",
                    UserDns = "10.1.0.3", UserHostname = "g", UserRestrict = true,
                    HostForwardingRules = PortForwardRules.ToJson([new PortForward("tcp", "127.0.0.1", 2222, "", 22)])
                },
                new NetworkAdapter { BackendType = NetworkBackendType.Tap, TapInterface = "tap0", TapScript = "/up", TapDownScript = "/down" },
                new NetworkAdapter
                {
                    BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Udp, SocketHost = "1.2.3.4", SocketPort = 10,
                    SocketLocalHost = "5.6.7.8", SocketLocalPort = 11
                }
            ],
            UsbDevices =
            [
                new UsbDevice { DeviceType = UsbDeviceType.HostPassthrough, HostBus = 2, HostAddress = 9, IsBootDevice = true, Description = "Key" },
                new UsbDevice
                {
                    DeviceType = UsbDeviceType.Storage, ImagePath = "/s.img", ImageFormat = DiskFormat.QCow2, IsReadOnly = true, IsRemovable = true
                },
                new UsbDevice { DeviceType = UsbDeviceType.Audio, AudioDriver = "dsound" },
                new UsbDevice { DeviceType = UsbDeviceType.Serial, SerialBackend = UsbSerialBackend.File, SerialTarget = "/o.log", SerialPort = 7 },
                new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = UsbControllerModel.Ehci }
            ]
        };

        await sut.AddAsync(vm);
        var loaded = (await sut.GetByIdAsync(vm.Id))!;

        loaded.NetworkAdapters.Should().BeEquivalentTo(vm.NetworkAdapters, o => o.Excluding(n => n.VirtualMachine));
        loaded.UsbDevices.Should().BeEquivalentTo(vm.UsbDevices, o => o.Excluding(u => u.VirtualMachine));
    }

    [Fact]
    public async Task EditingAnAdapterOrADevice_UpdatesTheSameRow()
    {
        using (var db = new QemuXplorerDbContext(_options)) db.Database.Migrate();
        var sut = new VirtualMachineRepository(new TestFactory(_options));
        var nic = new NetworkAdapter { SocketMode = NetworkSocketMode.Listen, SocketPort = 1 };
        var usb = new UsbDevice { DeviceType = UsbDeviceType.Tablet };
        var vm = new VirtualMachine { Name = "m", NetworkAdapters = [nic], UsbDevices = [usb] };
        await sut.AddAsync(vm);

        // What the edit dialogs hand back: a new object with the same key.
        var edited = (await sut.GetByIdAsync(vm.Id))!;
        edited.NetworkAdapters[0] = new NetworkAdapter { Id = nic.Id, VmId = vm.Id, BackendType = NetworkBackendType.Socket, SocketPort = 4242 };
        edited.UsbDevices[0] = new UsbDevice { Id = usb.Id, VmId = vm.Id, DeviceType = UsbDeviceType.Mouse };
        await sut.UpdateAsync(edited);

        var reloaded = (await sut.GetByIdAsync(vm.Id))!;
        reloaded.NetworkAdapters.Should().ContainSingle().Which.Should().Match<NetworkAdapter>(n => n.Id == nic.Id && n.SocketPort == 4242);
        reloaded.UsbDevices.Should().ContainSingle().Which.Should().Match<UsbDevice>(u => u.Id == usb.Id && u.DeviceType == UsbDeviceType.Mouse);
    }

    [Fact]
    public async Task MachinesSavedBeforeThisMigration_KeepTheirDevices_AndReadAsTheOldKind()
    {
        // Build the database exactly as the previous release left it, with a machine in it.
        var vmId = Guid.NewGuid();
        using (var db = new QemuXplorerDbContext(_options))
            db.GetService<IMigrator>().Migrate(PreviousMigration);

        InsertRow("VirtualMachines", new() { ["Id"] = Key(vmId), ["Name"] = "old", ["CreatedAt"] = "2026-01-01 00:00:00", ["UpdatedAt"] = "2026-01-01 00:00:00" });
        InsertRow("UsbDevices", new() { ["Id"] = Key(Guid.NewGuid()), ["VmId"] = Key(vmId), ["VendorId"] = 1133, ["ProductId"] = 50475, ["Description"] = "Receiver", ["IsPassthrough"] = 1 });
        InsertRow("NetworkAdapters", new() { ["Id"] = Key(Guid.NewGuid()), ["VmId"] = Key(vmId), ["Model"] = 1, ["MacAddress"] = "52:54:00:00:00:09" });

        using (var db = new QemuXplorerDbContext(_options)) db.Database.Migrate();

        var sut = new VirtualMachineRepository(new TestFactory(_options));
        var vm = (await sut.GetByIdAsync(vmId))!;

        var usb = vm.UsbDevices.Should().ContainSingle().Subject;
        (usb.DeviceType, usb.VendorId, usb.ProductId, usb.IsPassthrough, usb.Description)
            .Should().Be((UsbDeviceType.HostPassthrough, (ushort)1133, (ushort)50475, true, "Receiver"));
        var nic = vm.NetworkAdapters.Should().ContainSingle().Subject;
        (nic.BackendType, nic.Model, nic.MacAddress, nic.SocketMode, nic.TapScript).Should()
            .Be((NetworkBackendType.User, NetworkModel.E1000, "52:54:00:00:00:09", NetworkSocketMode.Listen, string.Empty));
    }
}
