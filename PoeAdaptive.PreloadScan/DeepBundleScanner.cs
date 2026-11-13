using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibBundle3;
using LibGGPK3;
using LibGGPK3.Records;

namespace PoeAdaptive.PreloadScan;

public sealed record BundleEnvelopeInfo(int UncompressedSize, int CompressedSize,
    int ChunkCount, int ChunkSize, int Compressor);
public sealed record DeepBundleEvidence(string Feature, string BundlePath, long DecompressedOffset,
    string Encoding, string Indicator, string Preview, bool FileBoundaryUnknown);
public sealed record DeepFeatureFinding(string Feature, int Matches, DeepBundleEvidence[] Evidence);
public sealed record DeepBundleReport(int SchemaVersion, string Status, string ScannerVersion,
    string OodleFileName, string OodleSha256, long BundlesDiscovered, int BundlesSelected,
    int BundlesRead, int InvalidHeaders, int DecodeErrors, int ChunksDecoded, long BytesDecoded,
    bool SamplingLimited, DeepFeatureFinding[] Features, string[] Warnings);

/// <summary>
/// For PoE 1 standalone GGPK: sample individual *compressed bundles* without relying on _.index.bin.
/// The absence of an index prevents recovery of virtual asset boundaries or trusted file names.
/// </summary>
public static class DeepBundleScanner
{
    public const int MaxNodes = 100_000;
    public const int MaxDepth = 24;
    public const int MaxBundles = 128;
    public const int MaxChunksPerBundle = 2;
    public const int MaxDecodedBytes = 64 * 1024 * 1024;
    public const int MaxChunkSize = 256 * 1024;
    public const int MaxEvidencePerFeature = 8;

    private sealed record BundleCandidate(FileRecord Record, string Path, int Priority);
    private sealed record Rule(string Name, string[] Indicators);
    // PoE1 PatchCatalog (not PoE2-exclusive Rain/Clouds/AtlasFog/ColorMods2).
    private static readonly Rule[] Rules =
    [
        new("Camera", ["CreateCameraZoomNode", "ClearCameraZoomNodes", "CreateCameraLookAtNode", "SetCustomCameraSpeed"]),
        new("Minimap", ["minimap_visibility_pixel", "minimap_blending_pixel", "walkable_color", "walkability_map_color"]),
        new("ColorMods", ["map_monsters_reflect", "map_item_drop_quantity"]),
        new("MonsterHP", ["item_drop_slots", "base_maximum_life"]),
        new("Fog", ["\"fog\"", "fog_density"]),
        new("EnvironmentParticles", ["\"rain_intensity\"", "\"clouds_intensity\"", "\"effect_spawner\""]),
        new("Shadow", ["\"shadows_enabled\""]),
        new("Light", ["\"directional_light\""]),
        new("Corpse", ["DisableRendering", "on_spawned_dead"]),
        new("Delirium", ["league_affliction", "Metadata/Effects/Environment"]),
        new("Particles", ["particle_emitter", "Metadata/Particles"]),
        new("Effects", ["AnimatedRender", "ClientAnimationController"]),
        new("BlackScreen", ["\"post_processing\"", "\"global_illumination\"", "postprocess"])
    ];

    private static readonly object NativeGate = new();
    private static IntPtr nativeHandle;
    private static string? nativeFileName;
    private static string? nativeSha;

