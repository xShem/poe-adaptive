using System.Text;
using LibBundle3;
using LibBundle3.Nodes;
using LibBundle3.Records;
using PoeAdaptive.Core;
using PoeRedux.Patches;
using PoeRedux.Services;

if (args.Contains("--wait-leaf")) { await Task.Delay(60000); return; }
if (args.Contains("--wait-child"))
{
    var start = new System.Diagnostics.ProcessStartInfo(AdditionalChecks.DotnetHost) { UseShellExecute = false };
    start.ArgumentList.Add(typeof(AdditionalChecks).Assembly.Location); start.ArgumentList.Add("--wait-leaf");
    using var leaf = System.Diagnostics.Process.Start(start)!;
    Console.WriteLine("LEAF_PID=" + leaf.Id);
    await Task.Delay(60000); return;
}
var sampleArgument = Array.IndexOf(args, "--diagnostic-sample");
if (sampleArgument >= 0)
{
    if (sampleArgument + 1 >= args.Length) throw new ArgumentException("Missing diagnostic sample path.");
    Environment.SetEnvironmentVariable("POE_DIAGNOSTIC_SAMPLE", Path.GetFullPath(args[sampleArgument + 1]));
}
int passed = 0;
void Check(bool condition, string message = "Assertion failed")
{ if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
void Test(string name, Action action)
{ action(); passed++; Console.WriteLine("PASS " + name); }
int RunPatch(IPatch patch, DirectoryNode tree)
{
    PatchIO.Begin();
    try { patch.Apply(tree); return PatchIO.End(); }
    catch { PatchIO.End(); throw; }
}

var temp = Path.Combine(Path.GetTempPath(), "PoeAdaptive-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    Test("VDF nested libraries, comments and escaped paths", () =>
    {
        var v = Vdf.Parse("// library\n\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\SteamLibrary\" } }");
        Check(v.Node("libraryfolders")!.Node("0")!.Get("path") == @"D:\SteamLibrary");
        Throws<InvalidDataException>(() => Vdf.Parse("\"broken\" {"));
    });

    string manifest = Path.Combine(temp, "Steam", "steamapps", "appmanifest_238960.acf");
    string index = Path.Combine(temp, "Steam", "steamapps", "common", "Path of Exile", "Bundles2", "_.index.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
    Directory.CreateDirectory(Path.GetDirectoryName(index)!);
    void Manifest(string build, string flags = "4", string installDir = "Path of Exile") =>
        File.WriteAllText(manifest, $"\"AppState\" {{ \"appid\" \"238960\" \"buildid\" \"{build}\" \"StateFlags\" \"{flags}\" \"installdir\" \"{installDir}\" }}");
    Manifest("101"); File.WriteAllText(index, "ORIGINAL-101");
    var install = SteamLocator.FromIndex(index);
    var store = new BuildStore(Path.Combine(temp, "data"), install);
    var hash = BuildStore.HashFile(index);

    Test("Steam detection and manual index agree", () =>
    {
        var found = SteamLocator.Discover([Path.Combine(temp, "Steam")]);
        Check(found.Count == 1 && found[0].Key == install.Key && install.BuildId == "101");
    });
    Test("Incomplete Steam updates and directory traversal are rejected", () =>
    {
        Manifest("101", "1026"); Throws<IOException>(() => SteamLocator.FromManifest(manifest));
        Manifest("101", "4", "../escape"); Throws<InvalidDataException>(() => SteamLocator.FromManifest(manifest));
        Manifest("101");
    });
    Test("Baseline creation never changes game index", () =>
    {
        using var locked = store.Lock();
        var state = store.EnsureBaseline(hash);
        Check(File.ReadAllText(index) == "ORIGINAL-101" && state.CurrentHash == hash);
        Throws<IOException>(() => { using var otherLock = store.Lock(); });
    });

    var prepared = Path.Combine(temp, "prepared.index"); File.WriteAllText(prepared, "PATCHED-101");
    Test("Atomic publish and exact restore", () =>
    {
        using var locked = store.Lock();
        store.Publish(prepared, hash, () => { });
        Check(File.ReadAllText(index) == "PATCHED-101");
        var state = store.Load()!;
        Check(state.CurrentHash == BuildStore.HashFile(index) && File.ReadAllText(store.OriginalIndex) == "ORIGINAL-101");
        store.Publish(store.OriginalIndex, state.CurrentHash, () => { });
        Check(BuildStore.HashFile(index) == hash);
    });
    Test("Game starting between prepare and publish prevents index change", () =>
    {
        int calls = 0;
        Throws<IOException>(() => store.Publish(prepared, hash, () => { if (++calls == 2) throw new IOException("game started"); }));
        Check(BuildStore.HashFile(index) == hash);
        store.Recover();
        Check(store.Load()!.CurrentHash == hash);
    });
    Test("Pending journal recovers both sides of interrupted publication", () =>
    {
        var after = BuildStore.HashFile(prepared);
        var pending = Path.Combine(store.DirectoryPath, "pending.json");
        BuildStore.SaveJson(pending, new PublishIntent(hash, after));
        store.Recover(); Check(!File.Exists(pending) && store.Load()!.CurrentHash == hash);
        BuildStore.SaveJson(pending, new PublishIntent(hash, after));
        File.Copy(prepared, index, true);
        store.Recover(); Check(!File.Exists(pending) && store.Load()!.CurrentHash == after);
        store.Publish(store.OriginalIndex, after, () => { });
    });
    Test("Externally changed index blocks apply and restore", () =>
    {
        File.WriteAllText(index, "EXTERNAL");
        Throws<IOException>(() => store.Publish(prepared, hash, () => { }));
        Check(File.ReadAllText(index) == "EXTERNAL");
        File.Copy(store.OriginalIndex, index, true);
    });
    Test("New Steam build rejects an old plan and uses its own baseline", () =>
    {
        Manifest("102"); File.WriteAllText(index, "ORIGINAL-102");
        Throws<IOException>(() => store.Publish(prepared, hash, () => { }));
        var fresh = install.Refresh();
        var next = new BuildStore(Path.Combine(temp, "data"), fresh);
        next.EnsureBaseline(BuildStore.HashFile(index));
        Check(next.OriginalIndex != store.OriginalIndex);
        Check(File.ReadAllText(next.OriginalIndex) == "ORIGINAL-102" && File.ReadAllText(store.OriginalIndex) == "ORIGINAL-101");
        Manifest("101"); File.Copy(store.OriginalIndex, index, true);
    });
    Test("Damaged baseline is rejected", () =>
    {
        var backup = File.ReadAllBytes(store.OriginalIndex);
        File.WriteAllText(store.OriginalIndex, "DAMAGED");
        Throws<InvalidDataException>(() => store.Load());
        File.WriteAllBytes(store.OriginalIndex, backup);
    });
    Test("Data rules tolerate whitespace and enforce valid match bounds", () =>
    {
        var rule = new TextRule("Rain", "2694490", "metadata/", ".env", "(\"rain_intensity\"\\s*:\\s*)[0-9.]+", "${1}0.0");
        var regex = TextRules.Compile(rule);
        var (text, count) = TextRules.Transform(regex, rule, "{\"rain_intensity\"   :  3.5}");
        Check(count == 1 && text == "{\"rain_intensity\"   :  0.0}");
        Throws<InvalidDataException>(() => TextRules.Compile(rule with { MinMatches = 0 }));
        Check(!TextRules.Targets(rule, "art/fx.env"));
    });
    Test("Catalog retains all 13 PoE1 and 15 PoE2 functions", () =>
    {
        Check(PatchCatalog.Create("238960").Length == 13 && PatchCatalog.Create("2694490").Length == 15);
        Check(PatchCatalog.Create("238960").Any(p => p is BlackScreen));
        Check(PatchCatalog.Create("2694490").Any(p => p is AtlasFog));
    });
    Test("Missing or unfamiliar assets produce zero changes", () =>
    {
        var tree = new TestTree();
        foreach (var patch in PatchCatalog.Create("2694490")) Check(RunPatch(patch, tree) == 0, patch.Name);
        var env = tree.Add("metadata/environmentsettings/unknown.env", "{\"new_format\": 3}");
        Check(RunPatch(new Fog(), tree) == 0 && env.Writes == 0);
    });
    Test("BOM-only rewrite does not masquerade as an applied patch", () =>
    {
        var file = new FakeFile("example.env", Encoding.Unicode.GetBytes("unchanged"));
        PatchIO.Begin(); PatchIO.Write(file, [0xff, 0xfe, .. file.Bytes]);
        Check(PatchIO.End() == 0 && file.Writes == 0);
    });
    Test("Write without an analysis session fails", () =>
    {
        var file = new FakeFile("example.env", []);
        Throws<InvalidOperationException>(() => PatchIO.Write(file, [1]));
        Check(file.Writes == 0);
    });
    Test("Camera accepts LF files and repeated transformation is idempotent", () =>
    {
        var tree = new TestTree();
        var file = tree.Add("metadata/characters/character.ot", "version 3\nBase\n{\n\tteam = 1\n}\n");
        Check(RunPatch(new Camera { ZoomLevel = 1.8 }, tree) == 1);
        Check(file.Text.Contains("CreateCameraZoomNode(5000.0, 5000.0, 1.8)"));
        Check(RunPatch(new Camera { ZoomLevel = 1.8 }, tree) == 0);
    });
    Test("Monster HP accepts LF files and is idempotent", () =>
    {
        var tree = new TestTree();
        var file = tree.Add("metadata/monsters/monster.ot", "version 3\nStats\n{\n\titem_drop_slots = 1\n}\n");
        Check(RunPatch(new MonsterHP(), tree) == 1 && file.Text.Contains("base_maximum_life = 1"));
        Check(RunPatch(new MonsterHP(), tree) == 0);
    });
    Test("Rain and clouds compose on the same environment asset", () =>
    {
        var tree = new TestTree();
        var file = tree.Add("metadata/environmentsettings/test.env", "{\"rain_intensity\": 4.0,\"clouds_intensity\": 2.0}");
        Check(RunPatch(new Rain(), tree) == 1);
        Check(RunPatch(new Clouds(), tree) == 1);
        Check(file.Text.TrimStart('\uFEFF') == "{\"rain_intensity\": 0.0,\"clouds_intensity\": 0.0}");
    });
    Test("Particle cleanup preserves protected gameplay assets", () =>
    {
        var tree = new TestTree();
        var protectedFile = tree.Add("metadata/particles/monster_effects/atlasexiles/orion/test.pet", "important");
        var cosmetic = tree.Add("metadata/particles/cosmetic/test.pet", "cosmetic");
        Check(RunPatch(new Particles(), tree) == 1);
        Check(protectedFile.Writes == 0 && cosmetic.Text.TrimStart('\uFEFF') == "0");
    });
    Test("Effect cleanup preserves protected assets and parses nested syntax", () =>
    {
        var tree = new TestTree();
        var protectedFile = tree.Add("metadata/effects/spells/ground_effects/caustic/test.aoc", "client { Render { value = 1 } }");
        var cosmetic = tree.Add("metadata/effects/spells/cosmetic/test.aoc", "version 3\nclient { Render { value = \"{string}\" } }\n");
        Check(RunPatch(new Effects(), tree) == 1);
        Check(protectedFile.Writes == 0 && !cosmetic.Text.Contains("Render"));
    });
    AdditionalChecks.Run(temp, Test);
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(temp, true); }

sealed class FakeFile : FileRecord
{
    public byte[] Bytes { get; private set; }
    public int Writes { get; private set; }
    public string Text => Encoding.Unicode.GetString(Bytes);
    public FakeFile(string path, byte[] bytes) : base(0, null!, 0, bytes.Length) { Path = path; Bytes = bytes; }
    public override ReadOnlyMemory<byte> Read(Bundle? bundle = null) => Bytes;
    public override void Write(scoped ReadOnlySpan<byte> content, bool saveIndex = false) { Bytes = content.ToArray(); Writes++; }
}
class TestDir(string name, DirectoryNode? parent) : DirectoryNode(name, parent);
sealed class TestFileNode(FileRecord record, DirectoryNode parent) : FileNode(record, parent);
sealed class TestTree() : TestDir("", null)
{
    public FakeFile Add(string path, string text)
    {
        DirectoryNode current = this;
        var parts = path.Split('/');
        foreach (var part in parts[..^1])
        {
            var child = current.Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == part);
            if (child == null) { child = new TestDir(part, current); current.Children.Add(child); }
            current = child;
        }
        var file = new FakeFile(path, Encoding.Unicode.GetBytes(text));
        current.Children.Add(new TestFileNode(file, current));
        return file;
    }
}
