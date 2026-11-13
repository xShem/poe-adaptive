using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibGGPK3;
using LibGGPK3.Records;
using Microsoft.Win32;

namespace PoeAdaptive.Core;

public enum GameId { PoE1 = 1, PoE2 = 2 }
public enum ClientKind { Steam, GGG, Epic }
public enum ResourceFormat { ExternalBundles, Ggpk }
public static class Games
{
    public static GameId FromLegacy(string id) => id switch { "238960" => GameId.PoE1, "2694490" => GameId.PoE2, _ => throw new InvalidDataException("Неизвестная игра в данных 0.1.0; исходный файл сохранён.") };
    public static string LegacyId(this GameId game) => game switch { GameId.PoE1 => "238960", GameId.PoE2 => "2694490", _ => throw new InvalidDataException("Неизвестная игра.") };
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeAdaptive");
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record GameInstall(GameId Game, ClientKind Client, string Root, ResourceFormat Format,
    string ResourcePath, string ManifestPath, string LauncherVersion, string BuildId, string VersionRoot)
{
    public int SchemaVersion => 2;
    public string IndexPath => ResourcePath;
    // Keep the 0.1.0 Steam key so existing profiles, snapshots and pending journals remain addressable.
    public string Key => Client == ClientKind.Steam ? Games.Digest(Path.GetFullPath(ResourcePath).ToUpperInvariant())[..24]
        : Games.Digest($"{Game}|{Client}|{Format}|{Path.GetFullPath(ResourcePath).ToUpperInvariant()}")[..24];
    public string Title => $"Path of Exile {(int)Game} · {Client} · {Root}";
    public GameInstall Refresh() => InstallationCatalog.Default.Refresh(this);
    public static GameInstall FromSteam(SteamInstall value) => new(Games.FromLegacy(value.AppId), ClientKind.Steam,
        value.Root, ResourceFormat.ExternalBundles, value.IndexPath, value.ManifestPath, value.BuildId, value.BuildId, Games.DataRoot);
}

public interface IInstallationAdapter
{
    ClientKind Client { get; }
    IEnumerable<GameInstall> Discover();
    GameInstall Refresh(GameInstall install);
}

/// <summary>Cheap format validation before invoking a native decompressor. No writes, including Dispose.</summary>
public static class ResourceProbe
{
    public static ResourceFormat Inspect(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12]; input.ReadExactly(header); input.Position = 0;
        if (header[4..8].SequenceEqual("GGPK"u8))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 3) throw new InvalidDataException("Поддерживается только Windows GGPK v3 с Bundles2.");
            using var ggpk = new GGPK(input, true);
            if (ggpk.Root["Bundles2"] is not DirectoryRecord bundles || bundles["_.index.bin"] is not FileRecord index)
            {
                var inventory = GgpkStructureInspector.Describe(ggpk, input.Length);
                throw new InvalidDataException(GgpkStructureInspector.Explain(inventory));
            }
            using var indexStream = new GGFileStream(index);
            ValidateBundle(indexStream);
            return ResourceFormat.Ggpk;
        }
        ValidateBundle(input);
        return ResourceFormat.ExternalBundles;
    }
    public static void ValidateBundle(Stream input)
    {
        Span<byte> h = stackalloc byte[60]; input.ReadExactly(h);
        var size = BinaryPrimitives.ReadInt32LittleEndian(h);
        var compressed = BinaryPrimitives.ReadInt32LittleEndian(h[4..]);
        var head = BinaryPrimitives.ReadInt32LittleEndian(h[8..]);
        var chunks = BinaryPrimitives.ReadInt32LittleEndian(h[36..]);
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(h[40..]);
        if (size <= 0 || compressed <= 0 || chunks <= 0 || chunks > 1000000 || chunkSize <= 0
            || head != 48L + chunks * 4L || input.Length != 12L + head + compressed
            || BinaryPrimitives.ReadInt64LittleEndian(h[20..]) != size
            || BinaryPrimitives.ReadInt64LittleEndian(h[28..]) != compressed
            || ((long)size + chunkSize - 1) / chunkSize != chunks)
            throw new InvalidDataException("Ресурс не является поддерживаемым bundle-индексом.");
        long sum = 0;
        Span<byte> n = stackalloc byte[4];
        for (int c = 0; c < chunks; c++) { input.ReadExactly(n); var length = BinaryPrimitives.ReadInt32LittleEndian(n); if (length <= 0) throw new InvalidDataException("Повреждён chunk."); sum += length; }
        if (sum != compressed) throw new InvalidDataException("Повреждена таблица chunks.");
    }
    public static string SelectResource(string selected)
    {
        selected = Path.GetFullPath(selected);
        if (File.Exists(selected)) { Inspect(selected); return selected; }
        if (!Directory.Exists(selected)) throw new DirectoryNotFoundException(selected);
        var candidates = new[] { Path.Combine(selected, "Bundles2", "_.index.bin"), Path.Combine(selected, "Content.ggpk") }.Where(File.Exists).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("Выберите конкретный файл ресурсов: ресурс не найден либо выбор неоднозначен.");
        Inspect(candidates[0]); return candidates[0];
    }
}

