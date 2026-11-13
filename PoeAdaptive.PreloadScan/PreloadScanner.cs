using System.Buffers.Binary;
using System.Text;
using LibGGPK3;
using LibGGPK3.Records;

namespace PoeAdaptive.PreloadScan;

public sealed record ScanEvidence(string Kind, string ArchivePath, string Indicator, string? Excerpt, bool PartialRead);
public sealed record FeatureFinding(string Feature, string Verdict, int TextMatches, int PathCandidates,
    ScanEvidence[] Evidence);
public sealed record ScanFileType(string Type, long Files, long Bytes);
public sealed record ScanRootGroup(string Root, long Files, long Bytes);
public sealed record PreloadReport(int SchemaVersion, string ScannerVersion, DateTimeOffset CheckedUtc,
    string FileName, long FileSize, uint GgpkVersion, bool Bundles2Present,
    bool Bundles2IndexPresent, string[] RootEntries, long NodesVisited, long DirectFilesFound,
    int DirectFilesSampled, long BytesSampled, bool Truncated,
    FeatureFinding[] Features, ScanFileType[] FileTypes, ScanRootGroup[] RootGroups,
    long BundleFilesFound, long TextFileCandidates, string[] IndexCandidates,
    string[] Warnings, string Summary)
{
    public DeepBundleReport? DeepBundles { get; init; }
}

/// <summary>
/// A bounded, strictly read-only search of ordinary GGPK directory files.
/// It DOES NOT decompress Bundles2 / Oodle payloads or validate a game's patch compatibility.
/// </summary>
public static class PreloadScanner
{
    public const int MaxVisitedNodes = 100_000;
    public const int MaxDepth = 24;
    public const int MaxReadPerFile = 256 * 1024;
    public const int MaxReadFiles = 512;
    public const int MaxSampleBytes = 24 * 1024 * 1024;
    public const int MaxEvidencePerFeature = 12;

    private sealed record Rule(string Name, string[] PathHints, string[] ContentSigns, string[] Extensions);
    private sealed record Candidate(FileRecord File, string Path, int Priority);
    private static readonly string[] ScriptFiles =
        [".ot", ".otc", ".env", ".hlsl", ".fxgraph", ".txt", ".csd", ".ao", ".aoc", ".mat", ".ffx", ".epk", ".pet", ".json"];

    // Indicators are obtained from patch code in PoeRedux/Patches and Patches2.
    // These are heuristics, not a contract with GGG's file format.
    private static readonly Rule[] Rules =
    [
        new("Camera", ["metadata/characters/character.ot"], ["CreateCameraZoomNode", "ClearCameraZoomNodes", "CreateCameraLookAtNode"], [".ot", ".otc"]),
        new("MonsterHP", ["metadata/monsters/monster.ot"], ["base_maximum_life", "item_drop_slots"], [".ot", ".otc"]),
        new("Corpse", ["metadata/monsters/monster.ot"], ["on_spawned_dead", "on_death", "DisableRendering"], [".ot", ".otc"]),
        new("Minimap", ["minimap_visibility_pixel.hlsl", "minimap_blending_pixel.hlsl"], ["walkable_color", "res_color = float4"], [".hlsl"]),
        new("ColorMods", [], ["map_monsters_reflect_%_physical_damage", "map_item_drop_quantity_+%"], [".txt"]),
        new("ColorMods2", [], ["map_monsters_%_all_damage_to_gain_as_fire", "map_pack_size_+%"], [".csd"]),
        new("Fog", ["metadata/environmentsettings/"], ["\"fog\""], [".env"]),
        new("Shadow", ["metadata/environmentsettings/"], ["\"shadows_enabled\""], [".env"]),
        new("Light", ["metadata/environmentsettings/"], ["\"directional_light\""], [".env"]),
        new("EnvironmentParticles", ["metadata/environmentsettings/"], ["\"effect_spawner\"", "\"area\""], [".env"]),
        new("EnvironmentParticles2", ["metadata/environmentsettings/"], ["\"rain_intensity\"", "\"clouds_intensity\""], [".env"]),
        new("Rain", ["metadata/environmentsettings/"], ["\"rain_intensity\""], [".env"]),
        new("Clouds", ["metadata/environmentsettings/"], ["\"clouds_intensity\""], [".env"]),
        new("AtlasFog", ["metadata/materials/environment/worldmap/worldmap_fogofwar.fxgraph"], ["fogofwar"], [".fxgraph"]),
        new("Delirium", ["metadata/effects/environment/league_affliction/"], ["AnimatedRender", "Metadata/FmtParent"], [".ao", ".aoc"]),
        new("Particles", ["metadata/particles/"], [], [".pet"]),
        new("Effects", ["metadata/effects/"], ["AnimatedRender", "ClientAnimationController"], [".ao", ".aoc"]),
        new("BlackScreen", ["metadata/materials/", "shaders/"], ["\"post_processing\"", "\"global_illumination\""], [".env", ".mat", ".hlsl", ".ffx", ".epk"])
    ];

