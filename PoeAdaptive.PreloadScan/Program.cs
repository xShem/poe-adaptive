using System.Text.Json;
using PoeAdaptive.PreloadScan;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    if (args.Any(a => a is "--help" or "-h"))
    {
        Console.WriteLine("PoE Adaptive GGPK Map 0.3.0 — карта файлов и структуры официального PoE 1");
        Console.WriteLine("Перетащите Content.ggpk на EXE или запустите:");
        Console.WriteLine("  PoeAdaptive.PreloadScan.exe [Content.ggpk] [report.zip]");
        Console.WriteLine("  PoeAdaptive.PreloadScan.exe --inventory-only [Content.ggpk]");
        Console.WriteLine("  PoeAdaptive.PreloadScan.exe --map-all [Content.ggpk]");
        Console.WriteLine("  PoeAdaptive.PreloadScan.exe --quick-json [Content.ggpk] [report.json]");
        Console.WriteLine("По умолчанию: все физические файлы и заголовки bundles плюс до 256 МиБ выборки Oodle.");
        Console.WriteLine("--map-all: все записи плюс чтение содержимого до 8 ГиБ (без изменения GGPK).");
        return 0;
    }
    var commands = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToHashSet();
    if (commands.Except(["--inventory-only", "--map-all", "--quick-json"]).Any() ||
        commands.Count > 1)
        throw new ArgumentException("Неизвестный или несовместимый параметр. Используйте --help.");
    var options = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
    bool interactive = options.Length == 0;
    var source = options.Length == 0 ? "" : options[0];
    if (string.IsNullOrWhiteSpace(source))
    {
        Console.Write("Перетащите Content.ggpk сюда или введите путь: ");
        source = Console.ReadLine() ?? "";
    }
    source = Path.GetFullPath(source.Trim().Trim('"'));
    if (!File.Exists(source)) throw new FileNotFoundException("Не найден файл GGPK.");
    if (options.Length > 2) throw new ArgumentException("Укажите только GGPK и необязательный путь отчёта.");

    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    if (!Directory.Exists(desktop))
        desktop = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    if (!Directory.Exists(desktop)) desktop = Path.GetTempPath();
    bool quick = commands.Contains("--quick-json");
    var extension = quick ? ".json" : ".zip";
    var defaultName = "PoE-GGPK-" + (quick ? "quick-" : "FULL-MAP-") +
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
        Guid.NewGuid().ToString("N")[..6] + extension;
    var output = options.Length > 1 ? Path.GetFullPath(options[1]) :
        Path.Combine(desktop, defaultName);

    var sourceDir = Path.GetDirectoryName(source)!.TrimEnd(Path.DirectorySeparatorChar);
    if (output.Equals(source, StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith(sourceDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Отчёт нельзя сохранять в каталоге игры.");

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    if (quick)
    {
        var report = PreloadScanner.Scan(source, Console.WriteLine);
        var deep = DeepBundleScanner.Scan(source, Console.WriteLine);
        report = report with { DeepBundles = deep };
        if (File.Exists(output)) throw new IOException("Отчёт уже существует.");
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Сохранён прежний краткий JSON: " + output);
    }
    else
    {
        var mode = commands.Contains("--inventory-only") ? MapMode.InventoryOnly :
            commands.Contains("--map-all") ? MapMode.Extended : MapMode.Sample;
        Console.WriteLine("Собираю карту структуры GGPK. Архив открыт только для чтения.");
        Console.WriteLine("В ZIP попадут только списки путей, структуры заголовков и ограниченные фрагменты.");
        var result = GgpkMapExporter.Export(source, output, mode, Console.WriteLine, cancellation.Token);
        Console.WriteLine("Готово: " + output);
        Console.WriteLine($"Файлов GGPK: {result.PhysicalFiles:N0}; bundles: {result.BundleRecords:N0}; " +
            $"проверенных заголовков: {result.BundleHeadersValid:N0}.");
        Console.WriteLine($"Декодировано: {result.ContentBytesDecoded / (1024 * 1024):N0} МиБ; " +
            $"сигналов функций: {result.FeatureSignalsFound:N0}; ссылок: {result.AssetReferencesFound:N0}.");
        Console.WriteLine("Полнота: физических записей = " + result.PhysicalTraversalComplete +
            "; всего содержимого bundles = " + result.ContentScanComplete + ".");
        Console.WriteLine("Нет индекса — нет подтверждённых имён виртуальных файлов. Это не средство изменения игры.");
    }
    if (interactive) { Console.WriteLine("Нажмите Enter."); Console.ReadLine(); }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Отменено. Неполный ZIP удалён, GGPK не изменён.");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Диагностика не выполнена: " + ex.GetType().Name + ": " + ex.Message);
    if (args.Length == 0) { Console.WriteLine("Нажмите Enter."); Console.ReadLine(); }
    return 1;
}
