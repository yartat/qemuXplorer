namespace QemuXplorer.Data;

public static class DbPathHelper
{
    private const string FolderName = ".qemu_explorer";
    private const string FileName = "data.db";

    public static string GetDataDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            home = Directory.GetCurrentDirectory();

        return Path.Combine(home, FolderName);
    }

    public static string GetDatabasePath()
    {
        var folder = GetDataDirectory();
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, FileName);
    }
}
