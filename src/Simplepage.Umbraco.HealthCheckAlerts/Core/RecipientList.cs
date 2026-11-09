using System.Net.Mail;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// Reads the RecipientEmail setting: one or more addresses separated by commas, semicolons,
/// spaces or new lines.
/// </summary>
public static class RecipientList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Splits and validates. Duplicates are dropped, ignoring case, keeping the first spelling.
    /// Anything that is not a plain address (no display names, no groups) is returned as invalid
    /// so the caller can log it. One bad address never stops the rest from being used.
    /// </summary>
    public static (IReadOnlyList<string> Valid, IReadOnlyList<string> Invalid) Parse(string? raw)
    {
        var valid = new List<string>();
        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in (raw ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsPlainAddress(token))
            {
                invalid.Add(token);
            }
            else if (seen.Add(token))
            {
                valid.Add(token);
            }
        }

        return (valid, invalid);
    }

    private static bool IsPlainAddress(string token)
    {
        // MailAddress alone accepts "Name <a@b.com>" and similar. Requiring the parsed address to
        // equal the token means only a bare address passes.
        return MailAddress.TryCreate(token, out var address)
               && string.Equals(address.Address, token, StringComparison.OrdinalIgnoreCase)
               && address.Host.Length > 0;
    }
}
