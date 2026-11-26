using LocalGameHost;
using System.Security.Cryptography;
using System.Text;

int checks = 0;
void Assert(bool ok) { if (!ok) throw new Exception("Regression failed: " + checks); checks++; }
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
    { checks++; return; }
    throw new Exception("Invalid snapshot accepted");
}

var files = new Dictionary<string, byte[]> {
    ["0.celeste"] = Encoding.UTF8.GetBytes("<fixture>save</fixture>"),
    ["settings.celeste"] = Encoding.UTF8.GetBytes("<fixture>settings</fixture>")
};
var snapshot = SaveSnapshot.Encode(files);
var decoded = SaveSnapshot.Decode(snapshot);
Assert(decoded.Count == 2);
foreach (var pair in files) Assert(decoded[pair.Key].SequenceEqual(pair.Value));
Assert(snapshot == SaveSnapshot.Encode(files));
Assert(SaveSnapshot.Decode(SaveSnapshot.Encode(new Dictionary<string, byte[]>())).Count == 0);
Reject(() => SaveSnapshot.Decode(snapshot[..^1] + (snapshot[^1] == 'A' ? "B" : "A")));
Reject(() => SaveSnapshot.Decode(new string('A', SaveSnapshot.MaxEncoded + 1)));
Reject(() => SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["../0.celeste"] = new byte[] {1} }));
Reject(() => SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["0.celeste"] = Array.Empty<byte>() }));
Reject(() => SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["x.celeste"] = new byte[SaveSnapshot.MaxRaw + 1] }));
Reject(() => SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["0.celeste"] = new byte[]{1}, ["0.CELESTE"] = new byte[]{2} }));
Reject(() => SaveSnapshot.Encode(new Dictionary<string, byte[]> { ["0.celeste"] = RandomNumberGenerator.GetBytes(300000) }));
// Confirm that cache eviction is not confused with a deliberate deletion.
var empty = new Dictionary<string, byte[]>();
Assert(SavePolicy.Bootstrap(null, files) == snapshot);
Assert(SavePolicy.Bootstrap(snapshot, empty) == snapshot);
var missing = SavePolicy.Inspect(snapshot, empty);
Assert(missing.Missing.Length == 2 && missing.Conflicting.Length == 0);
var newer = new Dictionary<string, byte[]> { ["0.celeste"] = new byte[] { 3 } };
var conflict = SavePolicy.Inspect(snapshot, newer);
Assert(conflict.Conflicting.SequenceEqual(new[] { "0.celeste" }));
Assert(conflict.Missing.SequenceEqual(new[] { "settings.celeste" }));
Assert(SavePolicy.Inspect(snapshot, files).Identical.Length == 2);
var updated = SavePolicy.Saved(snapshot, "0.celeste", newer["0.celeste"]);
Assert(SaveSnapshot.Decode(updated)["0.celeste"].SequenceEqual(newer["0.celeste"]));
Assert(SaveSnapshot.Decode(updated)["settings.celeste"].SequenceEqual(files["settings.celeste"]));
var deleted = SavePolicy.Deleted(updated, "0.celeste");
Assert(!SaveSnapshot.Decode(deleted).ContainsKey("0.celeste"));
Assert(!SavePolicy.Inspect(deleted, empty).Missing.Contains("0.celeste"));
var allDeleted = SavePolicy.Deleted(deleted, "settings.celeste");
Assert(SaveSnapshot.Decode(allDeleted).Count == 0);
Assert(SavePolicy.Bootstrap(allDeleted, files) == allDeleted);
Reject(() => SavePolicy.Bootstrap("corrupt", files));
Reject(() => SavePolicy.Saved(snapshot, "../bad.celeste", new byte[] { 1 }));
Reject(() => SavePolicy.Deleted(snapshot, "../bad.celeste"));
Reject(() => SavePolicy.Saved(snapshot, "0.celeste", new byte[SaveSnapshot.MaxRaw + 1]));
Assert(SaveSnapshot.Decode(snapshot)["0.celeste"].SequenceEqual(files["0.celeste"]));
var recoveryDir = Path.Combine(Path.GetTempPath(), "celeste-recovery-" + Guid.NewGuid().ToString("N"));
try
{
    Assert(SaveRestore.TryRestoreMissing(snapshot, recoveryDir, "0.celeste"));
    var primary = Path.Combine(recoveryDir, "0.celeste");
    Assert(File.ReadAllBytes(primary).SequenceEqual(files["0.celeste"]));
    File.WriteAllBytes(primary, new byte[] { 9, 8, 7 });
    Assert(!SaveRestore.TryRestoreMissing(snapshot, recoveryDir, "0.celeste"));
    Assert(File.ReadAllBytes(primary).SequenceEqual(new byte[] { 9, 8, 7 }));
    File.Delete(primary);
    Assert(SaveRestore.TryRestoreMissing(snapshot, recoveryDir, "0.celeste"));
    Assert(File.ReadAllBytes(primary).SequenceEqual(files["0.celeste"]));
    Reject(() => SaveRestore.TryRestoreMissing(snapshot, recoveryDir, "../bad.celeste"));
    Reject(() => SaveRestore.TryRestoreMissing(deleted, recoveryDir, "0.celeste"));
    Assert(Directory.GetFiles(recoveryDir).Length == 1);
}
finally { if (Directory.Exists(recoveryDir)) Directory.Delete(recoveryDir, true); }
var migrationDir = Path.Combine(Path.GetTempPath(), "celeste-migration-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(migrationDir);
    var primary = Path.Combine(migrationDir, "0.celeste");
    File.WriteAllBytes(primary, files["0.celeste"]);
    var store = new FixtureStore();
    var coordinator = new SaveCoordinator(migrationDir, store);
    coordinator.Initialize();
    Assert(SaveSnapshot.Decode(store.Read()!)["0.celeste"].SequenceEqual(files["0.celeste"]));
    Assert(File.ReadAllBytes(primary).SequenceEqual(files["0.celeste"]));
    File.Delete(primary);
    new SaveCoordinator(migrationDir, store).Initialize();
    Assert(File.ReadAllBytes(primary).SequenceEqual(files["0.celeste"]));
    var newerBytes = new byte[] { 7, 8, 9 };
    File.WriteAllBytes(primary, newerBytes);
    coordinator.Saved("0", newerBytes);
    Assert(SaveSnapshot.Decode(store.Read()!)["0.celeste"].SequenceEqual(newerBytes));
    var saved = store.Read();
    store.Fail = true;
    try { coordinator.Saved("0", newerBytes); throw new Exception("Expected failed commit"); }
    catch (IOException) { checks++; }
    Assert(store.Read() == saved);
    Assert(!coordinator.Deleting("0"));
    Assert(File.Exists(primary));
    store.Fail = false;
    Assert(coordinator.Deleting("0"));
    File.Delete(primary);
    new SaveCoordinator(migrationDir, store).Initialize();
    Assert(!File.Exists(primary));
    // Simulate a failed disk deletion or untracked write: don't resurrect,
    // overwrite or accept a conflicting inventory silently.
    File.WriteAllBytes(primary, newerBytes);
    try { new SaveCoordinator(migrationDir, store).Initialize(); throw new Exception("Expected conflict"); }
    catch (IOException) { checks++; }
    Assert(File.ReadAllBytes(primary).SequenceEqual(newerBytes));
    Assert(SaveSnapshot.Decode(store.Read()!).Count == 0);
}
finally { if (Directory.Exists(migrationDir)) Directory.Delete(migrationDir, true); }
var probeDir = Path.Combine(Path.GetTempPath(), "celeste-real-probe-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(probeDir);
    var target = Path.Combine(probeDir, "0.celeste");
    var original = Path.Combine(probeDir, ".v10-recovery-original");
    File.WriteAllBytes(target, files["0.celeste"]);
    var store = new FixtureStore();
    var coordinator = new SaveCoordinator(probeDir, store);
    try { coordinator.TestRecoveryOnce(); throw new Exception("Expected absent backup refusal"); }
    catch (IOException) { checks++; }
    Assert(File.Exists(target) && !File.Exists(original));
    coordinator.Initialize();
    var prior = store.Read();
    File.WriteAllBytes(target, new byte[] { 7 });
    try { coordinator.TestRecoveryOnce(); throw new Exception("Expected conflicting backup refusal"); }
    catch (IOException) { checks++; }
    Assert(File.ReadAllBytes(target).SequenceEqual(new byte[] { 7 }) && !File.Exists(original));
    File.WriteAllBytes(target, files["0.celeste"]);
    var result = coordinator.TestRecoveryOnce();
    Assert(result.Contains("PASS: normal startup restored exact original bytes"));
    Assert(File.ReadAllBytes(target).SequenceEqual(files["0.celeste"]));
    Assert(File.ReadAllBytes(original).SequenceEqual(files["0.celeste"]));
    Assert(store.Read() == prior);
    try { coordinator.TestRecoveryOnce(); throw new Exception("Expected repeated probe refusal"); }
    catch (IOException) { checks++; }
    Assert(File.Exists(target) && File.Exists(original));
}
finally { if (Directory.Exists(probeDir)) Directory.Delete(probeDir, true); }
Console.WriteLine($"PASS: {checks} snapshot, recovery, save lifecycle and real-save probe regression checks");

sealed class FixtureStore : ISnapshotStore
{
    private string? value;
    public bool Fail;
    public string? Read() => value;
    public void Commit(string candidate)
    {
        if (Fail) throw new IOException("Simulated persistence failure");
        value = candidate;
    }
}
