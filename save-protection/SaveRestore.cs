using System;
using System.IO;
using System.Linq;

namespace LocalGameHost;

// Restore only a specifically selected missing primary file. The caller must
// serialize operations with the game save writer and decide when to recover.
internal static class SaveRestore
{
    internal static bool TryRestoreMissing(string snapshot, string directory, string name)
    {
        var files = SaveSnapshot.Decode(snapshot);
        var entry = files.FirstOrDefault(p => String.Equals(p.Key, name, StringComparison.Ordinal));
        if (entry.Key == null) throw new InvalidDataException("Save not present in snapshot");
        Directory.CreateDirectory(directory);
        if (Directory.EnumerateFileSystemEntries(directory)
            .Any(p => String.Equals(Path.GetFileName(p), entry.Key, StringComparison.OrdinalIgnoreCase)))
            return false;
        var target = Path.Combine(directory, entry.Key);
        var temporary = Path.Combine(directory, ".restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(entry.Value);
                stream.Flush(true);
            }
            // Same directory; no overwrite even if a primary appeared meanwhile.
            File.Move(temporary, target, overwrite: false);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
