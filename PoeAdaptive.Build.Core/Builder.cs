using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PoeAdaptive.Build;

public sealed record SdkSpec(string Version, string Url, string Sha512)
{
    public static SdkSpec Pinned
    {
        get
        {
            using var stream = typeof(SdkSpec).Assembly.GetManifestResourceStream("PoeAdaptive.Build.Core.sdk.json")!;
            return JsonSerializer.Deserialize<SdkSpec>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }
}
public sealed record BuildOptions(string Source, string Output, bool InstallSdk = false, bool Offline = false, string? Dotnet = null, string? SdkCache = null);
public sealed record BuildResult(string Directory, string AppZip, string BuilderZip, string SourceZip);

public static class Processes
{
    public static async Task<string> Run(string file, IEnumerable<string> args, string directory, Action<string>? log, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        using var process = new Process { StartInfo = start };
        cancellation.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("Не удалось запустить " + file);
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var output = new StringBuilder();
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            { lock (output) output.AppendLine(line); log?.Invoke(line); }
        }
        var drainOut = Drain(process.StandardOutput); var drainError = Drain(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(drainOut, drainError);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(file)} завершился с кодом {process.ExitCode}. См. журнал.\n{output.ToString()[Math.Max(0, output.Length - 3000)..]}");
        return output.ToString().Trim();
    }
}

