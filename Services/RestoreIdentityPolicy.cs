namespace TheAllocator.Services;

public static class RestoreIdentityPolicy
{
    public static void EnsureSidMatches(string expectedSid, string resolvedSid, string context)
    {
        if (string.IsNullOrWhiteSpace(expectedSid) || string.IsNullOrWhiteSpace(resolvedSid))
        {
            throw new InvalidOperationException($"{context} SID validation could not be completed because a SID is missing.");
        }

        if (string.Equals(expectedSid, resolvedSid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{context} SID mismatch. Expected '{expectedSid}', but Windows resolved '{resolvedSid}'.");
    }
}
