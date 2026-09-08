using System.Net;

namespace TheAllocator.Services;

public static class PrinterPortAddressPolicy
{
    public static string ResolveRestoreHostAddress(string? savedHostAddress, string? portName)
    {
        if (!string.IsNullOrWhiteSpace(savedHostAddress))
        {
            return savedHostAddress.Trim();
        }

        return TryInferLegacyHostAddress(portName, out var inferred)
            ? inferred
            : string.Empty;
    }

    public static bool TryInferLegacyHostAddress(string? portName, out string hostAddress)
    {
        hostAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(portName))
        {
            return false;
        }

        var candidate = portName.Trim();
        if (candidate.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = candidate[3..];
            if (IPAddress.TryParse(suffix, out _))
            {
                hostAddress = suffix;
                return true;
            }

            return false;
        }

        if (IPAddress.TryParse(candidate, out _))
        {
            hostAddress = candidate;
            return true;
        }

        if (candidate.Contains(' ') || candidate.Contains('\\') || candidate.Contains('/'))
        {
            return false;
        }

        if (Uri.CheckHostName(candidate) == UriHostNameType.Dns)
        {
            hostAddress = candidate;
            return true;
        }

        return false;
    }
}