public static class SdkManager
{
    public static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeAdaptive", "Builder", "SDK");
    public static async Task<string?> Find(string? explicitPath, string cache, Action<string>? log, CancellationToken token)
    {
        foreach (var path in new[] { explicitPath, Path.Combine(cache, SdkSpec.Pinned.Version, "dotnet.exe"), "dotnet" }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            try
            {
                var list = await Processes.Run(path!, ["--list-sdks"], Path.GetTempPath(), null, token);
                if (list.Split('\n').Any(l => l.StartsWith(SdkSpec.Pinned.Version + " ", StringComparison.Ordinal))) return path;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { log?.Invoke(ex.Message); }
        }
        return null;
    }
    public static async Task VerifyDownload(string file, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA512.HashDataAsync(stream, token));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-512 SDK не совпадает с закреплённым значением Microsoft. Загрузка не будет использована.");
    }
    public static void ExtractArchive(string zip, string destination, CancellationToken token)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('\\', '/')));
            if (entry.Name.Length == 0 && path.TrimEnd(Path.DirectorySeparatorChar).Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый путь SDK.");
            if (entry.Name.Length == 0) Directory.CreateDirectory(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path); }
        }
    }
    public static async Task<string> Bootstrap(string cache, bool offline, Action<string>? log, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Автоматическая установка SDK предназначена для Windows x64.");
        var spec = SdkSpec.Pinned; Directory.CreateDirectory(cache);
        using var gate = new FileStream(Path.Combine(cache, "sdk.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var cachedHost = Path.Combine(cache, spec.Version, "dotnet.exe");
        if (File.Exists(cachedHost))
        {
            if (await Processes.Run(cachedHost, ["--version"], cache, log, token) != spec.Version) throw new IOException("Управляемый SDK повреждён. Выберите новый каталог кэша.");
            return cachedHost;
        }
        var zip = Path.Combine(cache, "dotnet-sdk-" + spec.Version + "-win-x64.zip");
        if (!File.Exists(zip))
        {
            if (offline) throw new IOException("SDK отсутствует в кэше. Для первой подготовки нужен интернет.");
            log?.Invoke("Загрузка закреплённого .NET SDK с builds.dotnet.microsoft.com; проверка SHA-512.");
            var partial = zip + ".partial";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                await using (var input = await client.GetStreamAsync(spec.Url, token))
                await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None)) await input.CopyToAsync(output, token);
                await VerifyDownload(partial, spec.Sha512, token); File.Move(partial, zip);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        await VerifyDownload(zip, spec.Sha512, token);
        var temp = Path.Combine(cache, ".extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp); token.ThrowIfCancellationRequested();
            await Task.Run(() => ExtractArchive(zip, temp, token), token);
            var version = await Processes.Run(Path.Combine(temp, "dotnet.exe"), ["--version"], temp, log, token);
            if (version != spec.Version) throw new InvalidDataException("Неподходящая версия SDK.");
            Directory.Move(temp, Path.Combine(cache, spec.Version));
            return Path.Combine(cache, spec.Version, "dotnet.exe");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}

public static class BuildPipeline
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".idea", "bin", "obj", "artifacts", "publish", "work", "output", "release", "node_modules" };
    public static string Version(string source) => XDocument.Load(Path.Combine(source, "PoeRedux", "PoeRedux.csproj")).Descendants("Version").Single().Value;
    public static void RequireSpace(string path, long bytes, Func<string, long>? available = null)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        if ((available?.Invoke(root) ?? new DriveInfo(root).AvailableFreeSpace) < bytes) throw new IOException("Недостаточно места: требуется как минимум " + bytes / 1073741824.0 + " ГиБ.");
    }
    public static IEnumerable<string> SourceFiles(string source, string output)
    {
        IEnumerable<string> Walk(string folder)
        {
            foreach (var file in Directory.EnumerateFiles(folder))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && !file.EndsWith(".user", StringComparison.OrdinalIgnoreCase) && !(Path.GetFileName(file).Contains("oo2core", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) yield return file;
            foreach (var child in Directory.EnumerateDirectories(folder))
                if (!Excluded.Contains(Path.GetFileName(child)) && !Path.GetFileName(child).StartsWith(".PoeAdaptive-builder-", StringComparison.Ordinal)
                    && !string.Equals(child, output, StringComparison.OrdinalIgnoreCase) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (var file in Walk(child)) yield return file;
        }
        return Walk(Path.GetFullPath(source)).Order(StringComparer.Ordinal);
    }
    public static async Task<BuildResult> Run(BuildOptions options, Action<string> log, CancellationToken token)
    {
        var source = Path.GetFullPath(options.Source); var output = Path.GetFullPath(options.Output);
        var version = Version(source);
        if (!File.Exists(Path.Combine(source, "PoeRedux.slnx"))) throw new InvalidDataException("Выберите корень исходников PoE Adaptive.");
        using (var global = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "global.json"))))
            if (global.RootElement.GetProperty("sdk").GetProperty("version").GetString() != SdkSpec.Pinned.Version) throw new InvalidDataException("Эта версия Builder не соответствует SDK проекта. Скачайте Builder того же выпуска.");
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || source.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Результат должен находиться отдельно от корня исходников и его родителей.");
        Directory.CreateDirectory(output);
        var logPath = Path.Combine(output, "build-log-" + Guid.NewGuid().ToString("N") + ".txt");
        var progress = log; var logGate = new object();
        log = line => { lock (logGate) File.AppendAllText(logPath, line + Environment.NewLine); progress(line); };
        RequireSpace(output, 5L * 1024 * 1024 * 1024);
        var cache = options.SdkCache ?? SdkManager.DefaultCache;
        Directory.CreateDirectory(cache);
        log("Проверка .NET SDK " + SdkSpec.Pinned.Version);
        var dotnet = await SdkManager.Find(options.Dotnet, cache, log, token);
        if (dotnet == null)
        {
            if (!options.InstallSdk) throw new IOException("Закреплённый .NET SDK не найден. Разрешите установку SDK в пользовательский каталог или установите его самостоятельно.");
            RequireSpace(cache, 3L * 1024 * 1024 * 1024);
            dotnet = await SdkManager.Bootstrap(cache, options.Offline, log, token);
        }
        log("SDK: " + dotnet);
        log(options.Offline ? "Сборка только из кэша. Недостающие пакеты вызовут ошибку." : "Restore может загружать NuGet-пакеты и runtime packs. После подготовки доступен режим --offline.");
        var job = Path.Combine(output, ".PoeAdaptive-builder-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        var snapshot = Path.Combine(job, "source"); Directory.CreateDirectory(snapshot);
        string? commit = null; bool? dirty = null;
        try
        {
            try
            {
                commit = await Processes.Run("git", ["rev-parse", "HEAD"], source, null, token);
                var statusArgs = new List<string> { "status", "--porcelain", "--untracked-files=normal" };
                var relativeOutput = Path.GetRelativePath(source, output);
                if (!relativeOutput.StartsWith("..", StringComparison.Ordinal)) statusArgs.AddRange(["--", ".", ":(exclude)" + relativeOutput.Replace('\\', '/')]);
                dirty = (await Processes.Run("git", statusArgs, source, null, token)).Length != 0;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { log("Git недоступен; идентификатор исходников будет вычислен по файлам."); }
            log("Создание отдельного снимка исходников.");
            var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in SourceFiles(source, output))
            {
                token.ThrowIfCancellationRequested(); var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(snapshot, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
                await using var input = File.OpenRead(target); manifest[relative.Replace('\\', '/')] = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            }
            var manifestJson = JsonSerializer.Serialize(manifest);
            var sourceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson)));
            var restoreArgs = new List<string> { "restore", "PoeRedux.slnx", "--locked-mode" };
            if (options.Offline) { var empty = Path.Combine(job, "offline-feed"); Directory.CreateDirectory(empty); restoreArgs.AddRange(["--source", empty]); }
            log("Восстановление зависимостей."); await Processes.Run(dotnet, restoreArgs, snapshot, log, token);
            log("Сборка всех проектов."); await Processes.Run(dotnet, ["build", "PoeRedux.slnx", "-c", "Release", "--no-restore"], snapshot, log, token);
            log("Выполнение консольных проверок.");
            var checks = await Processes.Run(dotnet, ["run", "--project", "PoeAdaptive.Tests", "-c", "Release", "--no-build", "--no-restore"], snapshot, log, token);
            if (!checks.Contains("checks passed.", StringComparison.Ordinal)) throw new IOException("Раннер не подтвердил выполнение проверок.");
            var stage = Path.Combine(job, "packages"); Directory.CreateDirectory(stage);
            var app = Path.Combine(stage, "app"); var builder = Path.Combine(stage, "builder");
            foreach (var (project, folder) in new[] { ("PoeRedux/PoeRedux.csproj", app), ("PoeAdaptive.Diagnostics/PoeAdaptive.Diagnostics.csproj", app), ("PoeAdaptive.Builder/PoeAdaptive.Builder.csproj", builder) })
            {
                log("Публикация " + project);
                await Processes.Run(dotnet, ["publish", project, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "--no-restore", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", folder], snapshot, log, token);
            }
            var info = new { schemaVersion = 1, version, sdk = SdkSpec.Pinned.Version, target = "win-x64", selfContained = true, sourceCommit = commit, localChanges = dirty, sourceId, gameClientValidation = false, oodleIncluded = false };
            var metadata = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            foreach (var folder in new[] { app, builder })
            {
                File.WriteAllText(Path.Combine(folder, "build-info.json"), metadata);
                foreach (var file in new[] { "README.md", "THIRD_PARTY.md", "LICENSE", "LICENSE-AGPL-3.0.txt", "Build.cmd", "Build.ps1" }) File.Copy(Path.Combine(snapshot, file), Path.Combine(folder, file), true);
                CopyDirectory(Path.Combine(snapshot, "docs"), Path.Combine(folder, "docs"));
                var runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                foreach (var notice in new[] { "LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "ThirdPartyNotices.txt" })
                    if (File.Exists(Path.Combine(runtime, notice))) File.Copy(Path.Combine(runtime, notice), Path.Combine(folder, "Runtime-" + notice), true);
            }
            File.WriteAllText(Path.Combine(stage, "source-manifest.json"), manifestJson);
            // Package only the source snapshot that was compiled; skip all generated bin/obj output.
            var sourceZip = Path.Combine(stage, $"PoeAdaptive-{version}-source.zip");
            using (var archive = ZipFile.Open(sourceZip, ZipArchiveMode.Create))
                foreach (var relative in manifest.Keys) archive.CreateEntryFromFile(Path.Combine(snapshot, relative.Replace('/', Path.DirectorySeparatorChar)), "PoeAdaptive/" + relative, CompressionLevel.Optimal);
            foreach (var folder in new[] { app, builder }) { Directory.CreateDirectory(Path.Combine(folder, "source")); File.Copy(sourceZip, Path.Combine(folder, "source", Path.GetFileName(sourceZip))); }
            var appZip = Path.Combine(stage, $"PoeAdaptive-{version}-win-x64.zip");
            var builderZip = Path.Combine(stage, $"PoeAdaptive-Builder-{version}-win-x64.zip");
            token.ThrowIfCancellationRequested();
            ZipFile.CreateFromDirectory(app, appZip); ZipFile.CreateFromDirectory(builder, builderZip);
            var sums = new List<string>();
            foreach (var file in new[] { appZip, builderZip, sourceZip }) { await using var input = File.OpenRead(file); sums.Add(Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant() + "  " + Path.GetFileName(file)); }
            File.WriteAllLines(Path.Combine(stage, "SHA256SUMS.txt"), sums);
            File.WriteAllText(Path.Combine(stage, "build-info.json"), metadata);
            token.ThrowIfCancellationRequested();
            var finished = Path.Combine(output, $"PoeAdaptive-{version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.Move(stage, finished); log("Готово: " + finished);
            return new(finished, Path.Combine(finished, Path.GetFileName(appZip)), Path.Combine(finished, Path.GetFileName(builderZip)), Path.Combine(finished, Path.GetFileName(sourceZip)));
        }
        finally { if (Directory.Exists(job)) Directory.Delete(job, true); }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var target = Path.Combine(destination, Path.GetRelativePath(source, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target, true); }
    }
}

public static class CommandLine
{
    public static async Task<int> Run(string[] args)
    {
        string Value(string key, string fallback) { var index = Array.IndexOf(args, key); return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Нет значения " + key); }
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try
        {
            if (args.Contains("--bootstrap-sdk"))
            {
                Console.WriteLine(await SdkManager.Bootstrap(Value("--sdk-cache", SdkManager.DefaultCache), args.Contains("--offline"), Console.WriteLine, cancel.Token));
                return 0;
            }
            await BuildPipeline.Run(new(Value("--source", Environment.CurrentDirectory), Value("--output", Path.Combine(Environment.CurrentDirectory, "artifacts")), args.Contains("--install-sdk"), args.Contains("--offline"), Value("--dotnet", "dotnet"), Value("--sdk-cache", SdkManager.DefaultCache)), Console.WriteLine, cancel.Token);
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Сборка отменена. Готовый результат не опубликован."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