public sealed class SteamAdapter(IEnumerable<string> roots) : IInstallationAdapter
{
    public ClientKind Client => ClientKind.Steam;
    public IEnumerable<GameInstall> Discover() => SteamLocator.Discover(roots).Select(GameInstall.FromSteam);
    public GameInstall Refresh(GameInstall install) => GameInstall.FromSteam(SteamLocator.FromManifest(install.ManifestPath));
}

public sealed class EpicAdapter(string manifestDirectory, Func<string, GameId>? identity = null) : IInstallationAdapter
{
    public ClientKind Client => ClientKind.Epic;
    private static GameId Game(string name) => name switch { "Path of Exile" => GameId.PoE1, "Path of Exile 2" => GameId.PoE2, _ => throw new InvalidDataException("Неизвестная игра Epic.") };
    public static GameInstall Read(string path, Func<string, GameId>? identity = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var e = doc.RootElement;
        var game = Game(e.GetProperty("DisplayName").GetString() ?? "");
        var root = Path.GetFullPath(e.GetProperty("InstallLocation").GetString()!);
        if (e.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.GetBoolean()) throw new IOException("Epic не завершил установку.");
        var executable = Path.GetFullPath(Path.Combine(root, e.GetProperty("LaunchExecutable").GetString()!));
        if (!executable.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(executable)) throw new InvalidDataException("Не найден исполняемый файл из манифеста Epic.");
        if ((identity ?? GggAdapter.ReadIdentity)(root) != game) throw new InvalidDataException("Игра в EXE не совпадает с манифестом Epic.");
        var resource = ResourceProbe.SelectResource(root);
        var token = e.GetProperty("AppVersionString").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("Нет версии Epic.");
        return new(game, ClientKind.Epic, root, ResourceProbe.Inspect(resource), resource, Path.GetFullPath(path), token, "", Games.DataRoot);
    }
    public IEnumerable<GameInstall> Discover()
    {
        if (!Directory.Exists(manifestDirectory)) yield break;
        foreach (var path in Directory.EnumerateFiles(manifestDirectory, "*.item"))
        {
            GameInstall? install = null;
            try { install = Read(path, identity); } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException or ArgumentException) { }
            if (install != null) yield return install;
        }
    }
    public GameInstall Refresh(GameInstall install) => Read(install.ManifestPath);
}

