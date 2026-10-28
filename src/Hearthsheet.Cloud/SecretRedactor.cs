using System.Text.RegularExpressions;

namespace Hearthsheet.Cloud;

/// <summary>
/// Last line of defense: scrubs anything shaped like a GitHub token, a JWT or an auth header from log lines.
/// The primary defense is that tokens are never passed to the logger in the first place.
/// </summary>
public static partial class SecretRedactor
{
    [GeneratedRegex(@"(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*|(?i:authorization\s*[:=]).*|(?i:bearer\s+)\S+|(?i:(password|token)""?\s*[:=]\s*)\S+)")]
    private static partial Regex SecretPattern();

    public static string Redact(string text) => SecretPattern().Replace(text, "[REDACTED]");
}
