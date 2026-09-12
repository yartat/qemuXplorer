using Microsoft.EntityFrameworkCore;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IDbContextFactory<QemuXplorerDbContext> factory,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        await db.Database.MigrateAsync(ct);

        if (!await db.AppSettings.AnyAsync(ct))
        {
            db.AppSettings.AddRange(
                new AppSetting { Key = "Theme", Value = "Dark", Description = "Application color theme (Light / Dark)" },
                new AppSetting { Key = "DefaultQemuPath", Value = string.Empty, Description = "Default directory containing QEMU binaries" },
                new AppSetting { Key = "AutoStartMonitor", Value = "false", Description = "Automatically connect to QEMU monitor on VM start" }
            );
            await db.SaveChangesAsync(ct);
        }
    }
}
