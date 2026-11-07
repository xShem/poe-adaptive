using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LibBundle3;
using LibBundle3.Records;
using PoeAdaptive.Core;
using PoeRedux.Models;
using PoeRedux.Patches;

namespace PoeRedux.Services;

public sealed record Profile(string[] Patches, double Zoom, List<ColorModsOption> Colors,
    bool AutoApply, string LastBuild, int SchemaVersion = 2);
public sealed record SavedProfile(int SchemaVersion, string InstallationKey, GameId Game, ClientKind Client, Profile Profile);
public sealed record PatchResult(string Id, string Name, int ChangedFiles);
public sealed record AnalysisReport(GameId Game, ClientKind Client, ResourceFormat Format, int SchemaVersion, string BuildId, string BeforeHash, string OutputHash,
    DateTimeOffset CreatedAt, List<PatchResult> Results);

public sealed class PreparedPatch : IDisposable
{
    public required GameInstall Install { get; init; }
    public required string DirectoryPath { get; init; }
    public required string IndexPath { get; init; }
    public required AnalysisReport Report { get; init; }
    public required Profile Profile { get; init; }
    public required string ResourceHint { get; init; }
    public required string RulesHash { get; init; }
    public required Dictionary<string, string> Dependencies { get; init; }
    public required Dictionary<string, string> StagedHashes { get; init; }
    public bool CanApply => Report.Results.Count != 0 && Report.Results.All(x => x.ChangedFiles > 0);
    public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
}

/// <summary>Counts real byte changes; all writes target the isolated staging index.</summary>
public static class PatchIO
{
    [ThreadStatic] private static HashSet<string>? changed;
    [ThreadStatic] private static FileRecord? lastRecord;
    [ThreadStatic] private static ReadOnlyMemory<byte> lastContent;
    public static void Begin() { changed = new(StringComparer.Ordinal); lastRecord = null; lastContent = default; }
    public static int End() { var count = changed?.Count ?? 0; changed = null; lastRecord = null; lastContent = default; return count; }
    public static ReadOnlyMemory<byte> Read(FileRecord record)
    {
        lastRecord = record;
        return lastContent = record.Read();
    }
    public static void Write(FileRecord record, byte[] bytes)
    {
        if (changed == null) throw new InvalidOperationException("Патчи можно выполнять только внутри анализатора.");
        var before = (lastRecord == record ? lastContent : record.Read()).Span;
        static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> data) =>
            data.StartsWith(new byte[] { 0xff, 0xfe }) ? data[2..] : data;
        if (WithoutBom(before).SequenceEqual(WithoutBom(bytes))) return;
        record.Write(bytes);
        lastRecord = null;
        lastContent = default;
        changed.Add(record.Path ?? record.PathHash.ToString());
    }
}

internal sealed class IsolatedFactory(string originalRoot, string stagingRoot) : IBundleFactory
{
    public static string SafePath(string root, string path)
    {
        var result = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый путь ресурса.");
        return result;
    }
    public Bundle GetBundle(BundleRecord record)
    {
        var staged = SafePath(stagingRoot, record.Path);
        var exists = File.Exists(staged);
        return new Bundle(new FileStream(exists ? staged : SafePath(originalRoot, record.Path),
            FileMode.Open, exists ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read), false, record);
    }
    public Stream CreateBundle(string path)
    {
        if (!path.StartsWith("PoeAdaptive/", StringComparison.Ordinal)) throw new InvalidDataException("Запись вне каталога патча.");
        var target = SafePath(stagingRoot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
    }
    public bool DeleteBundle(string path) => throw new NotSupportedException("Удаление игровых ресурсов запрещено.");
}

internal sealed class StagingIndex(Stream stream, IBundleFactory factory, string session)
    : LibBundle3.Index(stream, false, true, factory)
{
    protected override Bundle CreateBundle(string bundlePath) =>
        base.CreateBundle($"PoeAdaptive/{session}/{Path.GetFileName(bundlePath)}");
}

public static class PatchCatalog
{
    public static IPatch[] Create(string appId) => Create(Games.FromLegacy(appId));
    public static IPatch[] Create(GameId game) => game == GameId.PoE1
        ? [new Camera(), new Minimap(), new ColorMods(), new MonsterHP(), new Fog(),
            new EnvironmentParticles(), new Shadow(), new Light(), new Corpse(), new Delirium(),
            new Particles(), new Effects(), new BlackScreen()]
        : [new Camera(), new Minimap(), new ColorMods2(), new MonsterHP(), new AtlasFog(), new Fog(),
            new Rain(), new Clouds(), new EnvironmentParticles2(), new Shadow(), new Light(), new Delirium(),
            new Particles(), new Effects(), new BlackScreen()];

