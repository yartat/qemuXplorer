using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QemuXplorer.Data;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models.Entities;
using Xunit;

namespace QemuXplorer.Core.Tests.Data;

/// <summary>
/// Runs the repository's real queries against SQLite in memory with the real migrations applied.
/// The context is configured so that the "several collection includes, no splitting behavior"
/// warning becomes an exception: that warning is how EF reports a query that loads three child
/// collections in one statement and multiplies the rows, and it used to appear in the log on
/// every list load.
/// </summary>
public sealed class VirtualMachineRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly VirtualMachineRepository _sut;

    public VirtualMachineRepositoryTests()
    {
        // An in-memory SQLite database lives only as long as its connection.
        _connection.Open();

        var options = new DbContextOptionsBuilder<QemuXplorerDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        var factory = new TestFactory(options);
        using (var db = factory.CreateDbContext())
            db.Database.Migrate();

        _sut = new VirtualMachineRepository(factory);
    }

    public void Dispose() => _connection.Dispose();

    private static VirtualMachine Machine(string name = "m") => new()
    {
        Name = name,
        Disks = [new DiskDevice { FilePath = "/a.qcow2", Index = 0 }, new DiskDevice { FilePath = "/b.qcow2", Index = 1 }],
        NetworkAdapters = [new NetworkAdapter { MacAddress = "52:54:00:00:00:01" }],
        UsbDevices = [new UsbDevice { VendorId = 0x046D, ProductId = 0xC52B }]
    };

    [Fact]
    public async Task GetAll_LoadsEveryChildCollection_WithoutTheMultipleCollectionIncludeWarning()
    {
        await _sut.AddAsync(Machine("one"));
        await _sut.AddAsync(Machine("two"));

        var all = await _sut.GetAllAsync();

        all.Select(v => v.Name).Should().Equal("one", "two");
        all.Should().OnlyContain(v => v.Disks.Count == 2 && v.NetworkAdapters.Count == 1 && v.UsbDevices.Count == 1);
    }

    [Fact]
    public async Task GetById_LoadsEveryChildCollection_WithoutTheMultipleCollectionIncludeWarning()
    {
        var vm = Machine();
        await _sut.AddAsync(vm);

        var loaded = await _sut.GetByIdAsync(vm.Id);

        loaded.Should().NotBeNull();
        loaded!.Disks.Should().HaveCount(2);
        loaded.NetworkAdapters.Should().HaveCount(1);
        loaded.UsbDevices.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetById_ForAnUnknownId_ReturnsNull()
    {
        (await _sut.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Update_ReconcilesChildrenByKey_WithoutTheWarning()
    {
        var vm = Machine();
        await _sut.AddAsync(vm);

        var edited = (await _sut.GetByIdAsync(vm.Id))!;
        var removed = edited.Disks[0];
        edited.Disks.Remove(removed);
        edited.NetworkAdapters.Add(new NetworkAdapter { MacAddress = "52:54:00:00:00:02" });
        edited.Name = "renamed";
        await _sut.UpdateAsync(edited);

        var reloaded = (await _sut.GetByIdAsync(vm.Id))!;
        reloaded.Name.Should().Be("renamed");
        reloaded.Disks.Select(d => d.Id).Should().NotContain(removed.Id).And.HaveCount(1);
        reloaded.NetworkAdapters.Select(n => n.MacAddress).Should().BeEquivalentTo("52:54:00:00:00:01", "52:54:00:00:00:02");
        reloaded.UsbDevices.Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_AddingADiskToAnExistingMachine_InsertsItInsteadOfUpdatingARowThatIsNotThere()
    {
        // What "Add disk..." then Save does: the wizard builds a DiskDevice whose Guid is already
        // set. EF treats a set key as an existing row, so this used to throw
        // DbUpdateConcurrencyException ("expected 1 row, affected 0").
        var vm = Machine();
        await _sut.AddAsync(vm);

        var edited = (await _sut.GetByIdAsync(vm.Id))!;
        var fromWizard = new DiskDevice { FilePath = "/wizard.qcow2", Index = 2 };
        edited.Disks.Add(fromWizard);

        var act = () => _sut.UpdateAsync(edited);

        await act.Should().NotThrowAsync();
        var reloaded = (await _sut.GetByIdAsync(vm.Id))!;
        reloaded.Disks.Should().HaveCount(3).And.Contain(d => d.Id == fromWizard.Id && d.FilePath == "/wizard.qcow2");
    }

    [Fact]
    public async Task Update_ForAnUnknownMachine_DoesNothing()
    {
        var act = () => _sut.UpdateAsync(Machine());

        await act.Should().NotThrowAsync();
        (await _sut.GetAllAsync()).Should().BeEmpty();
    }

    private sealed class TestFactory(DbContextOptions<QemuXplorerDbContext> options) : IDbContextFactory<QemuXplorerDbContext>
    {
        public QemuXplorerDbContext CreateDbContext() => new(options);
    }
}
