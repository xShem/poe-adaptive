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
    private List<SteamInstall> installs = [];
    private SteamInstall? selected;
    private PreparedPatch? prepared;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        PatchesItemsControl.ItemsSource = patches;
        Loaded += (_, _) => { FindSteam(); timer.Start(); };
        timer.Tick += async (_, _) => await PollUpdates();
        Closed += (_, _) => { timer.Stop(); prepared?.Dispose(); };
    }

    private void InvalidatePlan()
    {
        prepared?.Dispose();
        prepared = null;
        if (ApplyButton != null) ApplyButton.IsEnabled = false;
        if (ResultsList != null) ResultsList.ItemsSource = null;
    }

    private void FindSteam()
    {
        try
        {
            var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") };
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key?.GetValue("SteamPath") is string root) roots.Add(root);
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                if (key?.GetValue("InstallPath") is string root) roots.Add(root);
            installs = SteamLocator.Discover(roots).ToList();
            var oldKey = selected?.Key;
            GameSelector.ItemsSource = installs;
            GameSelector.SelectedItem = installs.FirstOrDefault(x => x.Key == oldKey) ?? installs.FirstOrDefault();
            if (installs.Count == 0) StatusText.Text = "Завершите установку в Steam или выберите индекс вручную.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void FindSteam_Click(object sender, RoutedEventArgs e) => FindSteam();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Steam index (_.index.bin)|_.index.bin", Title = "Выберите Bundles2/_.index.bin" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var install = SteamLocator.FromIndex(dialog.FileName);
            installs.RemoveAll(x => x.Key == install.Key);
            installs.Add(install);
            GameSelector.ItemsSource = installs.ToArray();
            GameSelector.SelectedItem = install;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GameSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InvalidatePlan();
        selected = GameSelector.SelectedItem as SteamInstall;
        patches.Clear();
        colors.Clear();
        if (selected == null) return;
        try
        {
            var available = PatchCatalog.Create(selected.AppId);
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
            InstallText.Text = $"{selected.Root}\nSteam build {selected.BuildId} · {selected.IndexPath}";
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
        InstallText.Text = $"{plan.Install.Root}\nSteam build {plan.Install.BuildId} · подготовлено без записи в игру";
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) { StatusText.Text = "Сначала выберите игру."; return; }
        var install = selected;
        InvalidatePlan();
        await Run(async () =>
        {
            var profile = CaptureProfile();
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
            StatusText.Text = "Восстановлен исходный индекс текущей сборки. Автоприменение выключено.";
        });
    }
    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AdaptivePatcher.DataRoot);
        Process.Start(new ProcessStartInfo(AdaptivePatcher.DataRoot) { UseShellExecute = true });
    }

    private async Task PollUpdates()
    {
        if (busy) return;
        try { AdaptivePatcher.EnsureClosed(); } catch (IOException) { return; }
        foreach (var candidate in installs.ToArray())
        {
            SteamInstall fresh;
            Profile? profile;
            try { fresh = candidate.Refresh(); profile = patcher.LoadProfile(fresh); }
            catch (Exception ex) { StatusText.Text = $"Ожидание готовности Steam: {ex.Message}"; continue; }
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
                    StatusText.Text = $"{fresh.Title}: профиль повторно применён к Steam build {fresh.BuildId}.";
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
