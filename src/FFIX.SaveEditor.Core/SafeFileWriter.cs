// SPDX-License-Identifier: MIT
namespace FFIX.SaveEditor.Core;

public static class SafeFileWriter
{
    public static void WriteNew(string inputPath, string outputPath, SaveDocument document)
    {
        if (SamePath(inputPath, outputPath))
            throw new IOException("Output path is the input file; use in-place writing so a backup is created.");
        AtomicWrite(outputPath, document.ToArray());
    }

    public static string WriteInPlaceWithBackup(string path, SaveDocument document)
    {
        var target = ResolveFinalLink(path);
        var backup = AvailableBackupPath(target);
        AtomicWrite(backup, File.ReadAllBytes(target));
        AtomicWrite(target, document.ToArray());
        return backup;
    }

    public static string AvailableBackupPath(string path)
    {
        if (!File.Exists(path + ".bak")) return path + ".bak";
        for (var index = 1; ; index++)
            if (!File.Exists(path + $".bak.{index}")) return path + $".bak.{index}";
    }

    internal static void AtomicWrite(string path, byte[] contents)
    {
        var destination = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Output has no parent directory.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException($"Output directory does not exist: {parent}");
        var temporary = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       128 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            if (!File.ReadAllBytes(temporary).AsSpan().SequenceEqual(contents))
                throw new IOException("Temporary-file verification failed; destination was not replaced.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool SamePath(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    private static string ResolveFinalLink(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        return info.LinkTarget is null ? info.FullName : info.ResolveLinkTarget(true)?.FullName ?? info.FullName;
    }
}
