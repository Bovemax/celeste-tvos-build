using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LocalGameHost;

// Platform-independent coordination. All calls are serialized. Store must
// either commit the complete new value or report failure; never clear old data.
internal interface ISnapshotStore
{
    string? Read();
    void Commit(string candidate);
}

internal sealed class SaveCoordinator
{
    private readonly string directory;
    private readonly ISnapshotStore store;
    private readonly object gate = new();
    private bool ready;

    internal SaveCoordinator(string directory, ISnapshotStore store)
    {
        this.directory = directory;
        this.store = store;
    }

    internal void Initialize()
    {
        lock (gate)
        {
            var primary = Inventory();
            var previous = store.Read();
            if (previous == null)
                store.Commit(SaveSnapshot.Encode(primary));
            else
            {
                var backup = SaveSnapshot.Decode(previous);
                var plan = SavePolicy.Inspect(previous, primary);
                // Disk-only files may be unsynchronized writes. Do not silently
                // replace either version or restore other files into a mixed set.
                if (plan.Conflicting.Length != 0 || primary.Keys.Any(k => !backup.ContainsKey(k)))
                    throw new IOException("Save backup differs from cached saves. Both copies preserved; recovery needs review.");
                foreach (var name in plan.Missing)
                    if (!SaveRestore.TryRestoreMissing(previous, directory, name))
                        throw new IOException("Recovery target appeared; existing save preserved.");
            }
            ready = true;
        }
    }

    internal void Saved(string slot, byte[] expected)
    {
        lock (gate)
        {
            var previous = RequireSnapshot();
            string name = slot + ".celeste";
            // Validate slot before resolving a filesystem path.
            var candidate = SavePolicy.Saved(previous, name, expected);
            var actual = ReadBounded(Path.Combine(directory, name));
            if (!actual.SequenceEqual(expected))
                throw new IOException("Primary save does not match validated game bytes");
            store.Commit(candidate);
        }
    }

    // Called BEFORE the game's explicit primary deletion. If the durable
    // update fails, refuse deletion. If the subsequent disk deletion fails,
    // startup detects the disk-only file and preserves it for review.
    internal bool Deleting(string slot)
    {
        lock (gate)
        {
            try
            {
                var previous = RequireSnapshot();
                store.Commit(SavePolicy.Deleted(previous, slot + ".celeste"));
                return true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Persistent deletion refused: " + e.Message);
                return false;
            }
        }
    }

    private string RequireSnapshot()
    {
        if (!ready) throw new InvalidOperationException("Save protection not initialized");
        return store.Read() ?? throw new IOException("Persistent snapshot missing");
    }

    private Dictionary<string, byte[]> Inventory()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return result;
        int total = 0;
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            if (!path.EndsWith(".celeste", StringComparison.OrdinalIgnoreCase)) continue;
            var data = ReadBounded(path);
            total = checked(total + data.Length);
            if (total > SaveSnapshot.MaxRaw || result.Count >= 64)
                throw new IOException("Save inventory exceeds backup limits");
            if (!result.TryAdd(Path.GetFileName(path), data))
                throw new IOException("Duplicate save names");
        }
        return result;
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > SaveSnapshot.MaxRaw)
            throw new IOException("Empty or oversized save file");
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        if (stream.ReadByte() != -1) throw new IOException("Save changed during read");
        return data;
    }
}
