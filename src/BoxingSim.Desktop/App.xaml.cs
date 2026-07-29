using System.Windows;
using System.Windows.Threading;

namespace BoxingSim.Desktop;

public partial class App : Application
{
    public App()
    {
        // A career is hours of play. An unhandled exception must report itself rather than vanish the window.
        DispatcherUnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"{e.Exception.Message}\n\nYour last saved career is untouched at:\n{DesktopCareerService.SavePath}",
                        "The Final Bell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