    public static PreloadReport Scan(string path, Action<string>? progress = null)
    {
        path = Path.GetFullPath(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var originalLength = source.Length;
        var originalTime = File.GetLastWriteTimeUtc(path);
        if (source.Length < 12) throw new InvalidDataException("GGPK слишком короткий.");
        Span<byte> header = stackalloc byte[12];
        source.ReadExactly(header);
        if (!header[4..8].SequenceEqual("GGPK"u8))
            throw new InvalidDataException("Нет сигнатуры GGPK в заголовке.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (version is not (3 or 4))
            throw new InvalidDataException("GGPK v" + version + ": сканер поддерживает только контейнеры v3/v4.");
        source.Position = 0;

        using var ggpk = new GGPK(source, true);
        var bundleRoot = ggpk.Root["Bundles2"] as DirectoryRecord;
        bool hasBundleIndex = bundleRoot?["_.index.bin"] is FileRecord;
        var rootNames = Enumerable.Range(0, Math.Min(ggpk.Root.Count, 24))
            .Select(n => Safe(ggpk.Root[n].Name, 96) + (ggpk.Root[n] is DirectoryRecord ? "/" : "")).ToArray();

        var evidence = Rules.ToDictionary(r => r.Name, _ => new List<ScanEvidence>(), StringComparer.Ordinal);
        var textCounts = Rules.ToDictionary(r => r.Name, _ => 0, StringComparer.Ordinal);
        var pathCounts = Rules.ToDictionary(r => r.Name, _ => 0, StringComparer.Ordinal);
        var candidates = new List<Candidate>();
        var warnings = new List<string>();
        var fileTypes = new Dictionary<string, (long Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var rootGroups = new Dictionary<string, (long Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var indexCandidates = new List<string>();
        long bundleFileCount = 0, eligibleTextCount = 0;
        long visited = 0, totalFiles = 0;
        bool truncated = false;
        var stack = new Stack<(TreeNode Node, int Depth)>();
        stack.Push((ggpk.Root, 0));
        progress?.Invoke("Чтение каталога GGPK (только чтение)...");

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (++visited > MaxVisitedNodes)
            {
                truncated = true;
                warnings.Add("Ограничение каталога: просмотрено не более 100 000 записей.");
                break;
            }
            if (node is DirectoryRecord directory)
            {
                if (depth >= MaxDepth)
                {
                    truncated = true;
                    warnings.Add("Превышена максимальная глубина каталогов.");
                    continue;
                }
                // The stack itself is bounded too: malformed archives may contain enormous directories.
                if (stack.Count + directory.Count > MaxVisitedNodes)
                {
                    truncated = true;
                    warnings.Add("Слишком большой каталог: дополнительные записи не добавлены в очередь.");
                    continue;
                }
                // Reverse for deterministic natural directory order with a LIFO stack.
                for (var i = directory.Count - 1; i >= 0; --i)
                    stack.Push((directory[i], depth + 1));
                continue;
            }
            if (node is not FileRecord file) continue;
            totalFiles++;
            var fullPath = file.GetPath().Replace('\\', '/');
            var relative = Safe(fullPath, 260);
            var type = fullPath.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase)
                ? ".bundle.bin" : Path.GetExtension(fullPath);
            if (string.IsNullOrEmpty(type)) type = "(no extension)";
            var previousType = fileTypes.GetValueOrDefault(type);
            fileTypes[type] = (previousType.Count + 1, previousType.Bytes + file.DataLength);
            var slash = fullPath.IndexOf('/');
            var root = slash < 0 ? "(root)" : fullPath[..slash] + "/";
            var previousGroup = rootGroups.GetValueOrDefault(root);
            rootGroups[root] = (previousGroup.Count + 1, previousGroup.Bytes + file.DataLength);
            if (type.Equals(".bundle.bin", StringComparison.OrdinalIgnoreCase)) bundleFileCount++;
            if (file.Name.Equals("_.index.bin", StringComparison.OrdinalIgnoreCase)
                || file.Name.Equals("index.bin", StringComparison.OrdinalIgnoreCase))
            {
                if (indexCandidates.Count < 20) indexCandidates.Add(relative);
            }
            foreach (var rule in Rules)
            {
                if (!rule.Extensions.Any(ext => fullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    continue; // A compressed .bundle.bin is never evidence that the patch code exists.
                var pathIndicator = rule.PathHints.FirstOrDefault(h => MatchesPathHint(fullPath, h));
                if (pathIndicator is null) continue;
                pathCounts[rule.Name]++;
                if (evidence[rule.Name].Count < MaxEvidencePerFeature)
                    evidence[rule.Name].Add(new("path-candidate", relative, pathIndicator, null, false));
            }
            if (ScriptFiles.Any(ext => fullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                eligibleTextCount++;
                var priority = Rules.Any(r => r.PathHints.Any(h => MatchesPathHint(fullPath, h)))
                    ? 2 : 1;
                // Do not accumulate an unlimited list of files from a huge preload.
                if (candidates.Count < 12_000) candidates.Add(new(file, relative, priority));
                else truncated = true;
            }
            if (visited % 5000 == 0) progress?.Invoke("Найдено записей: " + visited);
        }

        int sampled = 0;
        long sampledBytes = 0;
        progress?.Invoke("Поиск текстовых признаков функций (без распаковки bundles)...");
        foreach (var item in candidates.OrderByDescending(x => x.Priority)
                     .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (sampled >= MaxReadFiles || sampledBytes >= MaxSampleBytes)
            {
                truncated = true;
                break;
            }
            if (item.File.DataLength < 1) continue;
            var count = Math.Min(item.File.DataLength, MaxReadPerFile);
            if (count > MaxSampleBytes - sampledBytes)
            {
                truncated = true;
                break;
            }
            var bytes = new byte[count];
            item.File.Read(bytes);
            sampled++;
            sampledBytes += count;
            var content = DecodeText(bytes);
            if (content is null) continue;
            var partial = item.File.DataLength > bytes.Length;
            foreach (var rule in Rules)
            {
                if (!rule.Extensions.Any(ext => item.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (var token in rule.ContentSigns)
                {
                    var index = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    textCounts[rule.Name]++;
                    if (evidence[rule.Name].Count < MaxEvidencePerFeature)
                        evidence[rule.Name].Add(new("text-indicator", item.Path, token,
                            Excerpt(content, index, token.Length), partial));
                }
            }
        }

        if (candidates.Count >= 12_000)
            warnings.Add("Ограничение кандидатов: оценено не более 12 000 файлов текстовых типов.");
        if (sampled >= MaxReadFiles || sampledBytes >= MaxSampleBytes)
            warnings.Add("Достигнут предел чтения: 512 файлов или 24 МиБ.");
        if (!hasBundleIndex)
            warnings.Add("Bundles2/_.index.bin не найден. Это не доказательство отсутствия данных будущей лиги.");
        if (eligibleTextCount == 0)
            warnings.Add("Нет ни одного прямого файла с проверяемым текстовым расширением; проверка содержимого функций не проводилась.");
        if (bundleFileCount > 0)
            warnings.Add("Найдены контейнеры .bundle.bin (" + bundleFileCount + "), но их содержимое не расшифровывалось и не распаковывалось.");
        warnings.Add("Содержимое упакованных bundles и Oodle не декодируется: совпадения относятся только к обычным файлам каталога GGPK.");
        warnings.Add("Совпадение названия файла или текстового индикатора НЕ доказывает работу патча после релиза.");
        if (source.Length != originalLength || File.GetLastWriteTimeUtc(path) != originalTime)
            throw new IOException("GGPK изменился во время сканирования. Отчёт не сохранён.");

        var features = Rules.Select(rule =>
        {
            var t = textCounts[rule.Name];
            var p = pathCounts[rule.Name];
            return new FeatureFinding(rule.Name,
                t > 0 ? "text-indicators-found" : p > 0 ? "path-candidates-only" :
                    sampled == 0 ? "not-inspected-no-readable-text-assets" : "no-evidence-in-scanned-direct-files",
                t, p, evidence[rule.Name].ToArray());
        }).ToArray();

        var types = fileTypes.Select(kv => new ScanFileType(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderByDescending(v => v.Files).ThenBy(v => v.Type, StringComparer.OrdinalIgnoreCase)
            .Take(20).ToArray();
        var groups = rootGroups.Select(kv => new ScanRootGroup(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderByDescending(v => v.Files).ThenBy(v => v.Root, StringComparer.OrdinalIgnoreCase)
            .Take(16).ToArray();
        return new(2, "0.2.1", DateTimeOffset.UtcNow, Path.GetFileName(path),
            originalLength, version, bundleRoot is not null, hasBundleIndex, rootNames,
            visited, totalFiles, sampled, sampledBytes, truncated, features,
            types, groups, bundleFileCount, eligibleTextCount, indexCandidates.ToArray(),
            warnings.Distinct().ToArray(), "Поиск признаков в файлах GGPK, не проверка совместимости патчей. " +
                "Отсутствие совпадений не означает отсутствия нужного кода в упакованных данных.");
    }

    /// <summary>Hints must begin at a path-segment boundary; cachedhlslshaders/ is not shaders/.</summary>
    public static bool MatchesPathHint(string path, string hint)
    {
        if (string.IsNullOrEmpty(hint)) return false;
        var index = path.IndexOf(hint, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || path[index - 1] == '/';
            var end = index + hint.Length;
            var after = hint.EndsWith('/') || end == path.Length || path[end] == '/';
            if (before && after) return true;
            index = path.IndexOf(hint, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string? DecodeText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return null;
        if (data.StartsWith(new byte[] { 0xff, 0xfe }))
            return Encoding.Unicode.GetString(data[2..(data.Length - (data.Length - 2) % 2)]);
        if (data.StartsWith(new byte[] { 0xfe, 0xff }))
            return Encoding.BigEndianUnicode.GetString(data[2..(data.Length - (data.Length - 2) % 2)]);
        if (data.Length >= 4 && data[1] == 0 && data[3] == 0)
            return Encoding.Unicode.GetString(data[..(data.Length / 2 * 2)]);
        if (data.IndexOf((byte)0) >= 0) return null;
        try
        {
            var utf8 = new UTF8Encoding(false, true).GetString(data);
            var nonprint = utf8.Count(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
            return nonprint * 100 > utf8.Length * 5 ? null : utf8;
        }
        catch (DecoderFallbackException) { return null; }
    }

    private static string Safe(string text, int maxChars) =>
        new(text.Where(ch => !char.IsControl(ch)).Take(maxChars).ToArray());

    private static string Excerpt(string content, int match, int length)
    {
        var start = Math.Max(0, match - 32);
        var end = Math.Min(content.Length, match + length + 48);
        var sample = content[start..end];
        return Safe(sample.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), 140);
    }
}
