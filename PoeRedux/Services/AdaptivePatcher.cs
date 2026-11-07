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
    bool AutoApply, string LastBuild);
public sealed record PatchResult(string Id, string Name, int ChangedFiles);
public sealed record AnalysisReport(string AppId, string BuildId, string BeforeHash, string OutputHash,
    DateTimeOffset CreatedAt, List<PatchResult> Results);

public sealed class PreparedPatch : IDisposable
{
    public required SteamInstall Install { get; init; }
    public required string DirectoryPath { get; init; }
    public required string IndexPath { get; init; }
    public required AnalysisReport Report { get; init; }
    public required Profile Profile { get; init; }
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
    public static IPatch[] Create(string appId) => appId == "238960"
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
    public static string RulesPath => Path.Combine(DataRoot, "rules.json");
    private static IntPtr nativeHandle;
    private static bool resolverSet;

    public static void EnsureClosed()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
                if (process.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Закройте PoE 1 и PoE 2 перед работой с ресурсами.");
        }
    }

    private static void ConfigureOodle(SteamInstall install)
    {
        if (!resolverSet)
        {
            NativeLibrary.SetDllImportResolver(typeof(LibBundle3.Index).Assembly,
                (name, _, _) => name == "oo2core" ? nativeHandle : IntPtr.Zero);
            resolverSet = true;
        }
        if (nativeHandle != IntPtr.Zero) return;
        var paths = Directory.EnumerateFiles(install.Root, "oo2core*.dll", SearchOption.TopDirectoryOnly)
            .Concat([Path.Combine(AppContext.BaseDirectory, "oo2core.dll")]);
        foreach (var path in paths.Where(File.Exists))
            if (NativeLibrary.TryLoad(Path.GetFullPath(path), out nativeHandle)) return;
        throw new FileNotFoundException("Не найдена совместимая Oodle DLL. Укажите oo2core.dll рядом с PoeAdaptive.exe; библиотека не включена в эту сборку. Подробности в README.");
    }

    public Profile? LoadProfile(SteamInstall install)
    {
        var path = Path.Combine(DataRoot, "Profiles", install.Key + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(path)) : null;
    }
    public void SaveProfile(SteamInstall install, Profile profile) =>
        BuildStore.SaveJson(Path.Combine(DataRoot, "Profiles", install.Key + ".json"), profile);

    public PreparedPatch Analyze(SteamInstall install, Profile profile, Action<string>? progress = null)
    {
        EnsureClosed();
        if (!double.IsFinite(profile.Zoom) || profile.Zoom < 1.2 || profile.Zoom > 2.4)
            throw new InvalidDataException("Масштаб камеры должен быть от 1.2 до 2.4.");
        install = install.Refresh();
        ConfigureOodle(install);
        var store = new BuildStore(DataRoot, install);
        using var operationLock = store.Lock();
        store.Recover();
        var before = BuildStore.HashFile(install.IndexPath);
        var known = store.Load();
        if (known != null && before == known.OriginalHash && before != known.CurrentHash)
        {
            // Steam verification restored this build's exact original index.
            BuildStore.SaveJson(Path.Combine(store.DirectoryPath, "state.json"), known with { CurrentHash = before });
        }
        var work = Path.Combine(DataRoot, "Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var stagedIndex = Path.Combine(work, "_.index.bin");
        try
        {
            File.Copy(known == null ? install.IndexPath : store.OriginalIndex, stagedIndex);
            var results = new List<PatchResult>();
            var rules = File.Exists(RulesPath)
                ? JsonSerializer.Deserialize<List<TextRule>>(File.ReadAllText(RulesPath)) ?? throw new InvalidDataException("Пустой rules.json.")
                : [];
            foreach (var rule in rules) TextRules.Compile(rule);
            var selected = PatchCatalog.Create(install.AppId).Where(p => profile.Patches.Contains(PatchCatalog.Id(p))).ToArray();
            if (selected.Length != profile.Patches.Distinct().Count() || selected.Length == 0)
                throw new InvalidDataException("Выберите поддерживаемые функции для этой игры.");
            using (var index = new StagingIndex(new FileStream(stagedIndex, FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
                new IsolatedFactory(Path.GetDirectoryName(install.IndexPath)!, work), Guid.NewGuid().ToString("N")))
            {
                if (index.Bundles.Span.ToArray().Any(b => b.Path.StartsWith("LibGGPK3/", StringComparison.OrdinalIgnoreCase)
                    || b.Path.StartsWith("PoeAdaptive/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Исходная база уже изменена другим патчером. Сначала выполните проверку файлов Steam.");
                store.EnsureBaseline(before);
                var tree = index.BuildTree(true);
                foreach (var patch in selected)
                {
                    progress?.Invoke("Анализ: " + patch.Name);
                    if (patch is Camera c) c.ZoomLevel = profile.Zoom;
                    if (patch is ColorMods c1) c1.ColorModsOptions = profile.Colors.Select(c => c.Copy()).ToList();
                    if (patch is ColorMods2 c2) c2.ColorModsOptions = profile.Colors.Select(c => c.Copy()).ToList();
                    // Patch instances are recreated for every scan: no stale FileNode lists.
                    PatchIO.Begin();
                    var overrides = rules.Where(r => r.AppId == install.AppId && r.PatchId == PatchCatalog.Id(patch)).ToArray();
                    if (overrides.Length == 0) patch.Apply(tree);
                    else ApplyRules(index, overrides);
                    var count = PatchIO.End();
                    index.Save();
                    results.Add(new(PatchCatalog.Id(patch), patch.Name, count));
                }
            }
            store.Validate(before);
            var report = new AnalysisReport(install.AppId, install.BuildId, before,
                BuildStore.HashFile(stagedIndex), DateTimeOffset.UtcNow, results);
            BuildStore.SaveJson(Path.Combine(DataRoot, "Reports", install.Key + "-latest.json"), report);
            return new() { Install = install, DirectoryPath = work, IndexPath = stagedIndex, Report = report, Profile = profile };
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
        var store = new BuildStore(DataRoot, plan.Install);
        using var operationLock = store.Lock();
        EnsureClosed();
        store.Validate(plan.Report.BeforeHash);
        if (BuildStore.HashFile(plan.IndexPath) != plan.Report.OutputHash) throw new IOException("Подготовленный индекс повреждён.");
        var stagedBundles = Path.Combine(plan.DirectoryPath, "PoeAdaptive");
        if (Directory.Exists(stagedBundles))
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
        store.Publish(plan.IndexPath, plan.Report.BeforeHash, EnsureClosed);
        SaveProfile(plan.Install, plan.Profile with { LastBuild = plan.Install.BuildId });
    }

    public void Restore(SteamInstall install)
    {
        install = install.Refresh();
        var store = new BuildStore(DataRoot, install);
        using var operationLock = store.Lock();
        EnsureClosed();
        store.Recover();
        var state = store.Load() ?? throw new IOException("Для текущей сборки нет исходного индекса.");
        store.Publish(store.OriginalIndex, state.CurrentHash, EnsureClosed);
        var profile = LoadProfile(install);
        if (profile != null) SaveProfile(install, profile with { AutoApply = false });
    }
}
