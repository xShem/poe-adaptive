using System.Buffers.Binary;
using LibGGPK3;
using LibGGPK3.Records;

namespace PoeAdaptive.Core;

/// <summary>
/// Shallow, read-only survey of a GGPK container. This never decodes Oodle
/// bundles, changes the archive, or assumes that any index-looking file is safe
/// to patch. All paths in the report are archive-relative.
/// </summary>
public sealed record GgpkStructureReport(
    int SchemaVersion,
    uint GgpkVersion,
    long FileBytes,
    string Classification,
    bool Bundles2Found,
    bool Bundles2IndexFound,
    string[] RootEntries,
    string[] Bundles2Entries,
    string[] OtherIndexCandidates,
    string Note);

public static class GgpkStructureInspector
{
    private const int MaxEntries = 24;
    private const int MaxCandidateEntries = 32;

    public static GgpkStructureReport Inspect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 12)
            return Empty("truncated", stream.Length, 0, "Файл короче 12-байтового заголовка.");
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[4..8].SequenceEqual("GGPK"u8))
            return Empty("not-ggpk", stream.Length, 0, "Сигнатура GGPK отсутствует.");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
        if (version != 3)
            return Empty("unsupported-version", stream.Length, version,
                "Версия контейнера отличается от GGPK v3; изменение не поддерживается.");
        stream.Position = 0;
        using var ggpk = new GGPK(stream, true);
        return Describe(ggpk, stream.Length);
    }

    /// <summary>Use the same already-open read-only GGPK in ResourceProbe to avoid another expensive open.</summary>
    public static GgpkStructureReport Describe(GGPK ggpk, long bytes)
    {
        var root = ggpk.Root;
        var bundles = root["Bundles2"] as DirectoryRecord;
        var index = bundles?["_.index.bin"] as FileRecord;
        var rootEntries = Names(root);
        string[] bundleEntries = bundles is null ? [] : Names(bundles);
        var candidates = new List<string>();
        // Deliberately shallow: only root, root directories and one more level.
        // A candidate is a clue for diagnosis, NEVER an alternative patch target.
        if (root["_.index.bin"] is FileRecord) candidates.Add("_.index.bin");
        for (var a = 0; a < Math.Min(root.Count, MaxCandidateEntries); a++)
        {
            if (root[a] is not DirectoryRecord first) continue;
            if (first["_.index.bin"] is FileRecord)
                candidates.Add(first.Name + "/_.index.bin");
            for (var b = 0; b < Math.Min(first.Count, MaxCandidateEntries); b++)
            {
                if (first[b] is DirectoryRecord second &&
                    second["_.index.bin"] is FileRecord)
                    candidates.Add(first.Name + "/" + second.Name + "/_.index.bin");
                if (candidates.Count >= MaxEntries) break;
            }
            if (candidates.Count >= MaxEntries) break;
        }
        var status = index is not null ? "bundled-ggpk-v3" :
            bundles is null ? "bundles2-directory-missing" : "bundles2-index-missing";
        var note = index is not null
            ? "Ожидаемый индекс найден. Его содержимое и совместимость правил проверяются отдельно."
            : "Это GGPK v3, но ожидаемый Bundles2/_.index.bin отсутствует. Архив не будет изменён. " +
              "Список файлов не доказывает ни устаревшую версию клиента, ни повреждение архива.";
        return new(1, ggpk.Version, bytes, status, bundles is not null,
            index is not null, rootEntries, bundleEntries, candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), note);
    }

    public static string Explain(GgpkStructureReport report)
    {
        var root = report.RootEntries.Length == 0 ? "(пусто)" : string.Join(", ", report.RootEntries);
        var child = report.Bundles2Found
            ? " В Bundles2: " + (report.Bundles2Entries.Length == 0 ? "(пусто)" : string.Join(", ", report.Bundles2Entries)) + "."
            : "";
        var alternate = report.OtherIndexCandidates.Length > 0
            ? " Другие похожие пути (не поддерживаются): " + string.Join(", ", report.OtherIndexCandidates) + "."
            : "";
        return "GGPK v" + report.GgpkVersion + ": отсутствует обязательный Bundles2/_.index.bin. " +
            "Корень архива: " + root + "." + child + alternate +
            " Для диагностики используйте PoeAdaptive.Diagnostics.exe --inspect-ggpk <файл.ggpk> <отчёт.json>. " +
            "Это безопасная проверка только на чтение; не переименовывайте и не пересобирайте GGPK.";
    }

    private static string[] Names(DirectoryRecord directory)
    {
        var list = new List<string>();
        for (var i = 0; i < Math.Min(directory.Count, MaxEntries); i++)
        {
            var node = directory[i];
            // Avoid unbounded output or control characters in messages/logs.
            var name = new string(node.Name.Where(c => !char.IsControl(c)).Take(96).ToArray());
            list.Add(name + (node is DirectoryRecord ? "/" : ""));
        }
        if (directory.Count > MaxEntries) list.Add("… ещё " + (directory.Count - MaxEntries));
        return list.ToArray();
    }

    private static GgpkStructureReport Empty(string status, long size, uint version, string note) =>
        new(1, version, size, status, false, false, [], [], [], note);
}
