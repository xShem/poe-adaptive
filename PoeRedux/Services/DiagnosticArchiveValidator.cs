using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoeRedux.Services;

/// <summary>Reject corrupt, oversized, unsafe, or inconsistent export parts before publication.</summary>
public static class DiagnosticArchiveValidator
{
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    {
        "manifest.json", "README-FOR-AI.md", "PROMPT.txt", "summary.txt"
    };
    private static bool ValidMember(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains('\\') ||
            value.Contains(':') || value.Contains('\0')) return false;
        return value.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    }
    private static bool Forbidden(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".ggpk", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("_.index.bin", StringComparison.OrdinalIgnoreCase);
    }

    public static void Validate(string path)
    {
        if (new FileInfo(path).Length > DiagnosticPackager.MaxZip)
            throw new InvalidDataException("Диагностический ZIP превысил 20 МиБ.");
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        if (entries.Count != zip.Entries.Count || entries.Count < Metadata.Count ||
            entries.Keys.Any(p => !ValidMember(p) || Forbidden(p)))
            throw new InvalidDataException("ZIP содержит повторяющиеся, запрещённые или небезопасные пути.");
        foreach (var required in Metadata)
            if (!entries.ContainsKey(required)) throw new InvalidDataException("ZIP не содержит " + required);
        if (entries.Values.Sum(e => e.Length) > DiagnosticPackager.MaxExpanded)
            throw new InvalidDataException("ZIP превышает лимит распакованных данных.");
        using var document = JsonDocument.Parse(entries["manifest.json"].Open());
        var manifest = document.RootElement;
        if (manifest.GetProperty("SchemaVersion").GetInt32() != 1 ||
            manifest.GetProperty("Parts").GetInt32() < 1 ||
            manifest.GetProperty("Part").GetInt32() < 1 ||
            manifest.GetProperty("Part").GetInt32() > manifest.GetProperty("Parts").GetInt32() ||
            !manifest.GetProperty("Consistent").GetBoolean())
            throw new InvalidDataException("Неизвестная или несогласованная схема пакета.");
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.GetProperty("Files").EnumerateArray())
        {
            var name = file.GetProperty("ZipPath").GetString() ?? "";
            if (!listed.Add(name) || !entries.TryGetValue(name, out var entry))
                throw new InvalidDataException("Запись манифеста отсутствует или повторена: " + name);
            if (entry.Length != file.GetProperty("Size").GetInt64())
                throw new InvalidDataException("Размер не соответствует манифесту: " + name);
            using var stream = entry.Open();
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            if (!digest.Equals(file.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 ресурса не соответствует манифесту: " + name);
        }
        if (entries.Keys.Except(Metadata).Except(listed).Any())
            throw new InvalidDataException("ZIP содержит материалы, отсутствующие в манифесте.");
        foreach (var key in Metadata)
        {
            using var input = entries[key].Open();
            using var text = new StreamReader(input);
            var value = text.ReadToEnd();
            if (value.Contains(@"C:\Users\", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("/Users/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("В отчёте обнаружен возможный персональный абсолютный путь.");
        }
    }
}