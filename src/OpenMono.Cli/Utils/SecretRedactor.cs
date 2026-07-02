using System.Text.RegularExpressions;

namespace OpenMono.Utils;

/// <summary>
/// Redacts secret-like tokens from user-provided text before it is stored in session history.
/// This does NOT secure secrets that were already disclosed elsewhere (e.g. pasted into GitHub issues).
/// </summary>
public static partial class SecretRedactor
{
    private const string Redacted = "[REDACTED_SECRET]";

    // OpenAI style keys: sk-... and sk-proj-...
    [GeneratedRegex(@"\bsk-(?:proj-)?[A-Za-z0-9_\-]{20,}\b", RegexOptions.Compiled)]
    private static partial Regex OpenAiKeyRegex();

    // Anthropic style keys often start with sk-ant-...
    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_\-]{20,}\b", RegexOptions.Compiled)]
    private static partial Regex AnthropicKeyRegex();

    // Generic bearer tokens (common for APIs). Keep conservative to avoid false positives.
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-_.=]{20,}\b", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    public static string RedactUserText(string input, out bool changed)
    {
        if (string.IsNullOrEmpty(input))
        {
            changed = false;
            return input;
        }

        var current = input;
        var before = current;

        current = OpenAiKeyRegex().Replace(current, Redacted);
        current = AnthropicKeyRegex().Replace(current, Redacted);
        current = BearerTokenRegex().Replace(current, "Bearer " + Redacted);

        changed = !ReferenceEquals(before, current) && !string.Equals(before, current, StringComparison.Ordinal);
        return current;
    }
}

