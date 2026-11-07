using System.Security.Cryptography;
using System.Text;

namespace PoeAdaptive.Core;

public sealed class Vdf
{
    public Dictionary<string, Vdf> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Value { get; private set; } = "";
    public string Get(string key) => Children.TryGetValue(key, out var node) ? node.Value : "";
    public Vdf? Node(string key) => Children.GetValueOrDefault(key);

    public static Vdf Parse(string text)
    {
        int pos = 0;
        string? Token()
        {
            while (pos < text.Length)
            {
                if (char.IsWhiteSpace(text[pos]) || text[pos] == '\uFEFF') { pos++; continue; }
                if (pos + 1 < text.Length && text[pos..(pos + 2)] == "//")
                { while (pos < text.Length && text[pos] != '\n') pos++; continue; }
                break;
            }
            if (pos == text.Length) return null;
            var c = text[pos++];
            if (c is '{' or '}') return c.ToString();
            if (c != '"') throw new InvalidDataException("Ожидалась строка VDF.");
            var s = new StringBuilder();
            while (pos < text.Length)
            {
                c = text[pos++];
                if (c == '"') return s.ToString();
                if (c == '\\' && pos < text.Length && text[pos] is '\\' or '"') c = text[pos++];
                s.Append(c);
            }
            throw new InvalidDataException("Незавершённая строка VDF.");
        }
        Vdf Object(bool nested)
        {
            var obj = new Vdf();
            while (true)
            {
                var key = Token();
                if (key == null && !nested || key == "}" && nested) return obj;
                if (key == null || key is "{" or "}") throw new InvalidDataException("Некорректный объект VDF.");
                var value = Token() ?? throw new InvalidDataException("Нет значения VDF.");
                if (value == "}") throw new InvalidDataException("Нет значения VDF.");
                obj.Children[key] = value == "{" ? Object(true) : new Vdf { Value = value };
            }
        }
        return Object(false);
    }
}

public sealed record SteamInstall(string AppId, string BuildId, string Root, string ManifestPath, string IndexPath)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Path.GetFullPath(IndexPath).ToUpperInvariant())))[..24];
    public string Title => AppId == "238960" ? "Path of Exile 1" : "Path of Exile 2";

    public SteamInstall Refresh()
    {
        var current = SteamLocator.FromManifest(ManifestPath);
        if (current.AppId != AppId || !string.Equals(current.IndexPath, IndexPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Steam изменил установку игры. Найдите её заново.");
        return current;
    }
}

public static class SteamLocator
{
    public static readonly string[] AppIds = ["238960", "2694490"];

    public static SteamInstall FromManifest(string path)
    {
        var state = Vdf.Parse(File.ReadAllText(path)).Node("AppState")
            ?? throw new InvalidDataException("Нет AppState в манифесте Steam.");
        var id = state.Get("appid");
        if (!AppIds.Contains(id)) throw new InvalidDataException("Это не манифест PoE 1 или PoE 2.");
        if (state.Get("StateFlags") != "4")
            throw new IOException("Steam ещё не завершил установку или обновление игры.");
        var build = state.Get("buildid");
        if (!ulong.TryParse(build, out var n) || n == 0) throw new InvalidDataException("Неизвестная сборка Steam.");
        var dir = state.Get("installdir");
        if (string.IsNullOrWhiteSpace(dir) || dir is "." or ".." || dir.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Некорректный каталог Steam.");
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "common", dir));
        var index = Path.Combine(root, "Bundles2", "_.index.bin");
        if (!File.Exists(index)) throw new FileNotFoundException("Не найден Steam-индекс Bundles2/_.index.bin.", index);
        return new(id, build, root, Path.GetFullPath(path), index);
    }

    public static SteamInstall FromIndex(string index)
    {
        index = Path.GetFullPath(index);
        for (var p = new DirectoryInfo(Path.GetDirectoryName(index)!); p != null; p = p.Parent)
        {
            foreach (var id in AppIds)
            {
                var manifest = Path.Combine(p.FullName, $"appmanifest_{id}.acf");
                if (!File.Exists(manifest)) continue;
                var install = FromManifest(manifest);
                if (string.Equals(install.IndexPath, index, StringComparison.OrdinalIgnoreCase)) return install;
            }
        }
        throw new InvalidDataException("Выберите Bundles2/_.index.bin из установки Steam.");
    }

    public static IReadOnlyList<SteamInstall> Discover(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(root);
            var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(file)) continue;
            var folders = Vdf.Parse(File.ReadAllText(file)).Node("libraryfolders");
            if (folders == null) continue;
            foreach (var (key, node) in folders.Children)
                if (int.TryParse(key, out _))
                {
                    var lib = node.Get("path");
                    if (lib.Length == 0) lib = node.Value;
                    if (lib.Length != 0) libraries.Add(lib);
                }
        }
        var result = new List<SteamInstall>();
        foreach (var lib in libraries)
            foreach (var id in AppIds)
            {
                var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{id}.acf");
                if (!File.Exists(manifest)) continue;
                try { result.Add(FromManifest(manifest)); }
                catch (IOException) { /* Incomplete installs are not selectable. */ }
            }
        return result.DistinctBy(x => x.Key).ToArray();
    }
}