    /// <summary>
    /// Only a user-provided native library immediately beside our executable is eligible.
    /// No DLL is redistributed, copied from the game, downloaded, or added to search PATH.
    /// </summary>
    public static bool TrySetUpLocalOodle(out string name, out string sha, out string issue)
    {
        lock (NativeGate)
        {
            name = nativeFileName ?? "";
            sha = nativeSha ?? "";
            issue = "";
            if (nativeHandle != IntPtr.Zero) return true;

            var appFolder = AppContext.BaseDirectory;
            var paths = Directory.EnumerateFiles(appFolder, "oo2core*.dll", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetFileName(p), "oo2core.dll", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p).StartsWith("oo2core_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length != 1)
            {
                issue = paths.Length == 0
                    ? "Положите один совместимый oo2core*.dll рядом с PoeAdaptive.PreloadScan.exe."
                    : "Найдено несколько oo2core DLL. Оставьте только одну рядом с EXE.";
                return false;
            }
            var file = paths[0];
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            if (!NativeLibrary.TryLoad(Path.GetFullPath(file), out var loaded))
            {
                issue = "Не удалось загрузить локальную Oodle DLL (нужна совместимая Windows x64).";
                return false;
            }
            string[] symbols = ["OodleLZ_Decompress", "OodleLZDecoder_MemorySizeNeeded",
                "OodleLZ_GetCompressedBufferSizeNeeded"];
            if (symbols.Any(symbol => !NativeLibrary.TryGetExport(loaded, symbol, out _)))
            {
                NativeLibrary.Free(loaded);
                issue = "В выбранной Oodle DLL отсутствуют необходимые точки входа.";
                return false;
            }
            try
            {
                nativeHandle = loaded;
                NativeLibrary.SetDllImportResolver(typeof(Bundle).Assembly,
                    (library, _, _) => library.Equals("oo2core", StringComparison.OrdinalIgnoreCase)
                        ? nativeHandle : IntPtr.Zero);
                nativeFileName = Path.GetFileName(file);
                nativeSha = hash;
                name = nativeFileName;
                sha = hash;
                return true;
            }
            catch (InvalidOperationException)
            {
                NativeLibrary.Free(loaded);
                nativeHandle = IntPtr.Zero;
                issue = "Другой компонент уже настроил загрузку Oodle. Выполните отдельный запуск сканера.";
                return false;
            }
        }
    }

    public static bool TryParseEnvelope(ReadOnlySpan<byte> bytes, long totalBytes, out BundleEnvelopeInfo? value)
    {
        value = null;
        if (bytes.Length < 60 || totalBytes < 64) return false;
        int uncompressed = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int compressed = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int headSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int compressor = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        long uncompressedLong = BinaryPrimitives.ReadInt64LittleEndian(bytes[20..]);
        long compressedLong = BinaryPrimitives.ReadInt64LittleEndian(bytes[28..]);
        int chunks = BinaryPrimitives.ReadInt32LittleEndian(bytes[36..]);
        int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[40..]);
        if (uncompressed < 1 || compressed < 1 || uncompressedLong != uncompressed ||
            compressedLong != compressed || uncompressed > 512L * 1024 * 1024 ||
            chunkSize < 4096 || chunkSize > MaxChunkSize || chunks <= 0 || chunks > 4096 ||
            ((long)uncompressed + chunkSize - 1) / chunkSize != chunks ||
            headSize != 48L + chunks * 4L || (long)12 + headSize + compressed != totalBytes ||
            compressor is not (3 or 8 or 9 or 11 or 12 or 13))
            return false;
        value = new(uncompressed, compressed, chunks, chunkSize, compressor);
        return true;
    }

    public static bool ValidateChunkLengths(ReadOnlySpan<byte> table, BundleEnvelopeInfo metadata)
    {
        if (table.Length != metadata.ChunkCount * 4) return false;
        long total = 0;
        for (var i = 0; i < metadata.ChunkCount; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(table.Slice(i * 4, 4));
            if (length <= 0 || length > metadata.CompressedSize) return false;
            total += length;
            if (total > metadata.CompressedSize) return false;
        }
        return total == metadata.CompressedSize;
    }

