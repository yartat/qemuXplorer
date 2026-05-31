using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(QEmuXplorerDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (!db.AppSettings.Any())
        {
            db.AppSettings.AddRange(
                new AppSetting { Key = "Theme", Value = "Light", Description = "Application color theme (Light / Dark)" },
                new AppSetting { Key = "DefaultQemuPath", Value = string.Empty, Description = "Default directory containing QEMU binaries" },
                new AppSetting { Key = "AutoStartMonitor", Value = "false", Description = "Automatically connect to QEMU monitor on VM start" }
            );
            await db.SaveChangesAsync();
        }
    }
}
