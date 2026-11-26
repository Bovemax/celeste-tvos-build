using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LocalGameHost;

// Pure policy: no disk writes or tvOS API calls. Caller serializes save events,
// validates the game's successful write, and commits only a complete candidate.
internal static class SavePolicy
{
    internal static string Bootstrap(string? previous, IReadOnlyDictionary<string, byte[]> primary)
    {
        // A missing cache must not overwrite a previous durable snapshot.
        if (previous != null) { SaveSnapshot.Decode(previous); return previous; }
        return SaveSnapshot.Encode(primary);
    }

    internal static string Saved(string previous, string name, byte[] verifiedPrimary)
    {
        var files = SaveSnapshot.Decode(previous);
        // Canonical case is preserved from the dictionary's original key.
        // Encode separately validates the requested name and bytes.
        SaveSnapshot.Encode(new Dictionary<string, byte[]> { [name] = verifiedPrimary });
        files[name] = verifiedPrimary;
        return SaveSnapshot.Encode(files);
    }

    internal static string Deleted(string previous, string name)
    {
        // Only a confirmed explicit deletion, never absence on disk.
        SaveSnapshot.Encode(new Dictionary<string, byte[]> { [name] = new byte[] { 1 } });
        var files = SaveSnapshot.Decode(previous);
        files.Remove(name);
        return SaveSnapshot.Encode(files);
    }

    internal sealed record Recovery(string[] Missing, string[] Conflicting, string[] Identical);

    internal static Recovery Inspect(string previous, IReadOnlyDictionary<string, byte[]> primary)
    {
        var snapshot = SaveSnapshot.Decode(previous);
        // Validate incoming disk inventory and detect case aliases. Never write
        // files here. Caller must explicitly choose missing files and perform
        // create-new (not overwrite) at recovery time.
        SaveSnapshot.Encode(primary);
        var disk = new Dictionary<string, byte[]>(primary, StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var conflicting = new List<string>();
        var identical = new List<string>();
        foreach (var pair in snapshot.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!disk.TryGetValue(pair.Key, out var existing)) missing.Add(pair.Key);
            else if (existing.SequenceEqual(pair.Value)) identical.Add(pair.Key);
            else conflicting.Add(pair.Key);
        }
        return new Recovery(missing.ToArray(), conflicting.ToArray(), identical.ToArray());
    }
}