    public static DeepBundleReport Scan(string path, Action<string>? progress = null)
    {
        var warnings = new List<string>();
        if (!TrySetUpLocalOodle(out var dllName, out var dllSha, out var setupProblem))
            return Empty("oodle-unavailable", "", "", [setupProblem]);
        path = Path.GetFullPath(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var beforeSize = input.Length;
        var beforeTimestamp = File.GetLastWriteTimeUtc(path);
        using var ggpk = new GGPK(input, true);
        if (ggpk.Version is not (3 or 4)) throw new InvalidDataException("Неподдерживаемая версия GGPK.");

        var candidates = new List<BundleCandidate>();
        var stack = new Stack<(TreeNode Node, int Depth)>();
        stack.Push((ggpk.Root, 0));
        bool truncated = false;
        var visited = 0;
        while (stack.Count != 0)
        {
            var (node, depth) = stack.Pop();
            if (++visited > MaxNodes) { truncated = true; break; }
            if (node is DirectoryRecord directory)
            {
                if (depth >= MaxDepth || directory.Count + stack.Count > MaxNodes)
                { truncated = true; continue; }
                for (var i = directory.Count - 1; i >= 0; i--)
                    stack.Push((directory[i], depth + 1));
            }
            else if (node is FileRecord file &&
                     file.Name.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase))
            {
                var internalPath = file.GetPath().Replace('\\', '/');
                int score = internalPath.Contains("metadata/", StringComparison.OrdinalIgnoreCase) ? 30 :
                    internalPath.Contains("shader", StringComparison.OrdinalIgnoreCase) ? 20 :
                    internalPath.Contains("effects", StringComparison.OrdinalIgnoreCase) ? 10 : 0;
                candidates.Add(new(file, internalPath, score));
            }
        }

        if (truncated) warnings.Add("Просмотр каталога ограничен; часть bundles могла не попасть в список.");
        var sorted = candidates.OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = new List<BundleCandidate>();
        foreach (var candidate in sorted.Where(c => c.Priority > 0).Take(MaxBundles / 4))
            selected.Add(candidate);
        var rest = sorted.Where(c => !selected.Contains(c)).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var count = Math.Min(MaxBundles - selected.Count, rest.Length);
        for (var i = 0; i < count; i++)
        {
            var index = count == 1 ? 0 : (int)((long)i * (rest.Length - 1) / (count - 1));
            selected.Add(rest[index]);
        }

        var matches = Rules.ToDictionary(r => r.Name, _ => 0, StringComparer.Ordinal);
        var evidence = Rules.ToDictionary(r => r.Name, _ => new List<DeepBundleEvidence>(), StringComparer.Ordinal);
        int read = 0, invalid = 0, failures = 0, chunksRead = 0;
        long bytesRead = 0;
        progress?.Invoke("Декодирование выбранных bundles Oodle (только чтение)…");
        foreach (var item in selected)
        {
            if (bytesRead >= MaxDecodedBytes) { truncated = true; break; }
            var file = item.Record;
            if (file.DataLength < 64) { invalid++; continue; }
            try
            {
                byte[] header = new byte[60];
                file.Read(header);
                if (!TryParseEnvelope(header, file.DataLength, out var metadata) || metadata is null)
                { invalid++; continue; }
                byte[] table = new byte[checked(metadata.ChunkCount * 4)];
                file.Read(table, 60);
                if (!ValidateChunkLengths(table, metadata)) { invalid++; continue; }
                using var stream = new GGFileStream(file);
                using var bundle = new Bundle(stream, false);
                read++;
                int[] chosen = metadata.ChunkCount == 1 ? [0] :
                    new[] { 0, metadata.ChunkCount / 2 }.Distinct().ToArray();
                foreach (var chunk in chosen.Take(MaxChunksPerBundle))
                {
                    var offset = checked(chunk * metadata.ChunkSize);
                    var size = Math.Min(metadata.ChunkSize, metadata.UncompressedSize - offset);
                    if (size <= 0) continue;
                    if (bytesRead + size > MaxDecodedBytes) { truncated = true; break; }
                    ArraySegment<byte> decoded = bundle.ReadWithoutCache(offset, size);
                    chunksRead++;
                    bytesRead += decoded.Count;
                    ScanChunk(item.Path, offset, decoded.AsSpan(), matches, evidence);
                }
                if (read % 10 == 0)
                    progress?.Invoke($"Прочитано bundles: {read}; декодировано: {bytesRead / 1048576} МиБ…");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
            {
                failures++;
                if (warnings.Count < 8) warnings.Add("Не удалось декодировать " +
                    Path.GetFileName(item.Path) + ": " + ex.GetType().Name);
            }
        }

        if (input.Length != beforeSize || File.GetLastWriteTimeUtc(path) != beforeTimestamp)
            throw new IOException("GGPK изменился во время чтения: диагностика остановлена.");

        if (selected.Count < candidates.Count || chunksRead < selected.Count)
            truncated = true;
        warnings.Add("_.index.bin не требуется для чтения отдельных bundles, но имена и границы ресурсов внутри них не восстановлены.");
        warnings.Add("Отчёт основан на ограниченной выборке распакованных блоков; отсутствие совпадений ничего не доказывает.");
        warnings.Add("Сканирование не изменяет файлы игры и не подтверждает работоспособность патчей.");
        return new(1, "sampled", "0.2.0", dllName, dllSha, candidates.Count, selected.Count,
            read, invalid, failures, chunksRead, bytesRead, truncated,
            Rules.Select(r => new DeepFeatureFinding(r.Name, matches[r.Name], evidence[r.Name].ToArray())).ToArray(),
            warnings.ToArray());
    }

    private static void ScanChunk(string bundle, long blockOffset, ReadOnlySpan<byte> bytes,
        Dictionary<string, int> counts, Dictionary<string, List<DeepBundleEvidence>> evidence)
    {
        foreach (var rule in Rules)
            foreach (var sign in rule.Indicators)
                foreach (var encoding in new[] { (Name: "utf-8/ascii", Bytes: Encoding.UTF8.GetBytes(sign)),
                    (Name: "utf-16le", Bytes: Encoding.Unicode.GetBytes(sign)) })
                {
                    var at = bytes.IndexOf(encoding.Bytes);
                    if (at < 0) continue;
                    counts[rule.Name]++;
                    if (evidence[rule.Name].Count >= MaxEvidencePerFeature) continue;
                    evidence[rule.Name].Add(new(rule.Name, bundle, blockOffset + at, encoding.Name,
                        sign, SafePreview(bytes, at, encoding.Bytes.Length, encoding.Name), true));
                }
    }

    private static string SafePreview(ReadOnlySpan<byte> input, int location, int needle, string encoding)
    {
        var left = Math.Max(0, location - (encoding == "utf-16le" ? 30 : 15));
        var right = Math.Min(input.Length, location + needle + (encoding == "utf-16le" ? 46 : 23));
        var span = input[left..right];
        if (encoding == "utf-16le")
        {
            if ((span.Length & 1) != 0) span = span[..^1];
            return Clean(Encoding.Unicode.GetString(span));
        }
        return Clean(Encoding.ASCII.GetString(span));
    }

    private static string Clean(string source) =>
        new(source.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-' or '.' or ':' or '=' or '/'
            or '"' or '\'' or '(' or ')' or ',' or '{' or '}' ? ch : ' ').Take(96).ToArray());

    private static DeepBundleReport Empty(string status, string name, string hash, string[] warnings) =>
        new(1, status, "0.2.0", name, hash, 0, 0, 0, 0, 0, 0, 0, true,
            Rules.Select(r => new DeepFeatureFinding(r.Name, 0, [])).ToArray(), warnings);
}
