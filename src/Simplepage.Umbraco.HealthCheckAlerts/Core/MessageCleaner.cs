using System.Net;
using System.Text.RegularExpressions;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// Health check messages are written for a web page, so they can contain HTML tags and entities.
/// This reduces one to plain text. The formatter HTML-encodes the result again, so nothing a check
/// returns can become markup in the email.
/// </summary>
public static class MessageCleaner
{
    /// <summary>Longest message kept. Umbraco's longest built-in messages are well under this.</summary>
    public const int MaxLength = 1000;

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(no detail given)";
        }

        // Tags first, then entities: decoding first would turn &lt;b&gt; into a tag and strip it.
        var text = Tags.Replace(message, " ");
        text = WebUtility.HtmlDecode(text);
        text = Whitespace.Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return "(no detail given)";
        }

        return text.Length <= MaxLength ? text : text[..MaxLength].TrimEnd() + " (truncated)";
    }
}
