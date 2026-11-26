using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foundation;

namespace LocalGameHost;

// Test only, using the previously verified separate application identity.
// This never accesses the game's save paths or executes game code.
internal static class SavePreferencesProbe
{
    private const string Key = "CelesteSaveProbe.v2";
    private const int Budget = 384 * 1024;
    internal static string Run()
    {
        var id = NSBundle.MainBundle.BundleIdentifier;
        if (!RuntimeTestIdentity.IsAllowed(id))
            throw new InvalidOperationException("Not the isolated test application");
        var defaults = NSUserDefaults.StandardUserDefaults;
        var previous = defaults.StringForKey(Key);
        var fixture = Encoding.UTF8.GetBytes("<fixture>Celeste preferences probe</fixture>");
        var home = Environment.GetEnvironmentVariable("HOME");
        if (String.IsNullOrEmpty(home)) throw new IOException("No application home");
        var directory = Path.Combine(home, "Library", "Caches", "CelesteSyntheticRecovery-v2");
        var path = Path.Combine(directory, "probe.celeste");
        if (previous != null)
        {
            var loaded = SaveSnapshot.Decode(previous);
            if (loaded.Count != 1 || !loaded.TryGetValue("probe.celeste", out var data) || !data.SequenceEqual(fixture))
                throw new InvalidDataException("Persistent fixture mismatch; no changes made");
            if (File.Exists(path))
            {
                // Only a previously recovered, byte-identical fixture may be
                // removed on repeated runs. Never remove a conflicting file.
                if (!File.ReadAllBytes(path).SequenceEqual(fixture))
                    throw new InvalidDataException("Unexpected existing fixture; left unchanged");
                File.Delete(path);
            }
            if (File.Exists(path)) throw new IOException("Fixture still exists");
            if (!SaveRestore.TryRestoreMissing(previous, directory, "probe.celeste")
                || !File.ReadAllBytes(path).SequenceEqual(fixture))
                throw new IOException("Restored fixture mismatch");
            if (SaveRestore.TryRestoreMissing(previous, directory, "probe.celeste"))
                throw new IOException("Existing file overwrite guard failed");
            if (!File.ReadAllBytes(path).SequenceEqual(fixture))
                throw new IOException("Existing fixture changed");
            return "PASS: snapshot survived relaunch.\nPASS: missing cached save restored byte-for-byte.\nPASS: existing file not overwritten.\nSynthetic data only; no game saves accessed.";
        }
        var candidate = SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["probe.celeste"] = fixture });
        // Measure the existing persistent domain as XML, then conservatively
        // allow the entire ASCII candidate plus extra plist overhead.
        // This is an estimate with margin, not an exact database size guarantee.
        using var domain = defaults.PersistentDomainForName(id!) ?? new NSDictionary();
        using var serialized = NSPropertyListSerialization.DataWithPropertyList(domain,
            (NSPropertyListFormat)100, (NSPropertyListWriteOptions)0, out var error);
        if (serialized == null || error != null)
            throw new IOException("Could not measure preferences domain: " + error);
        if ((ulong)serialized.Length + (ulong)candidate.Length + 4096UL > Budget)
            throw new IOException("Preferences budget exceeded; nothing written");
        defaults.SetString(candidate, Key);
        if (defaults.StringForKey(Key) != candidate)
            throw new IOException("Preferences immediate read-back failed");
        // Exercise loss of just our own fixture, not actual game caches.
        if (!SaveRestore.TryRestoreMissing(candidate, directory, "probe.celeste"))
            throw new IOException("Synthetic fixture already exists; left unchanged");
        if (!File.ReadAllBytes(path).SequenceEqual(fixture))
            throw new IOException("Synthetic fixture mismatch");
        File.Delete(path);
        if (File.Exists(path)) throw new IOException("Fixture still exists");
        return "PASS: snapshot written; cached test save deleted.\nClose this test app completely and reopen it.\nNo game saves accessed.";
    }
}
