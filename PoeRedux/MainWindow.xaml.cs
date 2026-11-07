using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PoeAdaptive.Core;
using PoeRedux.Models;
using PoeRedux.Patches;
using PoeRedux.Services;

namespace PoeRedux;

public partial class MainWindow : Window
{
    private readonly AdaptivePatcher patcher = new();
    private readonly ObservableCollection<PatchViewModel> patches = [];
    private readonly ObservableCollection<ColorModsViewModel> colors = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly HashSet<string> failedBuilds = [];
    private readonly HashSet<string> diagnosedBuilds = [];
    private CancellationTokenSource? diagnosticCancellation;
    private DiagnosticExport? diagnosticResult;
    private List<GameInstall> installs = [];
    private GameInstall? selected;
    private PreparedPatch? prepared;
    private bool busy;
    private bool polling;

    public MainWindow()
    {
        InitializeComponent();
        PatchesItemsControl.ItemsSource = patches;
        Loaded += (_, _) => { FindGames(); timer.Start(); };
        timer.Tick += async (_, _) => await PollUpdates();
        Closed += (_, _) => { timer.Stop(); diagnosticCancellation?.Cancel(); prepared?.Dispose(); };
    }

    private void InvalidatePlan()
    {
        prepared?.Dispose();
        prepared = null;
        if (ApplyButton != null) ApplyButton.IsEnabled = false;
        if (ResultsList != null) ResultsList.ItemsSource = null;
    }

