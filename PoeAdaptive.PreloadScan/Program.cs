using System.Text.Json;
using PoeAdaptive.PreloadScan;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    if (args is ["--help"] or ["-h"])
    {
        Console.WriteLine("PoE Adaptive PreloadScan: PreloadScan.exe [Content.ggpk] [report.json]");
        Console.WriteLine("Можно перетащить Content.ggpk на EXE. Архив открывается только для чтения.");
        return 0;
    }
    var source = args.Length > 0 ? args[0] : "";
    if (string.IsNullOrWhiteSpace(source))
    {
        Console.Write("Перетащите сюда Content.ggpk (или вставьте путь) и нажмите Enter: ");
        source = (Console.ReadLine() ?? "").Trim().Trim('"');
    }
    source = Path.GetFullPath(source.Trim('"'));
    if (!File.Exists(source)) throw new FileNotFoundException("GGPK-файл не найден.");
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    if (!Directory.Exists(desktop))
        desktop = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    if (!Directory.Exists(desktop))
        desktop = Path.GetTempPath();
    var target = args.Length > 1 ? Path.GetFullPath(args[1]) :
        Path.Combine(desktop, "PoE-GGPK-preload-report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
    var gameDirectory = Path.GetDirectoryName(source)!.TrimEnd(Path.DirectorySeparatorChar);
    if (target.Equals(source, StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith(gameDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Для безопасности отчёт нужно сохранить вне каталога GGPK.");
    if (!Directory.Exists(Path.GetDirectoryName(target)))
        throw new DirectoryNotFoundException("Каталог для отчёта не существует.");

    Console.WriteLine("Сканирование только для чтения. Ни один игровой файл не будет изменён.");
    var before = new FileInfo(source);
    long oldSize = before.Length;
    DateTime oldWriteTime = before.LastWriteTimeUtc;
    var report = PreloadScanner.Scan(source, Console.WriteLine);
    // Load optional Oodle only from our own executable directory.
    // Findings from packed bundles must remain distinct from direct-file evidence.
    var deep = DeepBundleScanner.Scan(source, Console.WriteLine);
    report = report with { DeepBundles = deep };
    before.Refresh();
    if (before.Length != oldSize || before.LastWriteTimeUtc != oldWriteTime)
        throw new IOException("Файл изменился во время проверки; отчёт не публикуется.");

    var staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        File.WriteAllText(staging, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(staging, target, false);
    }
    finally { if (File.Exists(staging)) File.Delete(staging); }
    Console.WriteLine();
    Console.WriteLine("Отчёт создан: " + target);
    Console.WriteLine("GGPK v" + report.GgpkVersion + "; прямых файлов: " + report.DirectFilesFound);
    Console.WriteLine("Bundles2/_.index.bin: " + (report.Bundles2IndexPresent ? "найден" : "не найден"));
    foreach (var feature in report.Features.Where(x => x.TextMatches > 0 || x.PathCandidates > 0))
        Console.WriteLine(feature.Feature + ": " + feature.Verdict +
            ", текст: " + feature.TextMatches + ", пути: " + feature.PathCandidates);
    Console.WriteLine("Глубокое сканирование: " + deep.Status + "; bundles: " + deep.BundlesRead +
        "; Oodle-блоков: " + deep.ChunksDecoded + "; объём: " + deep.BytesDecoded / 1048576 + " МиБ.");
    foreach (var feature in deep.Features.Where(x => x.Matches > 0))
        Console.WriteLine("  " + feature.Feature + ": " + feature.Matches + " признаков в распакованных блоках.");
    foreach (var warning in deep.Warnings.Take(3))
        Console.WriteLine("ПРИМЕЧАНИЕ: " + warning);
    Console.WriteLine("ВНИМАНИЕ: совпадения не доказывают работоспособность патча; без индекса нет имён внутренних ресурсов.");
    if (args.Length == 0)
    {
        Console.WriteLine("Нажмите Enter для выхода.");
        Console.ReadLine();
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Диагностика не выполнена: " + ex.GetType().Name + ": " + ex.Message);
    if (args.Length == 0)
    {
        Console.WriteLine("Нажмите Enter для выхода.");
        Console.ReadLine();
    }
    return 1;
}