public sealed class GggAdapter(IEnumerable<string> roots, Func<string, GameId>? identity = null) : IInstallationAdapter
{
    public ClientKind Client => ClientKind.GGG;
    public static GameId ReadIdentity(string root)
    {
        var matches = Directory.EnumerateFiles(root, "*.exe").Select(path => FileVersionInfo.GetVersionInfo(path))
            .Where(v => (v.CompanyName ?? "").Contains("Grinding Gear Games", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.ProductName == "Path of Exile 2" || v.FileDescription == "Path of Exile 2" ? (GameId?)GameId.PoE2
                : v.ProductName == "Path of Exile" && !(v.FileDescription ?? "").Contains("2") ? GameId.PoE1 : null)
            .Where(g => g != null).Distinct().ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Игра не подтверждена метаданными EXE GGG. Региональные клиенты требуют отдельной проверки.");
        return matches[0]!.Value;
    }
    public GameInstall Read(string root, string? resourceOverride = null)
    {
        root = Path.GetFullPath(root);
        var game = (identity ?? ReadIdentity)(root);
        var resource = resourceOverride ?? ResourceProbe.SelectResource(root);
        // EXE content, not filesystem timestamps, supplies independent launcher evidence.
        var binaries = Directory.EnumerateFiles(root, "PathOfExile*.exe").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (binaries.Length == 0) throw new InvalidDataException("Нет EXE клиента GGG.");
        var token = Games.Digest(string.Join("|", binaries.Select(BuildStore.HashFile)));
        return new(game, ClientKind.GGG, root, ResourceProbe.Inspect(resource), resource, "", token, "", Games.DataRoot);
    }
    public IEnumerable<GameInstall> Discover()
    {
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            GameInstall? install = null;
            try { install = Read(root); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
            if (install != null) yield return install;
        }
    }
    public GameInstall Refresh(GameInstall install) => Read(install.Root, install.ResourcePath);
}

public sealed class InstallationCatalog(params IInstallationAdapter[] adapters)
{
    public static InstallationCatalog Default => new(new SteamAdapter(SteamRoots()),
        new EpicAdapter(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests")), new GggAdapter(GggRoots()));
    public IReadOnlyList<GameInstall> Discover() => adapters.SelectMany(a => a.Discover()).DistinctBy(i => i.Key).ToArray();
    public GameInstall Manual(string path, GameId? expectedGame = null, ClientKind? expectedClient = null)
    {
        var resource = ResourceProbe.SelectResource(path);
        var found = Discover().Where(i => string.Equals(i.ResourcePath, resource, StringComparison.OrdinalIgnoreCase)).ToArray();
        GameInstall install;
        if (found.Length == 1) install = found[0];
        else if (found.Length > 1) throw new InvalidDataException("Несколько клиентов заявляют одну установку.");
        else
        {
            SteamInstall? steam = null;
            try { steam = SteamLocator.FromIndex(resource); } catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            if (steam != null) install = GameInstall.FromSteam(steam);
            else
            {
                var root = ResourceProbe.Inspect(resource) == ResourceFormat.Ggpk ? Path.GetDirectoryName(resource)! : Directory.GetParent(Path.GetDirectoryName(resource)!)!.FullName;
                // An Epic-owned directory with missing/incomplete metadata must not become a GGG install.
                if (Directory.Exists(Path.Combine(root, ".egstore"))) throw new InvalidDataException("Каталог Epic не имеет подтверждённого завершённого манифеста. Завершите проверку / установку в Epic.");
                install = new GggAdapter([]).Read(root, resource);
            }
        }
        if (expectedGame != null && install.Game != expectedGame || expectedClient != null && install.Client != expectedClient)
            throw new InvalidDataException("Выбранная игра или клиент не совпадает с установкой.");
        return install;
    }
    public GameInstall Refresh(GameInstall install)
    {
        var fresh = adapters.Single(a => a.Client == install.Client).Refresh(install);
        if (fresh.Key != install.Key || fresh.Game != install.Game || fresh.Format != install.Format) throw new IOException("Идентичность установки изменилась. Найдите игру заново.");
        return install.Client == ClientKind.Steam ? fresh : VersionTracker.Resolve(fresh with { VersionRoot = install.VersionRoot });
    }
    private static IEnumerable<string> SteamRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path) yield return path;
        }
    }
    private static IEnumerable<string> GggRoots()
    {
        foreach (var baseDir in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            foreach (var name in new[] { "Path of Exile", "Path of Exile 2" }) yield return Path.Combine(Environment.GetFolderPath(baseDir), "Grinding Gear Games", name);
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var location in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var parent = hive.OpenSubKey(location); if (parent == null) continue;
                foreach (var name in parent.GetSubKeyNames())
                {
                    using var key = parent.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is string title && title is "Path of Exile" or "Path of Exile 2"
                        && key.GetValue("InstallLocation") is string root) yield return root;
                }
            }
    }
}
