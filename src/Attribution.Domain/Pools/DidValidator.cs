using System.Text.RegularExpressions;

namespace Attribution.Domain.Pools;

// FR-002: a malformed CSV entry is one whose number field is empty, contains characters
// other than digits and an optional leading '+', or is not a syntactically valid E.164
// number (country code plus subscriber number, 8-15 digits total).
public static partial class DidValidator
{
    public static bool IsValidE164(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return E164Pattern().IsMatch(candidate);
    }

    [GeneratedRegex(@"^\+?[0-9]{8,15}$")]
    private static partial Regex E164Pattern();
}
