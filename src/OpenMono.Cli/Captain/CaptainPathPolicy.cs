namespace OpenMono.Captain;

public static class CaptainPathPolicy
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly string[] ProtectedDirs =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube"),
    ];

    private static readonly string[] ProtectedFileNames =
    [
        ".gitconfig",
        ".netrc",
        ".npmrc",
        ".pypirc",
        "credentials",
        "id_rsa", "id_rsa.pub",
        "id_ed25519", "id_ed25519.pub",
        "id_ecdsa", "id_ecdsa.pub",
        "id_dsa", "id_dsa.pub",
    ];

    public static string? ValidatePath(string resolvedPath, IReadOnlyList<string> roots)
    {
        if (IsUncPath(resolvedPath))
            return $"Access denied: UNC paths are not allowed ('{resolvedPath}').";

        if (IsDeviceFile(resolvedPath))
            return $"Access denied: device files cannot be accessed ('{resolvedPath}').";

        if (IsProtectedFile(resolvedPath))
            return $"Access denied: '{resolvedPath}' is a protected credential or configuration file.";

        if (!IsWithinRoots(resolvedPath, roots))
            return $"Access denied: '{resolvedPath}' is outside the configured captain roots.";

        return null;
    }

    public static string? ValidateDirectory(string resolvedDir, IReadOnlyList<string> roots)
    {
        if (IsUncPath(resolvedDir))
            return $"Access denied: UNC paths are not allowed ('{resolvedDir}').";

        if (IsDeviceFile(resolvedDir))
            return $"Access denied: device files cannot be accessed ('{resolvedDir}').";

        if (!IsWithinRoots(resolvedDir, roots))
            return $"Access denied: '{resolvedDir}' is outside the configured captain roots.";

        return null;
    }

    private static bool IsWithinRoots(string resolvedPath, IReadOnlyList<string> roots)
    {
        var normalizedPath = NormalizeDirPath(resolvedPath);
        foreach (var root in roots)
        {
            var normalizedRoot = NormalizeDirPath(root);
            if (normalizedPath.StartsWith(normalizedRoot, PathComparison))
                return true;
        }
        return false;
    }

    private static bool IsProtectedFile(string resolvedPath)
    {
        foreach (var dir in ProtectedDirs)
        {
            if (resolvedPath.StartsWith(NormalizeDirPath(dir), PathComparison))
                return true;
        }

        var fileName = Path.GetFileName(resolvedPath);
        if (fileName.Equals(".env", PathComparison) ||
            fileName.StartsWith(".env.", PathComparison))
            return true;

        foreach (var name in ProtectedFileNames)
        {
            if (fileName.Equals(name, PathComparison))
                return true;
        }

        if (resolvedPath.Contains(Path.Combine(".openmono", "settings"), PathComparison))
            return true;

        return false;
    }

    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static bool IsDeviceFile(string resolvedPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(resolvedPath);
            return WindowsDeviceNames.Contains(nameWithoutExt);
        }
        return resolvedPath.StartsWith("/dev/", StringComparison.Ordinal)
            || resolvedPath.StartsWith("/proc/", StringComparison.Ordinal);
    }

    private static bool IsUncPath(string resolvedPath) =>
        OperatingSystem.IsWindows() &&
        resolvedPath.StartsWith(@"\\", StringComparison.Ordinal);

    private static string NormalizeDirPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
}

