using System.IO;
using LibBundle3;
using LibBundle3.Records;
using LibGGPK3;
using LibGGPK3.Records;
using PoeAdaptive.Core;

namespace PoeRedux.Services;

public interface IResourceAdapter
{
    ResourceFormat Format { get; }
    ResourceSession Stage(GameInstall install, string baseline, string work);
    string Complete(GameInstall install, string baseline, string work);
    void Verify(string prepared);
}

public sealed class ResourceSession(LibBundle3.Index index, Action dispose) : IDisposable
{
    public LibBundle3.Index Index { get; } = index;
    public void Dispose() { Index.Dispose(); dispose(); }
}

public sealed class ExternalResourceAdapter : IResourceAdapter
{
    public ResourceFormat Format => ResourceFormat.ExternalBundles;
    public ResourceSession Stage(GameInstall install, string baseline, string work)
    {
        var index = Path.Combine(work, "_.index.bin"); File.Copy(baseline, index);
        return new(new StagingIndex(new FileStream(index, FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
            new IsolatedFactory(Path.GetDirectoryName(install.ResourcePath)!, work), Guid.NewGuid().ToString("N")), () => { });
    }
    public string Complete(GameInstall install, string baseline, string work) => Path.Combine(work, "_.index.bin");
    public void Verify(string prepared)
    {
        ResourceProbe.Inspect(prepared);
        using var input = File.OpenRead(prepared);
        using var index = new LibBundle3.Index(input, true, true, new IsolatedFactory(Path.GetDirectoryName(prepared)!, Path.GetDirectoryName(prepared)!));
        if (index.Files.Count == 0) throw new InvalidDataException("Пустой индекс.");
        foreach (var file in index.Files.Values.Where(f => f.BundleRecord.Path.StartsWith("PoeAdaptive/", StringComparison.Ordinal))) file.Read();
    }
}

internal sealed class ContainerFactory(GGPK ggpk, string work) : IBundleFactory
{
    private readonly LibGGPK3.Records.DirectoryRecord bundles = ggpk.Root["Bundles2"] as LibGGPK3.Records.DirectoryRecord ?? throw new InvalidDataException("Нет Bundles2.");
    public Bundle GetBundle(BundleRecord record)
    {
        var local = IsolatedFactory.SafePath(work, record.Path);
        if (File.Exists(local)) return new Bundle(new FileStream(local, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), false, record);
        if (!bundles.TryFindNode(record.Path, out var node) || node is not LibGGPK3.Records.FileRecord file) throw new FileNotFoundException(record.Path);
        return new Bundle(new GGFileStream(file), false, record); // underlying GGPK stream is explicitly FileAccess.Read
    }
    public Stream CreateBundle(string path)
    {
        if (!path.StartsWith("PoeAdaptive/", StringComparison.Ordinal)) throw new InvalidDataException("Недопустимая запись.");
        var target = IsolatedFactory.SafePath(work, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }
    public bool DeleteBundle(string path) => throw new NotSupportedException();
}

public sealed class GgpkResourceAdapter : IResourceAdapter
{
    public ResourceFormat Format => ResourceFormat.Ggpk;
    public ResourceSession Stage(GameInstall install, string baseline, string work)
    {
        var input = new FileStream(baseline, FileMode.Open, FileAccess.Read, FileShare.Read);
        GGPK? ggpk = null;
        try
        {
            ggpk = new GGPK(input, true);
            var bundles = ggpk.Root["Bundles2"] as LibGGPK3.Records.DirectoryRecord ?? throw new InvalidDataException("Нет Bundles2.");
            var file = bundles["_.index.bin"] as LibGGPK3.Records.FileRecord ?? throw new InvalidDataException("Нет индекса.");
            var indexPath = Path.Combine(work, "_.index.bin"); File.WriteAllBytes(indexPath, file.Read());
            var owner = ggpk;
            return new(new StagingIndex(new FileStream(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
                new ContainerFactory(owner, work), Guid.NewGuid().ToString("N")), () => { owner.Dispose(); input.Dispose(); });
        }
        catch { ggpk?.Dispose(); input.Dispose(); throw; }
    }
    public string Complete(GameInstall install, string baseline, string work)
    {
        var prepared = Path.Combine(work, "prepared.ggpk");
        FileSafety.RequireSpace(prepared, new FileInfo(baseline).Length + Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) + 64L * 1024 * 1024);
        File.Copy(baseline, prepared);
        using (var stream = new FileStream(prepared, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using (var ggpk = new GGPK(stream, true))
            {
                var bundles = (LibGGPK3.Records.DirectoryRecord)ggpk.Root["Bundles2"]!;
                var changed = Path.Combine(work, "PoeAdaptive");
                if (Directory.Exists(changed)) foreach (var path in Directory.EnumerateFiles(changed, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(work, path).Replace('\\', '/');
                    bundles.FindOrAddFile(relative, out var file);
                    file.Write(File.ReadAllBytes(path));
                }
                ((LibGGPK3.Records.FileRecord)bundles["_.index.bin"]!).Write(File.ReadAllBytes(Path.Combine(work, "_.index.bin")));
                ggpk.Flush();
            }
            stream.Flush(true);
        }
        Verify(prepared);
        return prepared;
    }
    public void Verify(string prepared)
    {
        ResourceProbe.Inspect(prepared);
        using var stream = new FileStream(prepared, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var ggpk = new LibBundledGGPK3.BundledGGPK(stream, true, true);
        if (ggpk.Index.Files.Count == 0) throw new InvalidDataException("Пустой контейнер.");
        // Read all newly emitted bundles and their referenced file ranges through the actual adapter.
        foreach (var file in ggpk.Index.Files.Values.Where(f => f.BundleRecord.Path.StartsWith("PoeAdaptive/", StringComparison.Ordinal))) file.Read();
    }
}

public static class ResourceAdapters
{
    public static IResourceAdapter For(ResourceFormat format) => format switch
    {
        ResourceFormat.ExternalBundles => new ExternalResourceAdapter(), ResourceFormat.Ggpk => new GgpkResourceAdapter(), _ => throw new NotSupportedException()
    };
    public static long EstimateAdditionalSpace(GameInstall install) => install.Format == ResourceFormat.Ggpk
        ? checked(new FileInfo(install.ResourcePath).Length * 4 + 1024L * 1024 * 1024) : 1024L * 1024 * 1024;
}
