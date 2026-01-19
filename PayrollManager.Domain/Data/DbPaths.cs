using System.IO;

namespace PayrollManager.Domain.Data;

public static class DbPaths
{
    public static string GetDatabasePath()
    {
        var basePath = AppContext.BaseDirectory;
        var dataFolder = Path.Combine(basePath, "Data");
        Directory.CreateDirectory(dataFolder);
        return Path.Combine(dataFolder, "payroll.db");
    }
}
