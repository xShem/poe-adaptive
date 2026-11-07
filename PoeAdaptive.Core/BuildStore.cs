using System.Security.Cryptography;
using System.Text.Json;

namespace PoeAdaptive.Core;

public sealed record BuildState(string BuildId, string OriginalHash, string CurrentHash, int SchemaVersion = 1,
    string? InstallationKey = null, GameId? Game = null, ClientKind? Client = null, ResourceFormat? Format = null);
public sealed record PublishIntent(string BeforeHash, string AfterHash, int SchemaVersion = 1, string? InstallationKey = null, string? BuildId = null);
public enum PublishPhase { Copied, Journaled, Replaced, StateSaved }

/// <summary>Version-bound index snapshots and recoverable, atomic index publication.</summary>
public sealed class BuildStore
{
    public string DirectoryPath { get; }
    public string OriginalIndex => Path.Combine(DirectoryPath, install.Format == ResourceFormat.Ggpk ? "original.ggpk" : "original.index.bin");
    private string StatePath => Path.Combine(DirectoryPath, "state.json");
    private string PendingPath => Path.Combine(DirectoryPath, "pending.json");
    private readonly GameInstall install;
    private readonly Func<GameInstall, GameInstall> refresh;
    private readonly Action<PublishPhase>? checkpoint;

    public BuildStore(string dataRoot, SteamInstall install) : this(dataRoot, GameInstall.FromSteam(install)) { }

    public BuildStore(string dataRoot, GameInstall install, Func<GameInstall, GameInstall>? refresh = null, Action<PublishPhase>? checkpoint = null)
    {
        this.install = install;
        this.refresh = refresh ?? (i => i.Refresh());
        this.checkpoint = checkpoint;
        DirectoryPath = Path.Combine(dataRoot, "Backups", install.Key, install.BuildId);
    }

    public static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public BuildState? Load()
    {
        if (!File.Exists(StatePath)) return null;
        var state = JsonSerializer.Deserialize<BuildState>(File.ReadAllText(StatePath))
            ?? throw new InvalidDataException("Повреждены данные резервной копии.");
        if (state.SchemaVersion is not (1 or 2) || (state.SchemaVersion == 1 && install.Client != ClientKind.Steam)
            || (state.SchemaVersion == 2 && (state.InstallationKey != install.Key || state.Game != install.Game || state.Client != install.Client || state.Format != install.Format))
            || state.BuildId != install.BuildId || !File.Exists(OriginalIndex) || HashFile(OriginalIndex) != state.OriginalHash)
            throw new InvalidDataException("Резервная копия повреждена или относится к другой сборке.");
        return state;
    }

    public void Validate(string expectedHash)
    {
        if (refresh(install).BuildId != install.BuildId)
            throw new IOException("Игра обновилась. Выполните анализ заново.");
        if (HashFile(install.IndexPath) != expectedHash)
            throw new IOException("Ресурсы игры изменились. Выполните анализ заново; при сторонних изменениях используйте проверку файлов клиента.");
    }

    public FileStream Lock()
    {
        var parent = Path.GetDirectoryName(DirectoryPath)!;
        Directory.CreateDirectory(parent);
        return new FileStream(Path.Combine(parent, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Recover()
    {
        if (!File.Exists(PendingPath)) return;
        var intent = JsonSerializer.Deserialize<PublishIntent>(File.ReadAllText(PendingPath))
            ?? throw new InvalidDataException("Повреждён журнал операции.");
        if (intent.SchemaVersion is not (1 or 2) || (intent.SchemaVersion == 1 && install.Client != ClientKind.Steam)
            || (intent.SchemaVersion == 2 && (intent.InstallationKey != install.Key || intent.BuildId != install.BuildId)))
            throw new InvalidDataException("Журнал другой установки или версии. Запись остановлена.");
        var state = Load() ?? throw new InvalidDataException("Нет исходного индекса для восстановления операции.");
        var current = HashFile(install.IndexPath);
        if (current == intent.AfterHash) SaveJson(StatePath, state with { CurrentHash = current });
        else if (current != intent.BeforeHash)
            throw new IOException("Незавершённая операция и неизвестный индекс. Выполните проверку файлов клиента.");
        File.Delete(PendingPath);
    }

    public BuildState EnsureBaseline(string expectedHash)
    {
        Validate(expectedHash);
        Recover();
        var known = Load();
        if (known != null)
        {
            if (known.CurrentHash != expectedHash)
                throw new IOException("Индекс изменён вне PoeAdaptive. Выполните проверку файлов клиента.");
            return known;
        }
        Directory.CreateDirectory(DirectoryPath);
        FileSafety.RequireSpace(OriginalIndex, new FileInfo(install.IndexPath).Length + 64L * 1024 * 1024);
        var temporary = OriginalIndex + ".tmp";
        File.Copy(install.IndexPath, temporary, true);
        if (HashFile(temporary) != expectedHash) throw new IOException("Ресурсы изменились при копировании.");
        File.Move(temporary, OriginalIndex, true);
        if (HashFile(OriginalIndex) != expectedHash) throw new IOException("Индекс изменился при создании копии.");
        var state = new BuildState(install.BuildId, expectedHash, expectedHash, 2, install.Key, install.Game, install.Client, install.Format);
        SaveJson(StatePath, state);
        return state;
    }

    public void Publish(string preparedIndex, string expectedHash, Action guard)
    {
        Recover();
        var state = Load() ?? throw new InvalidOperationException("Сначала создайте план.");
        if (state.CurrentHash != expectedHash) throw new IOException("План устарел.");
        guard();
        Validate(expectedHash);
        FileSafety.RequireSpace(install.IndexPath, new FileInfo(preparedIndex).Length + 64L * 1024 * 1024);
        var after = HashFile(preparedIndex);
        var temp = install.IndexPath + ".PoeAdaptive-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = File.OpenRead(preparedIndex))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            if (HashFile(temp) != after) throw new IOException("Ошибка копирования нового индекса.");
            checkpoint?.Invoke(PublishPhase.Copied);
            SaveJson(PendingPath, new PublishIntent(expectedHash, after, 2, install.Key, install.BuildId));
            checkpoint?.Invoke(PublishPhase.Journaled);
            guard();
            Validate(expectedHash);
            File.Replace(temp, install.IndexPath, null);
            checkpoint?.Invoke(PublishPhase.Replaced);
            SaveJson(StatePath, state with { CurrentHash = after });
            checkpoint?.Invoke(PublishPhase.StateSaved);
            File.Delete(PendingPath);
            VersionTracker.Record(install, state with { CurrentHash = after });
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
