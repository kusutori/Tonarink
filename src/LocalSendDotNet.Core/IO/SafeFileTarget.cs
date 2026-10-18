namespace LocalSendDotNet;

internal static class SafeFileTarget
{
    public static string ResolveUnique(string root, string requestedName, ISet<string>? reserved = null)
    {
        var rootFull = Path.GetFullPath(root);
        Directory.CreateDirectory(rootFull);
        if (Path.IsPathRooted(requestedName) || requestedName.Contains(':', StringComparison.Ordinal))
            throw new LocalSendException(CoreText.UnsafePathNamed(requestedName));

        var components = requestedName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0 || components.Any(static x => x is "." or ".."))
            throw new LocalSendException(CoreText.UnsafePathNamed(requestedName));

        var sanitized = components.Select(SanitizeComponent).ToArray();
        EnsureNoLinkedDirectories(rootFull, sanitized[..^1]);
        var candidate = Path.GetFullPath(Path.Combine([rootFull, .. sanitized]));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new LocalSendException(CoreText.PathEscapesNamed(requestedName));

        Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
        EnsureNoLinkedDirectories(rootFull, sanitized[..^1]);
        if (!File.Exists(candidate) && (reserved is null || reserved.Add(candidate)))
            return candidate;

        var directory = Path.GetDirectoryName(candidate)!;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var index = 1; ; index++)
        {
            var alternate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(alternate) && (reserved is null || reserved.Add(alternate)))
                return alternate;
        }
    }

    private static string SanitizeComponent(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim().TrimEnd('.', ' ');
        if (sanitized.Length == 0 || sanitized is "." or "..")
            throw new LocalSendException(CoreText.InvalidFilenameNamed(value));
        if (OperatingSystem.IsWindows())
        {
            var stem = sanitized.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 stem[3] is >= '1' and <= '9'))
                sanitized = "_" + sanitized;
        }
        return sanitized;
    }

    private static void EnsureNoLinkedDirectories(string root, IEnumerable<string> components)
    {
        var current = root;
        foreach (var component in components)
        {
            current = Path.Combine(current, component);
            if (!Directory.Exists(current))
                continue;
            var info = new DirectoryInfo(current);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new LocalSendException(CoreText.PathLinkedNamed(component));
        }
    }
}
