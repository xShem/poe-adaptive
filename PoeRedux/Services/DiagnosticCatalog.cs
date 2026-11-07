using PoeAdaptive.Core;
using System.Text;

namespace PoeRedux.Services;

/// <summary>Inspectable resource requirements. A matching path is a rule target, not proof that a patch works.</summary>
public sealed record DiagnosticTarget(string Prefix, string Extension, string? ExactPath = null,
    string[]? ExpectedText = null, bool Required = false, string Source = "builtin")
{
    public bool Matches(string path) =>
        (ExactPath is null || path.Equals(ExactPath, StringComparison.OrdinalIgnoreCase)) &&
        path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}

public sealed record DiagnosticDiscovery(string Feature, DiagnosticTarget Target, int Matched,
    string[] Paths, string[] Candidates, string Status);

public static class DiagnosticCatalog
{
    private static DiagnosticTarget T(string prefix, string ext, string? exact = null, bool required = false, params string[] signs) =>
        new(prefix, ext, exact, signs, required);

    /// <summary>In the analyzer, JSON rules replace the corresponding built-in patch, in their original order.</summary>
    public static DiagnosticTarget[] For(GameId game, string feature, IEnumerable<TextRule> rules)
    {
        var overrides = rules.Where(r => r.TargetGame == game && r.PatchId == feature).ToArray();
        if (overrides.Length > 0)
            return overrides.Select((r, n) => new DiagnosticTarget(r.PathPrefix, r.Extension, null,
                [r.Pattern], true, "rules.json:" + n)).ToArray();
        var env = T("metadata/environmentsettings/", ".env");
        return feature switch
        {
            "Camera" => [T("metadata/characters/", ".ot", "metadata/characters/character.ot", true, "team = 1", "CreateCameraZoomNode"),
                T("metadata/", ".ot", null, false, "CreateCameraZoomNode"), T("metadata/", ".otc", null, false, "CreateCameraZoomNode")],
            "MonsterHP" => [T("metadata/monsters/", ".ot", "metadata/monsters/monster.ot", true, "item_drop_slots = 1")],
            "Corpse" => [T("metadata/monsters/", ".ot", "metadata/monsters/monster.ot", true, "Life")],
            "Minimap" => [T("shaders/", ".hlsl", "shaders/minimap_visibility_pixel.hlsl", true, "res_color"),
                T("shaders/", ".hlsl", "shaders/minimap_blending_pixel.hlsl", true, "walkable_color")],
            "AtlasFog" => [T("metadata/materials/environment/worldmap/", ".fxgraph",
                "metadata/materials/environment/worldmap/worldmap_fogofwar.fxgraph", true, "nodes", "links")],
            "ColorMods" when game == GameId.PoE1 => [T("metadata/", ".txt", null, false, "description")],
            "ColorMods2" => [T("metadata/", ".csd", null, false, "description")],
            "Fog" => [env with { ExpectedText = ["\"fog\""] }],
            "Light" => [env with { ExpectedText = ["\"directional_light\""] }],
            "Shadow" => [env with { ExpectedText = ["\"shadows_enabled\""] }],
            "EnvironmentParticles" or "EnvironmentParticles2" => [env with { ExpectedText = ["\"area\"", "\"rain_intensity\"", "\"clouds_intensity\""] }],
            "Rain" => [env with { ExpectedText = ["\"rain_intensity\""] }],
            "Clouds" => [env with { ExpectedText = ["\"clouds_intensity\""] }],
            "Delirium" => [T("metadata/effects/environment/league_affliction/", ".ao", null, false, "Metadata/"),
                T("metadata/effects/environment/league_affliction/", ".aoc", null, false, "Metadata/")],
            "Particles" => [T("metadata/particles/", ".pet")],
            "Effects" => [T("", ".ao"), T("", ".aoc")],
            "BlackScreen" => [T("", ".aoc"), T("", ".ao"), T("", ".env"), T("", ".epk"),
                T("", ".ffx"), T("", ".hlsl"), T("", ".mat"), T("", ".pet")],
            _ => throw new InvalidDataException("Неизвестная функция диагностики: " + feature)
        };
    }

    /// <summary>Index-only relocation search. Suggestions are never silently promoted to confirmed targets.</summary>
    public static DiagnosticDiscovery Search(string feature, DiagnosticTarget target, IEnumerable<string> paths,
        int maxListed = 12)
    {
        var all = paths.Where(p => p is not null).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var matching = all.Where(target.Matches).ToArray();
        var knownName = target.ExactPath is null ? null : Path.GetFileName(target.ExactPath);
        var prefixPieces = target.Prefix.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidates = all.Where(p => !target.Matches(p) && p.EndsWith(target.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: p, Score: (knownName is not null && Path.GetFileName(p).Equals(knownName, StringComparison.OrdinalIgnoreCase) ? 100 : 0)
                + prefixPieces.Count(part => p.Contains(part, StringComparison.OrdinalIgnoreCase)) * 10))
            .Where(x => x.Score >= (knownName is null ? 10 : 20))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxListed).Select(x => x.Path).ToArray();
        return new(feature, target, matching.Length, matching.Take(maxListed).ToArray(), candidates,
            matching.Length == 0 ? "target_missing; relocation_candidates_unconfirmed" : "path_matched; content_unverified");
    }

    public static (string? Text, string EncodingName) DecodeText(ReadOnlySpan<byte> data, string path)
    {
        // Avoid turning binary payloads into fictional text. UTF-16 is the default for metadata assets.
        var known = new[] { ".ot", ".otc", ".env", ".txt", ".csd", ".ao", ".aoc", ".pet", ".epk",
            ".ffx", ".mat", ".hlsl", ".fxgraph" };
        if (!known.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return (null, "binary");
        if (data.StartsWith(new byte[] { 0xff, 0xfe })) return (Encoding.Unicode.GetString(data[2..]), "utf-16le-bom");
        if (data.StartsWith(new byte[] { 0xfe, 0xff })) return (Encoding.BigEndianUnicode.GetString(data[2..]), "utf-16be-bom");
        if (data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (StrictUtf8(data[3..]), "utf-8-bom");
        if (data.Length >= 4 && data[1] == 0 && data[3] == 0)
            return (Encoding.Unicode.GetString(data), "utf-16le");
        if (data.IndexOf((byte)0) >= 0) return (null, "binary-or-unsupported-encoding");
        var utf8 = StrictUtf8(data);
        return utf8 is null ? (null, "binary-or-unsupported-encoding") : (utf8, "utf-8");
    }

    private static string? StrictUtf8(ReadOnlySpan<byte> bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }

    public static string[] EvaluateSigns(string? text, DiagnosticTarget target)
    {
        if (target.ExpectedText is not { Length: > 0 }) return [];
        return target.ExpectedText.Select(sign =>
        {
            var found = text is not null && (target.Source.StartsWith("rules.json:", StringComparison.Ordinal)
                ? SafeRegex(sign, text) : text.Contains(sign, StringComparison.Ordinal));
            return (found ? "found:" : "missing:") + sign;
        }).ToArray();
    }

    private static bool SafeRegex(string expression, string input)
    {
        try { return System.Text.RegularExpressions.Regex.IsMatch(input, expression,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }
}