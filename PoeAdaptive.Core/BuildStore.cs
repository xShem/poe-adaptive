using System.Security.Cryptography;
using System.Text.Json;

namespace PoeAdaptive.Core;

public sealed record BuildState(string BuildId, string OriginalHash, string CurrentHash);
public sealed record PublishIntent(string BeforeHash, string AfterHash);

/// <summary>Version-bound index snapshots and recoverable, atomic index publication.</summary>
public sealed class BuildStore
{
    public string DirectoryPath { get; }
    public string OriginalIndex => Path.Combine(DirectoryPath, "original.index.bin");
    private string StatePath => Path.Combine(DirectoryPath, "state.json");
    private string PendingPath => Path.Combine(DirectoryPath, "pending.json");
    private readonly SteamInstall install;

    public BuildStore(string dataRoot, SteamInstall install)
    {
        this.install = install;
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
        if (state.BuildId != install.BuildId || !File.Exists(OriginalIndex) || HashFile(OriginalIndex) != state.OriginalHash)
            throw new InvalidDataException("Резервная копия повреждена или относится к другой сборке.");
        return state;
    }

    public void Validate(string expectedHash)
    {
        if (install.Refresh().BuildId != install.BuildId)
            throw new IOException("Игра обновилась. Выполните анализ заново.");
        if (HashFile(install.IndexPath) != expectedHash)
            throw new IOException("Ресурсы игры изменились. Выполните анализ заново; при сторонних изменениях используйте проверку файлов Steam.");
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
        var state = Load() ?? throw new InvalidDataException("Нет исходного индекса для восстановления операции.");
        var current = HashFile(install.IndexPath);
        if (current == intent.AfterHash) SaveJson(StatePath, state with { CurrentHash = current });
        else if (current != intent.BeforeHash)
            throw new IOException("Незавершённая операция и неизвестный индекс. Выполните проверку файлов Steam.");
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
                throw new IOException("Индекс изменён вне PoeAdaptive. Выполните проверку файлов Steam.");
            return known;
        }
        Directory.CreateDirectory(DirectoryPath);
        File.Copy(install.IndexPath, OriginalIndex, true);
        if (HashFile(OriginalIndex) != expectedHash) throw new IOException("Индекс изменился при создании копии.");
        var state = new BuildState(install.BuildId, expectedHash, expectedHash);
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
        var after = HashFile(preparedIndex);
        var temp = install.IndexPath + ".PoeAdaptive-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = File.OpenRead(preparedIndex))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            if (HashFile(temp) != after) throw new IOException("Ошибка копирования нового индекса.");
            SaveJson(PendingPath, new PublishIntent(expectedHash, after));
            guard();
            Validate(expectedHash);
            File.Replace(temp, install.IndexPath, null);
            SaveJson(StatePath, state with { CurrentHash = after });
            File.Delete(PendingPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
