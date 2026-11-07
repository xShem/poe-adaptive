using System.Text.RegularExpressions;

namespace PoeAdaptive.Core;

public sealed record TextRule(string PatchId, string AppId, string PathPrefix, string Extension,
    string Pattern, string Replacement, int MinMatches = 1, int MaxMatches = 100000,
    string EncodingName = "utf-16");

public static class TextRules
{
    public static Regex Compile(TextRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.PatchId) || !SteamLocator.AppIds.Contains(rule.AppId)
            || string.IsNullOrWhiteSpace(rule.PathPrefix) || rule.PathPrefix.Contains("..")
            || !rule.Extension.StartsWith('.') || string.IsNullOrEmpty(rule.Pattern)
            || rule.MinMatches < 1 || rule.MaxMatches < rule.MinMatches
            || rule.EncodingName is not ("utf-16" or "utf-8" or "ascii"))
            throw new InvalidDataException("Некорректное правило совместимости.");
        return new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    public static bool Targets(TextRule rule, string path) =>
        path.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(rule.Extension, StringComparison.OrdinalIgnoreCase);

    public static (string Text, int Matches) Transform(Regex regex, TextRule rule, string text)
    {
        var count = regex.Matches(text).Count;
        return (count == 0 ? text : regex.Replace(text, rule.Replacement), count);
    }
}
