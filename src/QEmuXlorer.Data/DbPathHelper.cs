namespace QEmuXlorer.Data;

public static class DbPathHelper
{
    public static string GetDatabasePath()
    {
        string folder;

        if (OperatingSystem.IsWindows())
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QEmuXlorer");
        }
        else if (OperatingSystem.IsMacOS())
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "QEmuXlorer");
        }
        else
        {
            // Linux / other
            string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            folder = Path.Combine(xdgData, "QEmuXlorer");
        }

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "data.db");
    }
}