    private void FindGames()
    {
        try
        {
            var manual = installs;
            installs = InstallationCatalog.Default.Discover().Concat(manual).DistinctBy(i => i.Key).ToList();
            var oldKey = selected?.Key;
            GameSelector.ItemsSource = installs;
            GameSelector.SelectedItem = installs.FirstOrDefault(x => x.Key == oldKey) ?? installs.FirstOrDefault();
            if (installs.Count == 0) StatusText.Text = "Завершите установку клиента или выберите файл / каталог вручную.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void FindGames_Click(object sender, RoutedEventArgs e) => FindGames();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Ресурсы PoE|*.bin;*.ggpk|Все файлы|*.*", Title = "Выберите Bundles2/_.index.bin или Content.ggpk" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var install = InstallationCatalog.Default.Manual(dialog.FileName);
            installs.RemoveAll(x => x.Key == install.Key);
            installs.Add(install);
            GameSelector.ItemsSource = installs.ToArray();
            GameSelector.SelectedItem = install;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Каталог установки PoE" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var install = InstallationCatalog.Default.Manual(dialog.FolderName);
            installs.RemoveAll(i => i.Key == install.Key); installs.Add(install);
            GameSelector.ItemsSource = installs.ToArray(); GameSelector.SelectedItem = install;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GameSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InvalidatePlan();
        selected = GameSelector.SelectedItem as GameInstall;
        patches.Clear();
        colors.Clear();
        if (selected == null) return;
        try
        {
            var available = PatchCatalog.Create(selected.Game);
            var profile = patcher.LoadProfile(selected);
            foreach (var patch in available)
            {
                var vm = new PatchViewModel(patch) { IsSelected = profile?.Patches.Contains(PatchCatalog.Id(patch)) == true };
                vm.PropertyChanged += (_, _) => InvalidatePlan();
                patches.Add(vm);
            }
            foreach (var color in profile?.Colors ?? PatchCatalog.Colors(available)) colors.Add(new(color.Copy()));
            ZoomSlider.Value = profile?.Zoom ?? 2.4;
            AutoApplyBox.IsChecked = profile?.AutoApply ?? false;
            InstallText.Text = $"{selected.Root}\n{selected.Client} · {selected.Format} · версия {selected.BuildId} · {selected.IndexPath}\nРезерв дополнительного места: {ResourceAdapters.EstimateAdditionalSpace(selected) / 1073741824.0:F1} ГиБ (оценка до анализа)";
            StatusText.Text = "Выберите функции и выполните анализ.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private Profile CaptureProfile() => new(patches.Where(p => p.IsSelected).Select(p => PatchCatalog.Id(p.Patch)).ToArray(),
        ZoomSlider.Value, colors.Select(c => { var copy = c.Option.Copy(); copy.Color = c.SelectedColor; copy.IsEnabled = c.IsSelected; return copy; }).ToList(),
        AutoApplyBox.IsChecked == true, selected == null ? "" : patcher.LoadProfile(selected)?.LastBuild ?? "");

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var patch in patches) patch.IsSelected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var patch in patches) patch.IsSelected = false; }
    private void Zoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => InvalidatePlan();
    private void Colors_Click(object sender, RoutedEventArgs e)
    {
        if (ColorModsEditor.Show(colors) == true)
        {
            foreach (var c in colors) { c.Option.Color = c.SelectedColor; c.Option.IsEnabled = c.IsSelected; }
            InvalidatePlan();
        }
        else foreach (var c in colors) { c.SelectedColor = c.Option.Color; c.IsSelected = c.Option.IsEnabled; }
    }
    private void AutoApply_Click(object sender, RoutedEventArgs e)
    {
        InvalidatePlan();
        if (selected == null) return;
        try
        {
            var profile = patcher.LoadProfile(selected);
            if (profile != null) patcher.SaveProfile(selected, profile with { AutoApply = AutoApplyBox.IsChecked == true });
            else StatusText.Text = "Автоприменение включится после первого успешного применения профиля.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        MainGrid.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await action(); }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; MainGrid.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; }
    }
    private void ShowError(Exception ex)
    {
        StatusText.Text = ex.Message;
        System.Windows.MessageBox.Show(this, ex.Message, "PoE Adaptive", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ShowResults(PreparedPatch plan)
    {
        ResultsList.ItemsSource = plan.Report.Results.Select(r => new
        {
            r.Name, r.ChangedFiles,
            Status = r.ChangedFiles > 0 ? "Изменения подготовлены" : "Нет изменений: правило устарело, перекрыто другой функцией или отключено"
        }).ToArray();
        ApplyButton.IsEnabled = plan.CanApply;
        InstallText.Text = $"{plan.Install.Root}\n{plan.Install.Client} · {plan.Install.Format} · версия {plan.Install.BuildId} · подготовлено без записи в игру";
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) { StatusText.Text = "Сначала выберите игру."; return; }
        var install = selected;
        InvalidatePlan();
        await Run(async () =>
        {
            var profile = CaptureProfile();
            var required = ResourceAdapters.EstimateAdditionalSpace(install);
            StatusText.Text = $"Подготовка ресурсов. Резерв места: {required / 1073741824.0:F1} ГиБ. GGPK обрабатывается на отдельной копии.";
            prepared = await Task.Run(() => patcher.Analyze(install, profile,
                message => Dispatcher.BeginInvoke(() => StatusText.Text = message)));
            ShowResults(prepared);
            StatusText.Text = prepared.CanApply ? "План готов. Проверьте результат и примените изменения."
                : "Снимите выбор функций без изменений и повторите анализ.";
        });
    }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (prepared == null) return;
        var plan = prepared;
        await Run(async () =>
        {
            StatusText.Text = "Публикация подготовленных ресурсов…";
            await Task.Run(() => patcher.Apply(plan));
            prepared = null;
            plan.Dispose();
            ApplyButton.IsEnabled = false;
            StatusText.Text = "Изменения применены. Профиль сохранён.";
        });
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var install = selected;
        InvalidatePlan();
        await Run(async () =>
        {
            await Task.Run(() => patcher.Restore(install));
            AutoApplyBox.IsChecked = false;
            StatusText.Text = "Восстановлены исходные ресурсы текущей версии. Автоприменение выключено.";
        });
    }
    private async void Verified_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        if (System.Windows.MessageBox.Show(this, "Используйте эту команду только после завершённой официальной проверки файлов выбранного клиента. Вы подтверждаете, что файлы восстановлены клиентом и сторонних патчей нет? Старые копии и журнал сохранятся.", "Подтверждение исходной версии", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var install = selected;
        InvalidatePlan();
        await Run(async () =>
        {
            AdaptivePatcher.EnsureClosed();
            var store = new BuildStore(AdaptivePatcher.DataRoot, install);
            using var gate = store.Lock();
            var fresh = await Task.Run(() => VersionTracker.AcceptVerified(install));
            await Task.Run(() => new DiagnosticPackager(AdaptivePatcher.DataRoot).ConfirmVerified(fresh));
            installs.RemoveAll(i => i.Key == fresh.Key); installs.Add(fresh);
            GameSelector.ItemsSource = installs.ToArray(); GameSelector.SelectedItem = fresh;
            StatusText.Text = "Исходная версия принята по вашему подтверждению. Выполните новый анализ.";
        });
    }
    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AdaptivePatcher.DataRoot);
        Process.Start(new ProcessStartInfo(AdaptivePatcher.DataRoot) { UseShellExecute = true });
    }

    private async void Diagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null || busy) return;
        var install = selected;
        var selectedFeatures = patches.Where(p => p.IsSelected).Select(p => PatchCatalog.Id(p.Patch)).ToArray();
        var options = new DiagnosticOptions(install, selectedFeatures, ExpectedText.Text, ActualText.Text);
        diagnosticCancellation?.Dispose();
        diagnosticCancellation = new CancellationTokenSource();
        var cancellation = diagnosticCancellation.Token;
        busy = true;
        CancelDiagnosticButton.IsEnabled = true;
        OpenDiagnosticButton.IsEnabled = false;
        CopyDiagnosticButton.IsEnabled = false;
        diagnosticResult = null;
        Progress.Visibility = Visibility.Visible;
        try
        {
            var packager = new DiagnosticPackager(AdaptivePatcher.DataRoot);
            diagnosticResult = await Task.Run(() => packager.Export(options, prepared,
                message => Dispatcher.BeginInvoke(() => StatusText.Text = message), cancellation), cancellation);
            OpenDiagnosticButton.IsEnabled = true;
            CopyDiagnosticButton.IsEnabled = true;
            StatusText.Text = $"Готово: {diagnosticResult.ZipPaths.Length} ZIP, {diagnosticResult.TotalBytes / 1024.0:F0} КиБ. Файлы игры не изменены.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Подготовка диагностики отменена."; }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            busy = false;
            CancelDiagnosticButton.IsEnabled = false;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelDiagnostic_Click(object sender, RoutedEventArgs e) => diagnosticCancellation?.Cancel();

    private void OpenDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticResult is not { ZipPaths.Length: > 0 }) return;
        var first = diagnosticResult.ZipPaths[0];
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + first + "\"") { UseShellExecute = true });
    }

    private void CopyDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticResult is null) return;
        Clipboard.SetText(diagnosticResult.Prompt);
        StatusText.Text = "Запрос для ChatGPT скопирован. Приложите все части ZIP.";
    }

    private async Task PollUpdates()
    {
        if (busy || polling) return;
        polling = true;
        try { await PollCore(); } finally { polling = false; }
    }
    private async Task PollCore()
    {
        try { AdaptivePatcher.EnsureClosed(); } catch (IOException) { return; }
        foreach (var candidate in installs.ToArray())
        {
            GameInstall fresh;
            Profile? profile;
            try { fresh = await Task.Run(candidate.Refresh); profile = patcher.LoadProfile(fresh); }
            catch (Exception ex) { StatusText.Text = $"Ожидание готовности клиента: {ex.Message}"; continue; }
            // Export on a confirmed installation build change, regardless of auto-apply settings.
            // Never turn a failed/unknown version check into an accepted baseline.
            if (candidate.BuildId.Length > 0 && fresh.BuildId != candidate.BuildId)
            {
                var diagnosticKey = fresh.Key + ":" + fresh.BuildId;
                if (diagnosedBuilds.Add(diagnosticKey))
                {
                    await Run(async () =>
                    {
                        try
                        {
                            var features = profile?.Patches ?? [];
                            var packager = new DiagnosticPackager(AdaptivePatcher.DataRoot);
                            var result = await Task.Run(() => packager.Export(new DiagnosticOptions(fresh,
                                features, Automatic: true, PriorVersion: candidate.BuildId)));
                            diagnosticResult = result;
                            OpenDiagnosticButton.IsEnabled = true;
                            CopyDiagnosticButton.IsEnabled = true;
                            StatusText.Text = $"Обновление: создан диагностический пакет ({result.ZipPaths.Length} частей).";
                        }
                        catch (Exception ex) { StatusText.Text = "Диагностика обновления отложена: " + ex.Message; }
                    });
                }
            }
            if (profile == null || !profile.AutoApply || profile.LastBuild.Length == 0 || profile.LastBuild == fresh.BuildId) continue;
            var key = fresh.Key + ":" + fresh.BuildId;
            if (failedBuilds.Contains(key)) continue;
            await Run(async () =>
            {
                InvalidatePlan();
                try
                {
                    using var plan = await Task.Run(() => patcher.Analyze(fresh, profile));
                    ShowResults(plan);
                    if (!plan.CanApply) throw new IOException("Автоприменение остановлено: есть функции без найденных изменений. Выполните анализ вручную.");
                    await Task.Run(() => patcher.Apply(plan));
                    ApplyButton.IsEnabled = false;
                    StatusText.Text = $"{fresh.Title}: профиль повторно применён к версии {fresh.BuildId}.";
                }
                catch (Exception ex)
                {
                    failedBuilds.Add(key);
                    ApplyButton.IsEnabled = false;
                    StatusText.Text = $"{fresh.Title}: автоприменение остановлено. {ex.Message}";
                }
            });
        }
    }
}
