using System.Security.Cryptography;
using System.Text.Json;

namespace PoeAdaptive.Core;

public enum ResourceState { Original, Applied, Updated, Unknown }
public sealed record VersionState(int SchemaVersion, string InstallationKey, GameId Game, ClientKind Client,
    ResourceFormat Format, string BuildId, string LauncherVersion, string OriginalHash, string CurrentHash, string Hint, Dictionary<string, string>? BundleHashes = null);

public static class VersionTracker
{
    private static string PathFor(GameInstall i) => Path.Combine(i.VersionRoot, "Versions", i.Key + ".json");
    public static string Hint(GameInstall i)
    {
        var f = new FileInfo(i.ResourcePath);
        using var s = new FileStream(i.ResourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[(int)Math.Min(s.Length, 65536)]; s.ReadExactly(bytes);
        var head = Convert.ToHexString(SHA256.HashData(bytes));
        s.Position = Math.Max(0, s.Length - bytes.Length); s.ReadExactly(bytes);
        var tail = Convert.ToHexString(SHA256.HashData(bytes));
        var bundles = i.Format == ResourceFormat.ExternalBundles
            ? string.Join("|", Directory.EnumerateFiles(Path.GetDirectoryName(i.ResourcePath)!, "*.bundle.bin", SearchOption.AllDirectories)
                .Where(p => !Path.GetRelativePath(Path.GetDirectoryName(i.ResourcePath)!, p).Replace('\\','/').StartsWith("PoeAdaptive/", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).Select(p => { var b = new FileInfo(p); return $"{p}:{b.Length}:{b.LastWriteTimeUtc.Ticks}"; })) : "";
        return Games.Digest($"{f.Length}|{f.LastWriteTimeUtc.Ticks}|{head}|{tail}|{bundles}");
    }
    public static Dictionary<string, string> Bundles(GameInstall i) => i.Format != ResourceFormat.ExternalBundles ? new()
        : Directory.EnumerateFiles(Path.GetDirectoryName(i.ResourcePath)!, "*.bundle.bin", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(Path.GetDirectoryName(i.ResourcePath)!, p).Replace('\\', '/').StartsWith("PoeAdaptive/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, BuildStore.HashFile, StringComparer.OrdinalIgnoreCase);
    private static bool SameBundles(Dictionary<string, string>? a, Dictionary<string, string> b) => a == null ? b.Count == 0
        : a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var hash) && hash == kv.Value);
    private static string BuildIdentity(GameInstall i, string hash, Dictionary<string, string> bundles) => "r2-" + Games.Digest(i.LauncherVersion + "|" + hash + "|" + string.Join("|", bundles.Select(kv => Path.GetRelativePath(i.Root, kv.Key) + ":" + kv.Value)))[..32];
    public static VersionState? Read(GameInstall i)
    {
        if (!File.Exists(PathFor(i))) return null;
        var v = JsonSerializer.Deserialize<VersionState>(File.ReadAllText(PathFor(i))) ?? throw new InvalidDataException("Повреждён паспорт ресурсов.");
        if (v.SchemaVersion != 2 || v.InstallationKey != i.Key || v.Game != i.Game || v.Client != i.Client || v.Format != i.Format)
            throw new InvalidDataException("Паспорт относится к другой установке или имеет неизвестную версию.");
        return v;
    }
    public static GameInstall Resolve(GameInstall i, bool force = false)
    {
        if (i.Client == ClientKind.Steam) return i;
        var old = Read(i); var hint = Hint(i);
        if (!force && old != null && old.Hint == hint && old.LauncherVersion == i.LauncherVersion) return i with { BuildId = old.BuildId };
        var hash = BuildStore.HashFile(i.ResourcePath);
        var bundles = Bundles(i);
        if (old != null)
        {
            var previous = i with { BuildId = old.BuildId };
            var store = new BuildStore(i.VersionRoot, previous);
            store.Recover();
            var state = store.Load();
            if (SameBundles(old.BundleHashes, bundles) && state != null && (hash == state.CurrentHash || hash == state.OriginalHash))
            {
                BuildStore.SaveJson(PathFor(i), old with { CurrentHash = hash, Hint = hint, LauncherVersion = i.LauncherVersion });
                return previous;
            }
            if (SameBundles(old.BundleHashes, bundles) && (hash == old.CurrentHash || hash == old.OriginalHash))
            {
                BuildStore.SaveJson(PathFor(i), old with { Hint = hint, LauncherVersion = i.LauncherVersion });
                return previous;
            }
            if (old.LauncherVersion == i.LauncherVersion)
                throw new IOException("Неизвестные сторонние изменения ресурсов. Обновление не подтверждено лаунчером; запись и автоприменение остановлены. Выполните проверку файлов и диагностику.");
        }
        ResourceProbe.Inspect(i.ResourcePath);
        var build = BuildIdentity(i, hash, bundles);
        BuildStore.SaveJson(PathFor(i), new VersionState(2, i.Key, i.Game, i.Client, i.Format, build, i.LauncherVersion, hash, hash, hint, bundles));
        return i with { BuildId = build };
    }
    public static GameInstall AcceptVerified(GameInstall i)
    {
        if (i.Client == ClientKind.Steam) return i.Refresh();
        FileSafety.RequireIdle(i, false); ResourceProbe.Inspect(i.ResourcePath);
        var old = Read(i); var hash = BuildStore.HashFile(i.ResourcePath); var bundles = Bundles(i);
        if (old != null && hash == old.CurrentHash && hash != old.OriginalHash)
            throw new IOException("Это собственная изменённая версия. Сначала восстановите ресурсы либо выполните официальную проверку файлов.");
        if (old != null)
        {
            var history = Path.Combine(i.VersionRoot, "Versions", "History", i.Key, Guid.NewGuid().ToString("N") + ".json");
            BuildStore.SaveJson(history, old);
            var pending = Path.Combine(i.VersionRoot, "Backups", i.Key, old.BuildId, "pending.json");
            if (File.Exists(pending)) File.Move(pending, pending + ".reviewed-" + Guid.NewGuid().ToString("N"));
        }
        var build = BuildIdentity(i, hash, bundles);
        BuildStore.SaveJson(PathFor(i), new VersionState(2, i.Key, i.Game, i.Client, i.Format, build, i.LauncherVersion, hash, hash, Hint(i), bundles));
        return i with { BuildId = build };
    }
    public static void Record(GameInstall i, BuildState state)
    {
        if (i.Client == ClientKind.Steam) return;
        var old = Read(i) ?? throw new IOException("Нет паспорта ресурсов.");
        if (old.BuildId != i.BuildId) throw new IOException("Версия ресурсов изменилась.");
        BuildStore.SaveJson(PathFor(i), old with { CurrentHash = state.CurrentHash, Hint = Hint(i) });
    }
}

public static class FileSafety
{
    public static void RequireSpace(string path, long bytes, Func<string, long>? available = null)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var free = available?.Invoke(root) ?? new DriveInfo(root).AvailableFreeSpace;
        if (free < bytes) throw new IOException($"Недостаточно места на {root}: нужно {bytes:N0} байт, свободно {free:N0}.");
    }
    public static void RequireIdle(GameInstall install, bool writable = true)
    {
        if (install.Client == ClientKind.Steam) SteamLocator.FromManifest(install.ManifestPath);
        if (install.Client == ClientKind.Epic) EpicAdapter.Read(install.ManifestPath);
        // Idle launchers are allowed. Active download/staging markers and file handles block writes.
        if (Directory.Exists(Path.Combine(install.Root, ".egstore", "bps"))
            || Directory.Exists(Path.Combine(install.Root, ".egstore", "Pending"))
            || Directory.EnumerateFiles(install.Root, "*.ggpk.*", SearchOption.TopDirectoryOnly).Any(p => !p.Contains(".PoeAdaptive-", StringComparison.Ordinal)))
            throw new IOException("Клиент обновляет или проверяет ресурсы. Дождитесь завершения операции.");
        using var file = new FileStream(install.ResourcePath, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None);
    }
}
