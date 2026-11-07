using System.Text.Json;
using PoeAdaptive.Core;
try
{
    if (args.Length != 2) { Console.WriteLine("PoeAdaptive.Diagnostics <каталог или ресурс> <report.json>\nОтчёт не содержит пути установки и игровых ресурсов. Полный SHA-256 вычисляется один раз; большой GGPK займёт время."); return 1; }
    var path = ResourceProbe.SelectResource(args[0]);
    var format = ResourceProbe.Inspect(path);
    GameInstall? install = null; string? identificationError = null;
    try { install = InstallationCatalog.Default.Manual(path); } catch (Exception ex) { identificationError = ex.GetType().Name + ": установка не идентифицирована; проверьте метаданные EXE и манифест клиента."; }
    var file = new FileInfo(path);
    var before = BuildStore.HashFile(path);
    var report = new { schemaVersion = 2, createdUtc = DateTimeOffset.UtcNow, game = install?.Game.ToString(), client = install?.Client.ToString(), format = format.ToString(), bytes = file.Length, sha256 = before, launcherVersion = install?.LauncherVersion, identificationError, resourceValidation = "envelope-and-container-structure", executables = Directory.EnumerateFiles(install?.Root ?? (format == ResourceFormat.Ggpk ? Path.GetDirectoryName(path)! : Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName), "*.exe").Select(p => { var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(p); return new { file = Path.GetFileName(p), v.ProductName, v.CompanyName, v.FileDescription, v.FileVersion }; }).ToArray(), nativeCodecValidation = false, realClientValidation = false };
    if (Path.GetFullPath(args[1]).StartsWith(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Сохраняйте отчёт вне установки игры.");
    File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    if (BuildStore.HashFile(path) != before) throw new IOException("Ресурс изменился во время диагностики.");
    Console.WriteLine("Отчёт сохранён. Ресурсы открывались только на чтение."); return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
