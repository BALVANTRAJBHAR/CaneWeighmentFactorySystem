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
        var configured = string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : NormalizeConfiguredPath(configuredPath!);

        // A configuration can be copied from another PC (for example D:\ on a
        // development machine) where that drive does not exist on this PC. Do
        // not let one stale drive setting stop evidence capture; safely select
        // the ready C:/D:/E: drive with the most free space instead.
        var basePath = configured != null && IsAvailableOnThisComputer(configured)
            ? configured
            : DetectDefaultDrive();

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

    public static bool IsAvailableOnThisComputer(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root)) return false;
            var drive = new DriveInfo(root);
            return drive.IsReady;
        }
        catch
        {
            return false;
        }
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
