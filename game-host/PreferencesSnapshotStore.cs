using System;
using System.IO;
using Foundation;

namespace LocalGameHost;

// Local-only preferences adapter. The on-device synthetic probe verified
// persistence across relaunch; immediate read-back alone is not a flush guarantee.
internal sealed class PreferencesSnapshotStore : ISnapshotStore
{
    private const string Key = "CelesteSaveSnapshot.v1";
    private const int Budget = 384 * 1024;
    private readonly NSUserDefaults defaults = NSUserDefaults.StandardUserDefaults;

    public string? Read() => defaults.StringForKey(Key);

    public void Commit(string candidate)
    {
        SaveSnapshot.Decode(candidate);
        var id = NSBundle.MainBundle.BundleIdentifier
            ?? throw new IOException("Missing application identity");
        using var domain = defaults.PersistentDomainForName(id) ?? new NSDictionary();
        using var proposed = (NSMutableDictionary)domain.MutableCopy();
        using var key = new NSString(Key);
        using var value = new NSString(candidate);
        // Use the public managed indexer; SetObject is not public in .NET tvOS.
        proposed[key] = value;
        using var serialized = NSPropertyListSerialization.DataWithPropertyList(proposed,
            (NSPropertyListFormat)100, (NSPropertyListWriteOptions)0, out var error);
        if (serialized == null || error != null)
            throw new IOException("Could not measure preferences domain: " + error);
        if ((ulong)serialized.Length + 4096UL > Budget)
            throw new IOException("Preferences budget exceeded; previous backup retained");
        defaults.SetString(candidate, Key);
        if (defaults.StringForKey(Key) != candidate)
            throw new IOException("Persistent snapshot read-back failed");
    }
}
