using Microsoft.EntityFrameworkCore;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data;

public class QEmuXplorerDbContext : DbContext
{
    public QEmuXplorerDbContext(DbContextOptions<QEmuXplorerDbContext> options) : base(options) { }

    public DbSet<VirtualMachine> VirtualMachines => Set<VirtualMachine>();
    public DbSet<DiskDevice> DiskDevices => Set<DiskDevice>();
    public DbSet<NetworkAdapter> NetworkAdapters => Set<NetworkAdapter>();
    public DbSet<UsbDevice> UsbDevices => Set<UsbDevice>();
    public DbSet<QemuInstallation> QemuInstallations => Set<QemuInstallation>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VirtualMachine>(vm =>
        {
            vm.HasKey(x => x.Id);
            vm.OwnsOne(x => x.Cpu);
            vm.OwnsOne(x => x.Memory);
            vm.OwnsOne(x => x.Display);
            vm.HasMany(x => x.Disks)
              .WithOne(x => x.VirtualMachine)
              .HasForeignKey(x => x.VmId)
              .OnDelete(DeleteBehavior.Cascade);
            vm.HasMany(x => x.NetworkAdapters)
              .WithOne(x => x.VirtualMachine)
              .HasForeignKey(x => x.VmId)
              .OnDelete(DeleteBehavior.Cascade);
            vm.HasMany(x => x.UsbDevices)
              .WithOne(x => x.VirtualMachine)
              .HasForeignKey(x => x.VmId)
              .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiskDevice>().HasKey(x => x.Id);
        modelBuilder.Entity<NetworkAdapter>().HasKey(x => x.Id);
        modelBuilder.Entity<UsbDevice>().HasKey(x => x.Id);
        modelBuilder.Entity<QemuInstallation>().HasKey(x => x.Id);
        modelBuilder.Entity<AppSetting>().HasKey(x => x.Key);
    }
}
