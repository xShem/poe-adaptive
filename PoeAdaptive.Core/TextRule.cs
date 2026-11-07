using System.Text.RegularExpressions;

namespace PoeAdaptive.Core;

public sealed record TextRule(string PatchId, string AppId, string PathPrefix, string Extension,
    string Pattern, string Replacement, int MinMatches = 1, int MaxMatches = 100000,
    string EncodingName = "utf-16", GameId? Game = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public GameId TargetGame => Game ?? Games.FromLegacy(AppId);
}

public static class TextRules
{
    public static List<TextRule> Load(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        List<TextRule> rules;
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            rules = System.Text.Json.JsonSerializer.Deserialize<List<TextRule>>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("Пустые правила.");
            rules = rules.Select(r => r with { Game = Games.FromLegacy(r.AppId) }).ToList();
            foreach (var rule in rules) Compile(rule);
            // Preserve the exact legacy input; migration is inspectable and never destroys ambiguity.
            BuildStore.SaveJson(path + ".v2.json", new RuleDocument(2, rules));
        }
        else
        {
            var envelope = System.Text.Json.JsonSerializer.Deserialize<RuleDocument>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("Пустые правила.");
            if (envelope.SchemaVersion != 2) throw new InvalidDataException("Неизвестная версия rules.json; исходный файл сохранён.");
            rules = envelope.Rules ?? throw new InvalidDataException("Нет правил.");
            foreach (var rule in rules) Compile(rule);
        }
        return rules;
    }

    public static Regex Compile(TextRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.PatchId) || !Enum.IsDefined(rule.TargetGame)
            || (rule.Game != null && !string.IsNullOrEmpty(rule.AppId) && Games.FromLegacy(rule.AppId) != rule.Game)
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

public sealed record RuleDocument(int SchemaVersion, List<TextRule> Rules);
