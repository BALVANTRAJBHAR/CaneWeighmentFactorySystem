namespace CaneFactory.Infrastructure.Camera;

/// <summary>
/// Resolves the operator-configured base drive for weighment evidence images.
/// A blank setting means: choose the ready C:, D: or E: drive with the most
/// available space.  The application owns the WeighmentImage subfolder.
/// </summary>
public static class WeighmentImagePathResolver
{
    public static string ResolveRoot(string? configuredPath)
    {
        var basePath = string.IsNullOrWhiteSpace(configuredPath)
            ? DetectDefaultDrive()
            : NormalizeConfiguredPath(configuredPath!);

        return string.Equals(Path.GetFileName(basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "WeighmentImage", StringComparison.OrdinalIgnoreCase)
            ? basePath
            : Path.Combine(basePath, "WeighmentImage");
    }

    public static string DetectDefaultDrive()
    {
        var candidates = new[] { "C:\\", "D:\\", "E:\\" };
        var ready = candidates
            .Select(path => new DriveInfo(path))
            .Where(drive =>
            {
                try { return drive.IsReady; }
                catch { return false; }
            })
            .OrderByDescending(drive =>
            {
                try { return drive.AvailableFreeSpace; }
                catch { return -1L; }
            })
            .FirstOrDefault();

        if (ready != null) return ready.RootDirectory.FullName;

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrWhiteSpace(systemRoot) ? Path.GetTempPath() : systemRoot;
    }

    public static string NormalizeConfiguredPath(string path)
    {
        var value = path.Trim().Trim('"');
        if (value.Length == 2 && value[1] == ':') value += Path.DirectorySeparatorChar;
        return Path.GetFullPath(value);
    }

    public static IReadOnlyList<object> GetAvailableDrives()
    {
        return new[] { "C:\\", "D:\\", "E:\\" }
            .Select(path => new DriveInfo(path))
            .Where(drive =>
            {
                try { return drive.IsReady; }
                catch { return false; }
            })
            .Select(drive => new
            {
                path = drive.RootDirectory.FullName,
                availableFreeBytes = drive.AvailableFreeSpace
            })
            .Cast<object>()
            .ToList();
    }
}
