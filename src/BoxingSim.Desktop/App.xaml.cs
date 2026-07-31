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
        // The dialog gets the readable version; the file gets everything, because a message box is the worst
        // possible place to read a stack trace and the detail is gone the moment it is dismissed.
        string? log = TryWriteCrashLog(e.Exception);
        MessageBox.Show($"{Explain(e.Exception)}\n\nYour last saved career is untouched at:\n{DesktopCareerService.SavePath}"
                        + (log is null ? "" : $"\n\nThe full details are in:\n{log}"),
                        "The Final Bell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static string? TryWriteCrashLog(Exception ex)
    {
        try
        {
            string path = System.IO.Path.Combine(DesktopCareerService.SaveDirectory, "crash.txt");
            System.IO.Directory.CreateDirectory(DesktopCareerService.SaveDirectory);
            System.IO.File.WriteAllText(path, $"{DateTime.Now:u}\n\n{ex}\n", System.Text.Encoding.UTF8);
            return path;
        }
        catch { return null; }   // a crash report that crashes is no use to anybody
    }

    /// <summary>An exception down to the thing that actually went wrong.
    ///
    /// The outer message is often the least useful sentence available: a failure anywhere in the window's
    /// markup or construction surfaces as "the invocation of the constructor on type MainWindow threw an
    /// exception", which says only that something broke somewhere. What went wrong — and, for markup, the
    /// line it is on — is in the inner exception, and that was being thrown away.</summary>
    private static string Explain(Exception ex)
    {
        var said = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            string line = e is System.Windows.Markup.XamlParseException x && x.LineNumber > 0
                          ? $"{e.Message} (line {x.LineNumber}, position {x.LinePosition})"
                          : e.Message;
            if (!said.Contains(line)) said.Add(line);
        }
        return string.Join("\n\n", said);
    }
}
