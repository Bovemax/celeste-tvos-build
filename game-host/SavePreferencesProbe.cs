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
    private const string Key = "CelesteSaveProbe.v1";
    private const int Budget = 384 * 1024;
    internal static string Run()
    {
        var id = NSBundle.MainBundle.BundleIdentifier;
        if (!RuntimeTestIdentity.IsAllowed(id))
            throw new InvalidOperationException("Not the isolated test application");
        var defaults = NSUserDefaults.StandardUserDefaults;
        var previous = defaults.StringForKey(Key);
        var fixture = Encoding.UTF8.GetBytes("<fixture>Celeste preferences probe</fixture>");
        if (previous != null)
        {
            var loaded = SaveSnapshot.Decode(previous);
            if (loaded.Count != 1 || !loaded.TryGetValue("probe.celeste", out var data) || !data.SequenceEqual(fixture))
                throw new InvalidDataException("Persistent fixture mismatch; no changes made");
            return "PASS: persistent save snapshot read on later launch.\nChecksum and contents verified.\nNo game saves accessed.";
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
        return "PASS: snapshot written and immediate read-back verified.\nClose this test app completely and reopen it to verify persistence.\nNo game saves accessed.";
    }
}
