using System;

namespace LocalGameHost;

internal static class RuntimeTestIdentity
{
    internal const string BaseId = "org.example.celestelocalhost.runtimetest";

    // The signer can append a single ten-character uppercase alphanumeric
    // component. Never accept the normal game ID or unrelated IDs.
    internal static bool IsAllowed(string? id)
    {
        if (id == BaseId) return true;
        if (id == null || !id.StartsWith(BaseId + ".", StringComparison.Ordinal)
            || id.Length != BaseId.Length + 11) return false;
        foreach (char c in id.AsSpan(BaseId.Length + 1))
            if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
        return true;
    }
}
