using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QemuXplorer.Data;

/// <summary>
/// Lets "dotnet ef" build the context without a startup project. The Avalonia entry point
/// does not use a generic host, so EF has no host builder to discover at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<QemuXplorerDbContext>
{
    public QemuXplorerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QemuXplorerDbContext>()
            .UseSqlite($"Data Source={DbPathHelper.GetDatabasePath()}")
            .Options;

        return new QemuXplorerDbContext(options);
    }
}