    public static string Id(IPatch patch) => patch.GetType().Name;
    public static List<ColorModsOption> Colors(IPatch[] patches) => patches.SelectMany(p => p switch
    {
        ColorMods c => c.ColorModsOptions,
        ColorMods2 c => c.ColorModsOptions,
        _ => new List<ColorModsOption>()
    }).Select(c => c.Copy()).ToList();
}

public sealed class AdaptivePatcher
{
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeAdaptive");
    private readonly string dataRoot;
    public AdaptivePatcher(string? dataRoot = null) { this.dataRoot = dataRoot ?? DataRoot; }
    public string RulesPath => Path.Combine(dataRoot, "rules.json");
    private static IntPtr nativeHandle;
    private static bool resolverSet;
    private static string? nativeHash;
    private static readonly object nativeLock = new();

    public static void EnsureClosed()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!process.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    // The standalone launcher shares the executable name. Its rendering modules distinguish the running game.
                    var game = process.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
                        || m.ModuleName.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase));
                    if (!game && process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.Contains("Path of Exile", StringComparison.OrdinalIgnoreCase)
                        && (process.MainWindowTitle.Contains("Launcher", StringComparison.OrdinalIgnoreCase) || process.MainWindowTitle.Contains("Patching", StringComparison.OrdinalIgnoreCase))) continue;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                throw new IOException("Закройте запущенную игру перед работой с ресурсами. Бездействующий лаунчер допускается, если его состояние подтверждено.");
            }
        }
    }

    private static void ConfigureOodle(GameInstall install)
    {
        lock (nativeLock)
        {
            var paths = Directory.EnumerateFiles(install.Root, "oo2core*.dll", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase).Concat([Path.Combine(AppContext.BaseDirectory, "oo2core.dll")]).Where(File.Exists).ToArray();
            if (paths.Length == 0) throw new FileNotFoundException("Не найдена Oodle DLL. Используйте совместимую локальную oo2core DLL выбранной игры; она не включена в пакет.");
            var path = paths[0]; var hash = BuildStore.HashFile(path);
            if (nativeHandle != IntPtr.Zero)
            {
                if (nativeHash != hash) throw new IOException("Для этой установки выбрана другая Oodle DLL. Перезапустите PoE Adaptive перед сменой библиотеки: .NET кэширует нативные точки входа.");
                return;
            }
            if (!NativeLibrary.TryLoad(Path.GetFullPath(path), out nativeHandle)) throw new IOException("Oodle DLL не загружается. Нужна совместимая Windows x64 библиотека.");
            foreach (var symbol in new[] { "OodleLZ_Compress", "OodleLZ_Decompress", "OodleLZ_GetCompressedBufferSizeNeeded", "OodleLZ_GetCompressScratchMemBound", "OodleLZDecoder_MemorySizeNeeded" })
                if (!NativeLibrary.TryGetExport(nativeHandle, symbol, out _)) { NativeLibrary.Free(nativeHandle); nativeHandle = IntPtr.Zero; throw new IOException("Несовместимая Oodle DLL."); }
            nativeHash = hash;
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(LibBundle3.Index).Assembly, (name, _, _) => name == "oo2core" ? nativeHandle : IntPtr.Zero);
                resolverSet = true;
            }
        }
    }

    public Profile? LoadProfile(GameInstall install)
    {
        var path = Path.Combine(dataRoot, "Profiles", install.Key + ".json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("Patches", out _))
        {
            if (install.Client != ClientKind.Steam) throw new InvalidDataException("Старый профиль не привязан к клиенту. Исходный файл сохранён.");
            return JsonSerializer.Deserialize<Profile>(doc.RootElement.GetRawText());
        }
        var saved = JsonSerializer.Deserialize<SavedProfile>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("Повреждён профиль.");
        if (saved.SchemaVersion != 2 || saved.InstallationKey != install.Key || saved.Game != install.Game || saved.Client != install.Client)
            throw new InvalidDataException("Профиль другой установки или неизвестной версии. Исходный файл сохранён.");
        return saved.Profile;
    }
    public void SaveProfile(GameInstall install, Profile profile)
    {
        var path = Path.Combine(dataRoot, "Profiles", install.Key + ".json");
        if (File.Exists(path))
        {
            using var old = JsonDocument.Parse(File.ReadAllText(path));
            if (old.RootElement.TryGetProperty("Patches", out _) && !File.Exists(path + ".v0.1.0")) File.Copy(path, path + ".v0.1.0");
        }
        BuildStore.SaveJson(path, new SavedProfile(2, install.Key, install.Game, install.Client, profile));
    }

    public PreparedPatch Analyze(GameInstall install, Profile profile, Action<string>? progress = null)
    {
        EnsureClosed();
        if (!double.IsFinite(profile.Zoom) || profile.Zoom < 1.2 || profile.Zoom > 2.4)
            throw new InvalidDataException("Масштаб камеры должен быть от 1.2 до 2.4.");
        install = install.Refresh();
        FileSafety.RequireIdle(install, false);
        ResourceProbe.Inspect(install.ResourcePath);
        FileSafety.RequireSpace(dataRoot, ResourceAdapters.EstimateAdditionalSpace(install));
        ConfigureOodle(install);
        var store = new BuildStore(dataRoot, install);
        using var operationLock = store.Lock();
        store.Recover();
        var resourceHint = VersionTracker.Hint(install);
        var before = BuildStore.HashFile(install.IndexPath);
        var known = store.Load();
        if (known != null && before == known.OriginalHash && before != known.CurrentHash)
        {
            // Steam verification restored this build's exact original index.
            BuildStore.SaveJson(Path.Combine(store.DirectoryPath, "state.json"), known with { CurrentHash = before });
        }
        var work = Path.Combine(dataRoot, "Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var stagedIndex = Path.Combine(work, "_.index.bin");
        try
        {
            var adapter = ResourceAdapters.For(install.Format);
            var dependencies = VersionTracker.Bundles(install);
            var rulesHash = File.Exists(RulesPath) ? BuildStore.HashFile(RulesPath) : "absent";
            var results = new List<PatchResult>();
            var rules = File.Exists(RulesPath)
                ? TextRules.Load(RulesPath)
                : [];
            foreach (var rule in rules) TextRules.Compile(rule);
            var selected = PatchCatalog.Create(install.Game).Where(p => profile.Patches.Contains(PatchCatalog.Id(p))).ToArray();
            if (selected.Length != profile.Patches.Distinct().Count() || selected.Length == 0)
                throw new InvalidDataException("Выберите поддерживаемые функции для этой игры.");
            store.EnsureBaseline(before);
            var baseline = store.OriginalIndex;
            using (var session = adapter.Stage(install, baseline, work))
            {
                var index = session.Index;
                if (index.Bundles.Span.ToArray().Any(b => b.Path.StartsWith("LibGGPK3/", StringComparison.OrdinalIgnoreCase)
                    || b.Path.StartsWith("PoeAdaptive/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Исходная база уже изменена другим патчером. Сначала выполните проверку файлов клиента.");
                var tree = index.BuildTree(true);
                foreach (var patch in selected)
                {
                    progress?.Invoke("Анализ: " + patch.Name);
                    if (patch is Camera c) c.ZoomLevel = profile.Zoom;
                    if (patch is ColorMods c1) c1.ColorModsOptions = profile.Colors.Select(c => c.Copy()).ToList();
                    if (patch is ColorMods2 c2) c2.ColorModsOptions = profile.Colors.Select(c => c.Copy()).ToList();
                    // Patch instances are recreated for every scan: no stale FileNode lists.
                    PatchIO.Begin();
                    var overrides = rules.Where(r => r.TargetGame == install.Game && r.PatchId == PatchCatalog.Id(patch)).ToArray();
                    if (overrides.Length == 0) patch.Apply(tree);
                    else ApplyRules(index, overrides);
                    var count = PatchIO.End();
                    index.Save();
                    results.Add(new(PatchCatalog.Id(patch), patch.Name, count));
                }
            }
            store.Validate(before);
            var output = adapter.Complete(install, baseline, work);
            adapter.Verify(output);
            foreach (var (path, hash) in dependencies)
                if (BuildStore.HashFile(path) != hash) throw new IOException("Внешние ресурсы изменились во время анализа.");
            var stagedHashes = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(work, p), BuildStore.HashFile);
            if (VersionTracker.Hint(install) != resourceHint) throw new IOException("Ресурсы изменились во время анализа.");
            var report = new AnalysisReport(install.Game, install.Client, install.Format, 2, install.BuildId, before,
                BuildStore.HashFile(output), DateTimeOffset.UtcNow, results);
            BuildStore.SaveJson(Path.Combine(dataRoot, "Reports", install.Key + "-latest.json"), report);
            return new() { Install = install, DirectoryPath = work, IndexPath = output, Report = report, Profile = profile, ResourceHint = resourceHint, RulesHash = rulesHash, Dependencies = dependencies, StagedHashes = stagedHashes };
        }
        catch
        {
            PatchIO.End();
            Directory.Delete(work, true);
            throw;
        }
    }

    private static void ApplyRules(LibBundle3.Index index, TextRule[] rules)
    {
        foreach (var rule in rules)
        {
            var regex = TextRules.Compile(rule);
            int total = 0;
            foreach (var file in index.Files.Values.Where(f => f.Path != null && TextRules.Targets(rule, f.Path)))
            {
                var bytes = PatchIO.Read(file);
                var encoding = rule.EncodingName switch { "ascii" => Encoding.ASCII, "utf-8" => Encoding.UTF8, _ => Encoding.Unicode };
                var text = encoding.GetString(bytes.Span);
                var (next, count) = TextRules.Transform(regex, rule, text);
                total = checked(total + count);
                if (total > rule.MaxMatches) throw new InvalidDataException($"Правило {rule.PatchId}: слишком много совпадений.");
                if (next != text) PatchIO.Write(file, encoding.GetBytes(next));
            }
            if (total < rule.MinMatches) throw new InvalidDataException($"Правило {rule.PatchId}: ожидаемые данные не найдены.");
        }
    }

    public void Apply(PreparedPatch plan)
    {
        if (!plan.CanApply) throw new InvalidOperationException("Есть функции без найденных изменений. Снимите их выбор и повторите анализ.");
        if ((File.Exists(RulesPath) ? BuildStore.HashFile(RulesPath) : "absent") != plan.RulesHash) throw new IOException("Правила изменились после анализа. Постройте новый план.");
        var store = new BuildStore(dataRoot, plan.Install);
        using var operationLock = store.Lock();
        EnsureClosed();
        store.Validate(plan.Report.BeforeHash);
        if (BuildStore.HashFile(plan.IndexPath) != plan.Report.OutputHash) throw new IOException("Подготовленный индекс повреждён.");
        FileSafety.RequireIdle(plan.Install);
        if (VersionTracker.Hint(plan.Install) != plan.ResourceHint) throw new IOException("План устарел: изменились ресурсы.");
        foreach (var (path, hash) in plan.Dependencies)
            if (BuildStore.HashFile(path) != hash) throw new IOException("Внешний bundle изменился. План устарел.");
        ConfigureOodle(plan.Install);
        ResourceAdapters.For(plan.Install.Format).Verify(plan.IndexPath);
        foreach (var (relative, hash) in plan.StagedHashes)
            if (BuildStore.HashFile(IsolatedFactory.SafePath(plan.DirectoryPath, relative)) != hash) throw new IOException("Подготовленные ресурсы повреждены. Выполните анализ заново.");
        FileSafety.RequireSpace(plan.Install.ResourcePath, plan.StagedHashes.Keys.Sum(p => new FileInfo(Path.Combine(plan.DirectoryPath, p)).Length) + 64L * 1024 * 1024);
        var stagedBundles = Path.Combine(plan.DirectoryPath, "PoeAdaptive");
        if (plan.Install.Format == ResourceFormat.ExternalBundles && Directory.Exists(stagedBundles))
            foreach (var source in Directory.EnumerateFiles(stagedBundles, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(plan.DirectoryPath, source);
                var target = IsolatedFactory.SafePath(Path.GetDirectoryName(plan.Install.IndexPath)!, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                if (BuildStore.HashFile(source) != BuildStore.HashFile(target)) throw new IOException("Ошибка проверки нового bundle.");
            }
        store.Publish(plan.IndexPath, plan.Report.BeforeHash, () => { EnsureClosed(); FileSafety.RequireIdle(plan.Install); });
        SaveProfile(plan.Install, plan.Profile with { LastBuild = plan.Install.BuildId });
    }

    public void Restore(GameInstall install)
    {
        install = install.Refresh();
        var store = new BuildStore(dataRoot, install);
        using var operationLock = store.Lock();
        EnsureClosed();
        store.Recover();
        var state = store.Load() ?? throw new IOException("Для текущей сборки нет исходного индекса.");
        store.Publish(store.OriginalIndex, state.CurrentHash, () => { EnsureClosed(); FileSafety.RequireIdle(install); });
        var profile = LoadProfile(install);
        if (profile != null) SaveProfile(install, profile with { AutoApply = false });
    }
}
