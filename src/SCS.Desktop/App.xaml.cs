using System.IO;
using System.Windows;

namespace SCS.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test" ? Path.GetFullPath(e.Args[1]) : null;
        try
        {
            var statePath = smoke is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubsistenceCustomSettings", "TXT-v1")
                : Path.Combine(smoke, "State");
            var window = new MainWindow(new WorkspaceStore(statePath));
            MainWindow = window;
            if (smoke is not null)
            {
                window.ShowInTaskbar = false; window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000;
                window.Loaded += async (_, _) =>
                {
                    try { await window.SmokeTest(smoke); Shutdown(0); }
                    catch (Exception ex) { Directory.CreateDirectory(smoke); File.WriteAllText(Path.Combine(smoke, "error.txt"), ex.ToString()); Shutdown(1); }
                };
            }
            window.Show();
        }
        catch (Exception ex)
        {
            if (smoke is not null) { Directory.CreateDirectory(smoke); File.WriteAllText(Path.Combine(smoke, "error.txt"), ex.ToString()); }
            else MessageBox.Show("The workspace could not be opened. Existing files were preserved.\n\n" + ex.Message, "Subsistence Custom Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
