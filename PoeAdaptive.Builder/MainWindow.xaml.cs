using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PoeAdaptive.Build;
namespace PoeAdaptive.Builder;
public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellation;
    private string? result;
    public MainWindow()
    {
        InitializeComponent(); Output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PoeAdaptive builds");
        Loaded += async (_, _) => Sdk.Text = "SDK " + SdkSpec.Pinned.Version + ": " + (await SdkManager.Find(null, SdkManager.DefaultCache, null, CancellationToken.None) ?? "не установлен; Builder предложит загрузку");
        Closing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); Stage.Text = "Отмена: ожидаю завершения дерева процессов…"; } };
    }
    private void ChooseSource(object sender, RoutedEventArgs e) { var d = new OpenFolderDialog { Title = "Корень исходников PoE Adaptive" }; if (d.ShowDialog() == true) Source.Text = d.FolderName; }
    private void ChooseOutput(object sender, RoutedEventArgs e) { var d = new OpenFolderDialog { Title = "Каталог результатов" }; if (d.ShowDialog() == true) Output.Text = d.FolderName; }
    private void SourceChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (ProjectVersion == null) return; try { ProjectVersion.Text = "Версия проекта: " + BuildPipeline.Version(Source.Text); } catch { ProjectVersion.Text = "Выберите каталог с PoeRedux.slnx"; } }
    private async void BuildClick(object sender, RoutedEventArgs e)
    {
        bool install = false;
        if (await SdkManager.Find(null, SdkManager.DefaultCache, null, CancellationToken.None) == null)
        {
            install = MessageBox.Show(this, $"Загрузить проверенный Microsoft .NET SDK {SdkSpec.Pinned.Version} в {SdkManager.DefaultCache}? Понадобится интернет и около 3 ГиБ свободного места. Права администратора не нужны.", "Подготовка SDK", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            if (!install) return;
        }
        cancellation = new(); result = null; OpenButton.IsEnabled = false;
        BuildButton.IsEnabled = SourceButton.IsEnabled = OutputButton.IsEnabled = Source.IsEnabled = Output.IsEnabled = Offline.IsEnabled = false; CancelButton.IsEnabled = true; Log.Clear();
        var source = Source.Text; var output = Output.Text; var offline = Offline.IsChecked == true; var token = cancellation.Token;
        void Write(string line) => Dispatcher.InvokeAsync(() => { Stage.Text = line.Length > 180 ? line[..180] : line; Log.AppendText(line + Environment.NewLine); Log.ScrollToEnd(); });
        try { var built = await Task.Run(() => BuildPipeline.Run(new(source, output, install, offline), Write, token)); result = built.Directory; OpenButton.IsEnabled = true; Stage.Text = "Сборка готова: " + result; }
        catch (OperationCanceledException) { Stage.Text = "Сборка отменена. Результат не опубликован."; }
        catch (Exception ex) { Stage.Text = "Сборка не завершена: " + ex.Message; Log.AppendText(ex + Environment.NewLine); }
        finally { cancellation.Dispose(); cancellation = null; BuildButton.IsEnabled = SourceButton.IsEnabled = OutputButton.IsEnabled = Source.IsEnabled = Output.IsEnabled = Offline.IsEnabled = true; CancelButton.IsEnabled = false; }
    }
    private void Cancel(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    private void OpenResult(object sender, RoutedEventArgs e) { if (result != null) Process.Start(new ProcessStartInfo(result) { UseShellExecute = true }); }
}
