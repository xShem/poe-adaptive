using PoeRedux.Services;
using System.Text;
using System.Text.Json;
using LibGGPK3;
using PoeAdaptive.Core;
using PoeAdaptive.Build;

static class AdditionalChecks
{
    public static string DotnetHost => Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
    public static void Run(string temp, Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
        void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        string Area(string name) { var path = Path.Combine(temp, name); Directory.CreateDirectory(path); return path; }
        var root = Area("Клиент с пробелами");
        var external = Path.Combine(root, "Bundles2", "_.index.bin"); Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        File.WriteAllBytes(external, BundleEnvelope([1, 2, 3, 4]));
        File.WriteAllText(Path.Combine(root, "PathOfExile_x64.exe"), "synthetic executable; identity injected only in tests");
        var epicPath = Path.Combine(temp, "epic.item");
        void Epic(string version, bool incomplete = false) => File.WriteAllText(epicPath, JsonSerializer.Serialize(new { DisplayName = "Path of Exile 2", InstallLocation = root, LaunchExecutable = "PathOfExile_x64.exe", AppVersionString = version, bIsIncompleteInstall = incomplete }));
        Epic("1");
        var externalInstall = EpicAdapter.Read(epicPath, _ => GameId.PoE2) with { VersionRoot = Area("versions") };
        test("Content probe accepts a structured bundle, rejects renamed garbage", () =>
        {
            Check(ResourceProbe.Inspect(external) == ResourceFormat.ExternalBundles);
            var bad = Path.Combine(root, "Content.ggpk"); File.WriteAllText(bad, "GGPK");
            Throws<EndOfStreamException>(() => ResourceProbe.Inspect(bad)); File.Delete(bad);
            var damaged = BundleEnvelope([1, 2, 3, 4]); damaged[60] = 55; File.WriteAllBytes(external, damaged);
            Throws<InvalidDataException>(() => ResourceProbe.Inspect(external)); File.WriteAllBytes(external, BundleEnvelope([1, 2, 3, 4]));
        });
        test("Epic identity, manifest version and incomplete update guards", () =>
        {
            Check(externalInstall.Game == GameId.PoE2 && externalInstall.Client == ClientKind.Epic && externalInstall.LauncherVersion == "1");
            Throws<InvalidDataException>(() => EpicAdapter.Read(epicPath, _ => GameId.PoE1));
            Epic("1", true); Throws<IOException>(() => EpicAdapter.Read(epicPath, _ => GameId.PoE2)); Epic("1");
        });
        test("GGG adapters detect both games without folder-name inference", () =>
        {
            foreach (var game in new[] { GameId.PoE1, GameId.PoE2 })
            {
                var adapter = new GggAdapter([root], _ => game); var i = adapter.Discover().Single();
                Check(i.Game == game && i.Client == ClientKind.GGG && i.Format == ResourceFormat.ExternalBundles);
            }
            Throws<InvalidDataException>(() => GggAdapter.ReadIdentity(root));
        });
        test("Manual resource selection and multiple installs remain independent", () =>
        {
            Check(ResourceProbe.SelectResource(root) == external && ResourceProbe.SelectResource(external) == external);
            var a = new GggAdapter([root], _ => GameId.PoE1).Read(root);
            var otherRoot = Area("Other install"); Directory.CreateDirectory(Path.Combine(otherRoot, "Bundles2"));
            File.Copy(external, Path.Combine(otherRoot, "Bundles2", "_.index.bin")); File.WriteAllText(Path.Combine(otherRoot, "PathOfExile.exe"), "other");
            var b = new GggAdapter([otherRoot], _ => GameId.PoE1).Read(otherRoot);
            Check(a.Key != b.Key && a.Key != (a with { Client = ClientKind.Epic }).Key);
            var catalog = new InstallationCatalog(new FakeAdapter(a, b));
            Check(catalog.Discover().Count == 2 && catalog.Manual(external, GameId.PoE1, ClientKind.GGG).Key == a.Key);
            Throws<InvalidDataException>(() => catalog.Manual(external, GameId.PoE2));
            Throws<InvalidDataException>(() => catalog.Manual(external, GameId.PoE1, ClientKind.Epic));
        });
        test("Epic and Steam discover both official game identities", () =>
        {
            var manifests = Area("Epic manifests");
            foreach (var game in new[] { GameId.PoE1, GameId.PoE2 })
            {
                var path = Path.Combine(manifests, ((int)game) + ".item");
                File.WriteAllText(path, JsonSerializer.Serialize(new { DisplayName = game == GameId.PoE1 ? "Path of Exile" : "Path of Exile 2", InstallLocation = root, LaunchExecutable = "PathOfExile_x64.exe", AppVersionString = "1" }));
                var found = new EpicAdapter(manifests, _ => game).Discover().ToArray();
                Check(found.Length == 1 && found[0].Game == game);
            }
            var steamRoot = Path.Combine(temp, "Steam");
            var app = Path.Combine(steamRoot, "steamapps", "appmanifest_2694490.acf");
            var index = Path.Combine(steamRoot, "steamapps", "common", "Second game", "Bundles2", "_.index.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(index)!); File.WriteAllBytes(index, BundleEnvelope([1,2,3,4]));
            File.WriteAllText(app, "\"AppState\" { \"appid\" \"2694490\" \"buildid\" \"200\" \"StateFlags\" \"4\" \"installdir\" \"Second game\" }");
            var installs = new SteamAdapter([steamRoot]).Discover().ToArray();
            Check(installs.Length == 2 && installs.Select(i => i.Game).Distinct().Count() == 2);
        });
        var containerRoot = Area("Standalone GGPK"); var container = Path.Combine(containerRoot, "Content.ggpk");
        CreateGgpk(container, BundleEnvelope([4, 3, 2, 1]));
        test("Unsupported standalone GGPK is classified without writes or false Steam assumptions", () =>
        {
            var noIndex = Path.Combine(Area("standalone unsupported GGPK"), "Content.ggpk");
            // Real GGPK v3 root record, intentionally without Bundles2/_.index.bin.
            using (var output = File.Create(noIndex))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(28); writer.Write(0x4B504747); writer.Write(3); writer.Write(28L); writer.Write(0L);
                writer.Write(50); writer.Write(0x52494450); writer.Write(1); writer.Write(0); writer.Write(new byte[32]); writer.Write((short)0);
            }
            using (var archive = new GGPK(noIndex))
            {
                archive.Root.FindOrAddFile("Metadata/example.txt", out var sample);
                sample.Write("synthetic"u8);
                archive.Root.FindOrAddDirectory("Bundles2", out _);
                archive.Root.FindOrAddFile("Legacy/_.index.bin", out var alternate);
                alternate.Write("not-bundles"u8);
            }
            var checksum = BuildStore.HashFile(noIndex);
            var report = GgpkStructureInspector.Inspect(noIndex);
            Check(report.GgpkVersion == 3 && report.Bundles2Found && !report.Bundles2IndexFound);
            Check(report.Classification == "bundles2-index-missing");
            Check(report.RootEntries.Contains("Bundles2/") &&
                report.OtherIndexCandidates.Contains("Legacy/_.index.bin"));
            try
            {
                ResourceProbe.Inspect(noIndex);
                throw new Exception("Unsupported layout should be rejected");
            }
            catch (InvalidDataException ex)
            {
                Check(ex.Message.Contains("Legacy/_.index.bin") && ex.Message.Contains("только на чтение"));
            }
            Check(BuildStore.HashFile(noIndex) == checksum);
        });
        test("Preload scanner finds direct feature text without Bundles2 and preserves GGPK", () =>
        {
            var archivePath = Path.Combine(Area("preload direct"), "Content.ggpk");
            using (var output = File.Create(archivePath))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(28); writer.Write(0x4B504747); writer.Write(3); writer.Write(28L); writer.Write(0L);
                writer.Write(50); writer.Write(0x52494450); writer.Write(1); writer.Write(0);
                writer.Write(new byte[32]); writer.Write((short)0);
            }
            using (var archive = new GGPK(archivePath))
            {
                archive.Root.FindOrAddFile("metadata/characters/character.ot", out var camera);
                camera.Write(Encoding.Unicode.GetBytes("on_initial_position_set = {CreateCameraZoomNode(5000.0, 5000.0, 2.4);}"));
                archive.Root.FindOrAddFile("metadata/environmentsettings/test.env", out var env);
                env.Write(Encoding.Unicode.GetBytes("""{"rain_intensity": 2.0, "clouds_intensity": 0.5}"""));
                archive.Root.FindOrAddFile("metadata/images/unrelated.bin", out var binary);
                binary.Write([0, 1, 2, 3, 4]);
            }
            var checksum = BuildStore.HashFile(archivePath);
            var result = PoeAdaptive.PreloadScan.PreloadScanner.Scan(archivePath);
            Check(result.GgpkVersion == 3 && !result.Bundles2IndexPresent);
            Check(result.Features.Single(f => f.Feature == "Camera").TextMatches > 0);
            Check(result.Features.Single(f => f.Feature == "Rain").TextMatches > 0);
            Check(result.Features.Single(f => f.Feature == "Clouds").TextMatches > 0);
            Check(result.Features.Single(f => f.Feature == "Minimap").TextMatches == 0);
            Check(result.Warnings.Any(w => w.Contains("не декодируется")));
            Check(BuildStore.HashFile(archivePath) == checksum);
            var mapPath = Path.Combine(Area("GGPK карта кириллица"), "physical-map.zip");
            var map = PoeAdaptive.PreloadScan.GgpkMapExporter.Export(
                archivePath, mapPath, PoeAdaptive.PreloadScan.MapMode.InventoryOnly);
            Check(map.PhysicalFiles == 3 && map.PhysicalTraversalComplete && map.BundleRecords == 0);
            Check(!map.RequiredIndexFound && map.ContentChunksScanned == 0 &&
                !map.ContentScanComplete);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(mapPath))
            {
                Check(zip.GetEntry("summary.json") != null);
                Check(zip.GetEntry("physical-files.jsonl") != null);
                Check(zip.GetEntry("directories.jsonl") != null);
                Check(zip.GetEntry("bundle-layouts.jsonl") != null);
                Check(zip.GetEntry("feature-evidence.jsonl") != null);
                Check(zip.GetEntry("asset-references.jsonl") != null);
                using var reader = new StreamReader(zip.GetEntry("physical-files.jsonl")!.Open());
                var filesText = reader.ReadToEnd();
                Check(filesText.Contains("metadata/characters/character.ot") &&
                    !filesText.Contains(Area("preload direct"), StringComparison.OrdinalIgnoreCase));
            }
            Throws<IOException>(() => PoeAdaptive.PreloadScan.GgpkMapExporter.Export(
                archivePath, Path.Combine(Path.GetDirectoryName(archivePath)!, "unsafe.zip"),
                PoeAdaptive.PreloadScan.MapMode.InventoryOnly));
            Check(BuildStore.HashFile(archivePath) == checksum);
        });
        test("GGPK map inventories a bundle envelope without requiring Oodle", () =>
        {
            var root = Area("GGPK bundle map");
            var source = Path.Combine(root, "Content.ggpk");
            CreateGgpk(source, BundleEnvelope([1, 2, 3, 4]));
            var outFile = Path.Combine(Area("map reports"), "layout.zip");
            var before = BuildStore.HashFile(source);
            var map = PoeAdaptive.PreloadScan.GgpkMapExporter.Export(
                source, outFile, PoeAdaptive.PreloadScan.MapMode.InventoryOnly);
            Check(map.BundleRecords == 1 && map.BundleHeadersValid == 1 &&
                map.BundleHeadersInvalid == 0 && map.RequiredIndexFound);
            using var zip = System.IO.Compression.ZipFile.OpenRead(outFile);
            using var reader = new StreamReader(zip.GetEntry("bundle-layouts.jsonl")!.Open());
            var line = reader.ReadLine()!;
            using var doc = JsonDocument.Parse(line);
            Check(doc.RootElement.GetProperty("HeaderValid").GetBoolean());
            Check(doc.RootElement.GetProperty("ChunkCount").GetInt32() == 1);
            Check(doc.RootElement.GetProperty("ChunkCompressedSizes")[0].GetInt32() == 4);
            Check(doc.RootElement.GetProperty("VirtualFilesListed").GetBoolean() == false);
            Check(BuildStore.HashFile(source) == before);
        });
        test("Deep scanner excludes non-Delirium environment paths from evidence", () =>
        {
            var unrelated = Encoding.Unicode.GetBytes("Metadata/Effects/Environment/act1/vfx/snowfall.ao");
            Check(!PoeAdaptive.PreloadScan.DeepBundleScanner.HasFeatureIndicator("Delirium", unrelated));
            var actual = Encoding.Unicode.GetBytes("Metadata/Effects/Environment/league_affliction/affliction.ao");
            Check(PoeAdaptive.PreloadScan.DeepBundleScanner.HasFeatureIndicator("Delirium", actual));
            Check(!PoeAdaptive.PreloadScan.DeepBundleScanner.HasFeatureIndicator("Delirium",
                Encoding.UTF8.GetBytes("Metadata/Effects/Environment/maps/other.ao")));
        });
        test("Deep PoE1 bundle scanner validates Oodle envelope without native DLL", () =>
        {
            var content = BundleEnvelope([1, 2, 3, 4]);
            Check(PoeAdaptive.PreloadScan.DeepBundleScanner.TryParseEnvelope(
                content.AsSpan(0, 60), content.Length, out var metadata));
            Check(metadata!.UncompressedSize == 4 && metadata.ChunkCount == 1 && metadata.ChunkSize == 262144);
            Check(PoeAdaptive.PreloadScan.DeepBundleScanner.ValidateChunkLengths(content.AsSpan(60, 4), metadata));
            var broken = (byte[])content.Clone();
            broken[60] = 99;
            Check(!PoeAdaptive.PreloadScan.DeepBundleScanner.ValidateChunkLengths(broken.AsSpan(60, 4), metadata));
            var fakeSize = (byte[])content.Clone();
            fakeSize[36] = 255;
            Check(!PoeAdaptive.PreloadScan.DeepBundleScanner.TryParseEnvelope(
                fakeSize.AsSpan(0, 60), fakeSize.Length, out _));
            Check(!PoeAdaptive.PreloadScan.DeepBundleScanner.TryParseEnvelope(
                content.AsSpan(0, 60), content.Length + 100, out _));
            Check(PoeAdaptive.PreloadScan.DeepBundleScanner.MaxDecodedBytes == 64 * 1024 * 1024);
            Check(PoeAdaptive.PreloadScan.DeepBundleScanner.MaxChunksPerBundle <= 2);
        });
        test("Preload scanner avoids false shader candidates in compressed bundle filenames", () =>
        {
            Check(!PoeAdaptive.PreloadScan.PreloadScanner.MatchesPathHint(
                "Bundles2/Folders/cachedhlslshaders/dx12.bundle.bin", "shaders/"));
            Check(PoeAdaptive.PreloadScan.PreloadScanner.MatchesPathHint(
                "Metadata/shaders/minimap_visibility_pixel.hlsl", "shaders/"));
            Check(PoeAdaptive.PreloadScan.PreloadScanner.MatchesPathHint(
                "metadata/characters/character.ot", "metadata/characters/character.ot"));
            Check(!PoeAdaptive.PreloadScan.PreloadScanner.MatchesPathHint(
                "metadata/characters/character.ot.backup", "metadata/characters/character.ot"));
        });
        test("Preload scanner refuses non GGPK without modifying it", () =>
        {
            var path = Path.Combine(Area("invalid preload"), "Content.ggpk");
            File.WriteAllText(path, "not a ggpk");
            var before = BuildStore.HashFile(path);
            Throws<InvalidDataException>(() => PoeAdaptive.PreloadScan.PreloadScanner.Scan(path));
            Check(BuildStore.HashFile(path) == before);
            Check(PoeAdaptive.PreloadScan.PreloadScanner.MaxSampleBytes <= 24 * 1024 * 1024);
        });
        test("Real GGPK record reader is byte-preserving with explicit read-only stream", () =>
        {
            var before = BuildStore.HashFile(container);
            Check(ResourceProbe.Inspect(container) == ResourceFormat.Ggpk);
            using (var input = File.OpenRead(container)) using (var ggpk = new GGPK(input, true)) Check(ggpk.Root["Bundles2"] != null);
            Check(BuildStore.HashFile(container) == before);
        });
        test("GGPK staging supports successive bundle writes while the source stays read-only", () =>
        {
            var before = BuildStore.HashFile(container); var work = Area("GGPK staging writes");
            var path = Path.Combine(work, "PoeAdaptive", "session", "0.bundle.bin"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, BundleEnvelope([1,2,3,4]));
            using var input = File.OpenRead(container); using var ggpk = new GGPK(input, true);
            var factory = new PoeRedux.Services.ContainerFactory(ggpk, work);
            using (var staged = factory.GetBundle(new FixtureBundleRecord("PoeAdaptive/session/0")))
            {
                var stream = staged.UnsafeGetStream(); stream.Position = stream.Length; stream.WriteByte(42); stream.Flush();
            }
            using (var original = factory.GetBundle(new FixtureBundleRecord("fixture"))) Check(!original.UnsafeGetStream().CanWrite);
            Check(File.ReadAllBytes(path)[^1] == 42 && BuildStore.HashFile(container) == before);
        });
        test("Own patches, original verification and confirmed update get distinct versions", () =>
        {
            var i = VersionTracker.Resolve(externalInstall); var initial = i.BuildId;
            var store = new BuildStore(i.VersionRoot, i, x => x);
            var original = BuildStore.HashFile(external); store.EnsureBaseline(original);
            var changed = Path.Combine(temp, "changed.index"); File.WriteAllBytes(changed, BundleEnvelope([2, 3, 4, 5]));
            store.Publish(changed, original, () => { });
            Check(VersionTracker.Resolve(externalInstall, true).BuildId == initial);
            Check(VersionTracker.Read(i)!.CurrentHash != VersionTracker.Read(i)!.OriginalHash);
            store.Publish(store.OriginalIndex, store.Load()!.CurrentHash, () => { });
            Check(VersionTracker.Resolve(externalInstall, true).BuildId == initial);
            File.WriteAllBytes(external, BundleEnvelope([3, 4, 5, 6]));
            Throws<IOException>(() => VersionTracker.Resolve(externalInstall, true));
            var next = VersionTracker.Resolve(externalInstall with { LauncherVersion = "2" }, true);
            Check(next.BuildId != initial);
            Throws<IOException>(() => store.Validate(original));
        });
        test("Unchanged index cannot hide foreign bundle changes; verified acceptance is explicit", () =>
        {
            var file = Path.Combine(root, "Bundles2", "original.bundle.bin"); File.WriteAllText(file, "first");
            var standalone = externalInstall with { Client = ClientKind.GGG, ManifestPath = "", LauncherVersion = "3" };
            var initial = VersionTracker.AcceptVerified(standalone);
            File.WriteAllText(file, "other");
            Throws<IOException>(() => VersionTracker.Resolve(initial, true));
            var verified = VersionTracker.AcceptVerified(initial);
            Check(verified.BuildId != initial.BuildId);
            File.Delete(file);
            VersionTracker.AcceptVerified(standalone with { LauncherVersion = "2" });
        });
        test("Both resource formats publish and restore exact bytes at every crash boundary", () =>
        {
            foreach (var format in new[] { ResourceFormat.ExternalBundles, ResourceFormat.Ggpk })
            foreach (var phase in Enum.GetValues<PublishPhase>())
            {
                var path = Path.Combine(Area("crash-" + format + phase), format == ResourceFormat.Ggpk ? "Content.ggpk" : "_.index.bin");
                if (format == ResourceFormat.Ggpk) CreateGgpk(path, BundleEnvelope([1, 2, 3, 4])); else File.WriteAllBytes(path, BundleEnvelope([1, 2, 3, 4]));
                var i = new GameInstall(GameId.PoE1, ClientKind.GGG, Path.GetDirectoryName(path)!, format, path, "", "synthetic-1", "", Area("crash-data"));
                i = VersionTracker.Resolve(i); var before = BuildStore.HashFile(path);
                var store = new BuildStore(i.VersionRoot, i, x => x, p => { if (p == phase) throw new IOException("simulated interruption"); });
                store.EnsureBaseline(before);
                var prepared = path + ".prepared";
                if (format == ResourceFormat.Ggpk)
                {
                    File.Copy(path, prepared);
                    using var ggpk = new GGPK(prepared); ggpk.Root.FindOrAddFile("synthetic-change.txt", out var record); record.Write("changed"u8);
                }
                else File.WriteAllBytes(prepared, BundleEnvelope([5, 6, 7, 8]));
                Throws<IOException>(() => store.Publish(prepared, before, () => { }));
                var recovered = new BuildStore(i.VersionRoot, i, x => x); recovered.Recover();
                var expected = phase is PublishPhase.Replaced or PublishPhase.StateSaved ? BuildStore.HashFile(prepared) : before;
                Check(BuildStore.HashFile(path) == expected && recovered.Load()!.CurrentHash == expected);
                recovered.Publish(recovered.OriginalIndex, expected, () => { }); Check(BuildStore.HashFile(path) == before);
            }
        });
        test("Unknown interrupted result blocks further writes", () =>
        {
            var i = GameInstall.FromSteam(SteamLocator.FromIndex(Path.Combine(temp, "Steam", "steamapps", "common", "Path of Exile", "Bundles2", "_.index.bin")));
            var store = new BuildStore(Path.Combine(temp, "data"), i);
            var before = BuildStore.HashFile(i.ResourcePath);
            BuildStore.SaveJson(Path.Combine(store.DirectoryPath, "pending.json"), new PublishIntent(before, "not-the-current-hash", 2, i.Key, i.BuildId));
            File.WriteAllText(i.ResourcePath, "unknown-interrupted-result");
            Throws<IOException>(store.Recover);
            Check(File.Exists(Path.Combine(store.DirectoryPath, "pending.json")));
            File.Copy(store.OriginalIndex, i.ResourcePath, true); store.Recover();
        });
        test("Snapshots cannot be transplanted across client or installation identities", () =>
        {
            var i = VersionTracker.Resolve(externalInstall with { LauncherVersion = "2" }); var store = new BuildStore(i.VersionRoot, i, x => x);
            store.EnsureBaseline(BuildStore.HashFile(i.ResourcePath));
            var wrong = store.Load()! with { InstallationKey = "another-installation" };
            BuildStore.SaveJson(Path.Combine(store.DirectoryPath, "state.json"), wrong);
            Throws<InvalidDataException>(() => store.Load());
        });
        test("Legacy rules map both AppIds and preserve ambiguous originals", () =>
        {
            var path = Path.Combine(temp, "rules.json");
            var r = new TextRule("Rain", "2694490", "metadata/", ".env", "old", "new");
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { r, r with { AppId = "238960", PatchId = "Fog" } }));
            var original = File.ReadAllText(path); var migrated = TextRules.Load(path);
            Check(migrated[0].TargetGame == GameId.PoE2 && migrated[1].TargetGame == GameId.PoE1 && File.ReadAllText(path) == original && File.Exists(path + ".v2.json"));
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { r with { AppId = "unknown" } })); original = File.ReadAllText(path);
            Throws<InvalidDataException>(() => TextRules.Load(path)); Check(File.ReadAllText(path) == original);
            BuildStore.SaveJson(path, new RuleDocument(999, [r])); Throws<InvalidDataException>(() => TextRules.Load(path));
        });
        test("Legacy Steam state remains readable and installation key is unchanged", () =>
        {
            var steam = SteamLocator.FromIndex(Path.Combine(temp, "Steam", "steamapps", "common", "Path of Exile", "Bundles2", "_.index.bin"));
            var i = GameInstall.FromSteam(steam); Check(i.Key == steam.Key && i.Game == GameId.PoE1);
            var store = new BuildStore(Path.Combine(temp, "data"), steam); var hash = BuildStore.HashFile(i.ResourcePath);
            BuildStore.SaveJson(Path.Combine(store.DirectoryPath, "state.json"), new { BuildId = i.BuildId, OriginalHash = hash, CurrentHash = hash });
            Check(store.Load()!.SchemaVersion == 1);
        });
        test("Legacy Steam profile migrates without losing its original JSON", () =>
        {
            var steam = GameInstall.FromSteam(SteamLocator.FromIndex(Path.Combine(temp, "Steam", "steamapps", "common", "Path of Exile", "Bundles2", "_.index.bin")));
            var data = Area("profile migration"); var profiles = Path.Combine(data, "Profiles"); Directory.CreateDirectory(profiles);
            var path = Path.Combine(profiles, steam.Key + ".json");
            var original = "{\"Patches\":[\"Fog\"],\"Zoom\":1.8,\"Colors\":[],\"AutoApply\":true,\"LastBuild\":\"101\"}";
            File.WriteAllText(path, original);
            var patcher = new PoeRedux.Services.AdaptivePatcher(data); var profile = patcher.LoadProfile(steam)!;
            Check(profile.AutoApply && profile.Patches.Single() == "Fog"); patcher.SaveProfile(steam, profile);
            Check(File.ReadAllText(path + ".v0.1.0") == original && patcher.LoadProfile(steam)!.Zoom == 1.8);
            BuildStore.SaveJson(path, new PoeRedux.Services.SavedProfile(2, "wrong", GameId.PoE1, ClientKind.Steam, profile));
            Throws<InvalidDataException>(() => patcher.LoadProfile(steam));
        });
        test("Active update markers, locked files and insufficient space block operations", () =>
        {
            var i = new GggAdapter([root], _ => GameId.PoE1).Read(root);
            FileSafety.RequireIdle(i); Directory.CreateDirectory(Path.Combine(root, ".egstore", "bps"));
            Throws<IOException>(() => FileSafety.RequireIdle(i)); Directory.Delete(Path.Combine(root, ".egstore"), true);
            using (var held = new FileStream(external, FileMode.Open, FileAccess.Read, FileShare.None)) Throws<IOException>(() => FileSafety.RequireIdle(i));
            Throws<IOException>(() => FileSafety.RequireSpace(temp, 1000, _ => 999));
            Throws<IOException>(() => BuildPipeline.RequireSpace(temp, 1000, _ => 0));
        });
        test("Diagnostic catalog covers both feature sets and ordered JSON overrides", () =>
        {
            foreach (var game in new[] { GameId.PoE1, GameId.PoE2 })
                foreach (var patch in PoeRedux.Services.PatchCatalog.Create(game))
                    Check(PoeRedux.Services.DiagnosticCatalog.For(game, PoeRedux.Services.PatchCatalog.Id(patch), []).Length > 0);
            var one = new TextRule("Rain", "2694490", "metadata/", ".env", "first", "A");
            var two = one with { PathPrefix = "metadata/environmentsettings/", Pattern = "second" };
            var targets = PoeRedux.Services.DiagnosticCatalog.For(GameId.PoE2, "Rain", [one, two]);
            Check(targets.Length == 2 && targets[0].Source == "rules.json:0" && targets[1].Source == "rules.json:1");
            Check(targets[0].Matches("metadata/foo.env") && !targets[1].Matches("metadata/foo.env"));
        });
        test("Moved game resource is a candidate, not a confirmed diagnostic target", () =>
        {
            var requirement = PoeRedux.Services.DiagnosticCatalog.For(GameId.PoE1, "Minimap", [])
                .Single(x => x.ExactPath == "shaders/minimap_visibility_pixel.hlsl");
            var result = PoeRedux.Services.DiagnosticCatalog.Search("Minimap", requirement,
                ["shaders/new/minimap_visibility_pixel.hlsl", "shaders/not-related.hlsl", "art/other.dds"]);
            Check(result.Matched == 0 && result.Candidates.Single() == "shaders/new/minimap_visibility_pixel.hlsl");
            Check(result.Status.StartsWith("target_missing"));
            Check(PoeRedux.Services.DiagnosticCatalog.Search("Minimap", requirement,
                ["shaders/minimap_visibility_pixel.hlsl"]).Matched == 1);
        });
        test("Diagnostic text decoding retains raw UTF-16 BOM and refuses binary previews", () =>
        {
            byte[] raw = [0xff, 0xfe, .. Encoding.Unicode.GetBytes("\"rain_intensity\": 4")];
            var (text, encoding) = PoeRedux.Services.DiagnosticCatalog.DecodeText(raw, "metadata/settings.env");
            Check(text!.Contains("rain_intensity") && encoding == "utf-16le-bom"
                && raw[0] == 0xff && raw[1] == 0xfe);
            var (binary, _) = PoeRedux.Services.DiagnosticCatalog.DecodeText([0, 2, 0, 255], "assets/picture.dds");
            Check(binary == null);
            var missing = PoeRedux.Services.DiagnosticCatalog.EvaluateSigns(text,
                new PoeRedux.Services.DiagnosticTarget("metadata/", ".env", ExpectedText: ["fog", "rain_intensity"]));
            Check(missing.Contains("missing:fog") && missing.Contains("found:rain_intensity"));
        });
        test("Diagnostic folder and ZIP limits are explicitly bounded", () =>
        {
            Check(PoeRedux.Services.DiagnosticPackager.MaxSingle == 4L * 1024 * 1024);
            Check(PoeRedux.Services.DiagnosticPackager.MaxExpanded == 64L * 1024 * 1024);
            Check(PoeRedux.Services.DiagnosticPackager.MaxZip == 20L * 1024 * 1024);
        });
        test("Read-only diagnostic reader refuses invalid indexes without changing them", () =>
        {
            var i = externalInstall;
            var checksum = BuildStore.HashFile(external);
            Throws<Exception>(() => { using var reader = PoeRedux.Services.DiagnosticResourceReader.Open(external, i.Format); _ = reader.Paths; });
            Check(BuildStore.HashFile(external) == checksum);
        });
        test("Diagnostic ZIP manifest hashes and unexpected entries are enforced", () =>
        {
            var zipPath = Path.Combine(temp, "synthetic diagnostic Кириллица.zip");
            byte[] raw = Encoding.Unicode.GetBytes("тестовый ресурс");
            var member = "resources/current/данные.env";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw));
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                void Add(string name, byte[] bytes)
                {
                    using var output = zip.CreateEntry(name).Open();
                    output.Write(bytes);
                }
                Add("manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1, Part = 1, Parts = 1, Consistent = true,
                    Files = new[] { new { ZipPath = member, Size = (long)raw.Length, Sha256 = digest } }
                })));
                Add("README-FOR-AI.md", "Synthetic only"u8.ToArray());
                Add("PROMPT.txt", "Analyze synthetic resources"u8.ToArray());
                Add("summary.txt", "Synthetic manifest"u8.ToArray());
                Add(member, raw);
            }
            PoeRedux.Services.DiagnosticArchiveValidator.Validate(zipPath);
            var samplePath = Environment.GetEnvironmentVariable("POE_DIAGNOSTIC_SAMPLE");
            if (!string.IsNullOrWhiteSpace(samplePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(samplePath))!);
                File.Copy(zipPath, samplePath, true);
            }
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update))
            {
                using var output = zip.CreateEntry("resources/current/unlisted.bin").Open();
                output.Write([1, 2, 3]);
            }
            Throws<InvalidDataException>(() => PoeRedux.Services.DiagnosticArchiveValidator.Validate(zipPath));
        });
        test("Diagnostic selectors match actual ColorMods and Effects roots", () =>
        {
            Check(DiagnosticCatalog.For(GameId.PoE2, "ColorMods2", []).Single().Matches("data/statdescriptions/test.csd"));
            Check(!DiagnosticCatalog.For(GameId.PoE2, "ColorMods2", []).Single().Matches("metadata/test.csd"));
            Check(DiagnosticCatalog.For(GameId.PoE1, "Effects", []).Single().Matches("metadata/effects/spells/test.aoc"));
        });
        test("Sequential diagnostic rules inspect preceding output without changing original bytes", () =>
        {
            var first = new TextRule("Rain", "2694490", "metadata/", ".env", "rain", "snow");
            var second = first with { Pattern = "snow", Replacement = "fog" };
            var bytes = Encoding.Unicode.GetBytes("rain");
            var results = DiagnosticRuleProbe.Evaluate("metadata/test.env", bytes, [first, second]);
            Check(results.Length == 2 && results.All(r => r.Matches == 1 && r.Changed == true && r.Error == null));
            Check(Encoding.Unicode.GetString(bytes) == "rain");
        });
        test("Diagnostic descriptions redact Windows and Unix personal paths", () =>
        {
            var i = new GameInstall(GameId.PoE1, ClientKind.GGG, root, ResourceFormat.Ggpk,
                Path.Combine(root, "Content.ggpk"), "", "v1", "v1", temp);
            var text = DiagnosticPackager.Redact(root + "\nC:\\Users\\Private\\game\n/home/private/game", i);
            Check(!text.Contains(root) && !text.Contains("Private") && !text.Contains("/home/private"));
        });
        test("Original attestation rejects bundle changes even with preserved length and timestamp", () =>
        {
            var location = Area("Diagnostic attestation");
            var index = Path.Combine(location, "_.index.bin"); File.WriteAllText(index, "index");
            var bundle = Path.Combine(location, "sample.bundle.bin"); File.WriteAllText(bundle, "before");
            var i = new GameInstall(GameId.PoE1, ClientKind.GGG, location, ResourceFormat.ExternalBundles,
                index, "", "v1", "v1", temp);
            var packager = new DiagnosticPackager(Area("Diagnostic attestation data"));
            packager.ConfirmVerified(i);
            Check(packager.HasAttestation(i, BuildStore.HashFile(index)));
            var timestamp = File.GetLastWriteTimeUtc(bundle);
            File.WriteAllText(bundle, "after!"); File.SetLastWriteTimeUtc(bundle, timestamp);
            Check(!packager.HasAttestation(i, BuildStore.HashFile(index)));
        });
        test("Pinned SDK and checksum validation do not require a game or Oodle", () =>
        {
            Check(SdkSpec.Pinned.Version == "10.0.403" && new Uri(SdkSpec.Pinned.Url).Host == "builds.dotnet.microsoft.com");
            var file = Path.Combine(temp, "sdk.zip"); File.WriteAllText(file, "not Microsoft SDK");
            Throws<InvalidDataException>(() => SdkManager.VerifyDownload(file, new string('0', 128), CancellationToken.None).GetAwaiter().GetResult());
        });
        test("SDK extraction accepts root entries and rejects archive traversal", () =>
        {
            var zip = Path.Combine(temp, "sdk-structure.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                archive.CreateEntry("./");
                using var writer = new StreamWriter(archive.CreateEntry("sdk/version.txt").Open()); writer.Write("synthetic SDK layout");
            }
            var destination = Path.Combine(temp, "SDK cache", "mixed path");
            SdkManager.ExtractArchive(zip, destination.Replace('\\', '/'), CancellationToken.None);
            Check(File.ReadAllText(Path.Combine(destination, "sdk", "version.txt")) == "synthetic SDK layout");
            var bad = Path.Combine(temp, "unsafe-sdk.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(bad, System.IO.Compression.ZipArchiveMode.Create)) archive.CreateEntry("../outside.txt");
            Throws<InvalidDataException>(() => SdkManager.ExtractArchive(bad, destination, CancellationToken.None));
            Check(!File.Exists(Path.Combine(temp, "SDK cache", "outside.txt")));
        });
        test("Build process reports failure and handles Cyrillic and spaces safely", () =>
        {
            var dotnet = DotnetHost;
            var output = Processes.Run(dotnet, ["--version"], root, null, CancellationToken.None).GetAwaiter().GetResult(); Check(output.StartsWith("10."));
            Throws<IOException>(() => Processes.Run(dotnet, ["definitely-no-such-command"], root, null, CancellationToken.None).GetAwaiter().GetResult());
        });
        test("Cancellation kills a running process tree and never reports success", () =>
        {
            var dotnet = DotnetHost;
            using var cancel = new CancellationTokenSource();
            var seen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = Processes.Run(dotnet, [typeof(AdditionalChecks).Assembly.Location, "--wait-child"], root,
                line => { if (line.StartsWith("LEAF_PID=")) seen.TrySetResult(int.Parse(line[9..])); }, cancel.Token);
            var leaf = seen.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            cancel.Cancel();
            Throws<OperationCanceledException>(() => command.GetAwaiter().GetResult());
            try { using var process = System.Diagnostics.Process.GetProcessById(leaf); var exited = process.WaitForExit(3000);
                if (!exited && OperatingSystem.IsLinux()) exited = File.ReadAllText($"/proc/{leaf}/stat").Contains(") Z ");
                Check(exited); } catch (ArgumentException) { } catch (FileNotFoundException) { }

        });
        test("A log write failure terminates the build process instead of hanging", () =>
        {
            Throws<IOException>(() => Processes.Run(DotnetHost, [typeof(AdditionalChecks).Assembly.Location, "--wait-child"], root,
                _ => throw new IOException("synthetic disk-full log failure"), CancellationToken.None).GetAwaiter().GetResult());
        });
        test("Builder snapshot excludes generated files and preserves Cyrillic source", () =>
        {
            var source = Area("Исходники Builder"); Directory.CreateDirectory(Path.Combine(source, "bin")); File.WriteAllText(Path.Combine(source, "bin", "generated"), "ignored"); File.WriteAllText(Path.Combine(source, "текст.cs"), "source"); File.WriteAllText(Path.Combine(source, "oo2core.dll"), "must never redistribute");
            Check(BuildPipeline.SourceFiles(source, Path.Combine(source, "artifacts")).Select(Path.GetFileName).SequenceEqual(new[] { "текст.cs" }));
        });
    }
    public static byte[] BundleEnvelope(byte[] data)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(data.Length); w.Write(data.Length); w.Write(52); w.Write(9); w.Write(1); w.Write((long)data.Length); w.Write((long)data.Length); w.Write(1); w.Write(262144); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(data.Length); w.Write(data); return ms.ToArray();
    }
    private static void CreateGgpk(string path, byte[] index)
    {
        // Genuine Windows GGPK v3 empty root, then use the real library to add records. No proprietary codec.
        using (var s = File.Create(path)) using (var w = new BinaryWriter(s))
        {
            w.Write(28); w.Write(0x4B504747); w.Write(3); w.Write(28L); w.Write(0L);
            w.Write(50); w.Write(0x52494450); w.Write(1); w.Write(0); w.Write(new byte[32]); w.Write((short)0);
        }
        using var ggpk = new GGPK(path); ggpk.Root.FindOrAddFile("Bundles2/_.index.bin", out var file); file.Write(index);
        ggpk.Root.FindOrAddFile("Bundles2/fixture.bundle.bin", out var fixture); fixture.Write(BundleEnvelope([1,2,3,4]));
    }
    private sealed class FixtureBundleRecord(string path) : LibBundle3.Records.BundleRecord(path, 4, null!, 0);
    private sealed class FakeAdapter(params GameInstall[] installs) : IInstallationAdapter
    {
        public ClientKind Client => ClientKind.GGG;
        public IEnumerable<GameInstall> Discover() => installs;
        public GameInstall Refresh(GameInstall i) => i;
    }
}
