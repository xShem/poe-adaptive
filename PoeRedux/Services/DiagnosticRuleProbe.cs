using System.Text;
using PoeAdaptive.Core;

namespace PoeRedux.Services;

public sealed record DiagnosticRuleProbeResult(int RuleIndex, int? Matches, bool? Changed, string? Error);

/// <summary>Per-resource samples, not a full-scope MinMatches/MaxMatches verdict. Never writes resources.</summary>
public static class DiagnosticRuleProbe
{
    public static DiagnosticRuleProbeResult[] Evaluate(string path, byte[] original, IReadOnlyList<TextRule> rules)
    {
        var bytes = original;
        var results = new List<DiagnosticRuleProbeResult>();
        for (int n = 0; n < rules.Count; n++)
        {
            var rule = rules[n];
            if (!TextRules.Targets(rule, path)) continue;
            try
            {
                var encoding = rule.EncodingName switch { "ascii" => Encoding.ASCII, "utf-8" => Encoding.UTF8, _ => Encoding.Unicode };
                var (next, matches) = TextRules.Transform(TextRules.Compile(rule), rule, encoding.GetString(bytes));
                var after = encoding.GetBytes(next);
                results.Add(new(n, matches, !bytes.AsSpan().SequenceEqual(after), null));
                bytes = after;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                results.Add(new(n, null, null, ex.GetType().Name));
                break; // Subsequent rules cannot be evaluated on an unknown intermediate result.
            }
        }
        return results.ToArray();
    }
}
