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
Console.WriteLine($"PASS: {checks} snapshot codec regression checks");
