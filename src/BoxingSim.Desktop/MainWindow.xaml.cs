using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace BoxingSim.Desktop;

public partial class MainWindow : Window
{
    private readonly CareerViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    /// <summary>Show the window first, then load. Parsing the roster before the window appeared made a cold
    /// start look like a hang; this way the setup card is on screen while the fighters are read in the
    /// background, and an existing save is picked up as soon as it can be.</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _vm.WarmupAsync();
        if (DesktopCareerService.HasSave) _vm.ContinueCareer.Execute(null);
    }

    // The call auto-scrolls to the newest line, but yields the moment you scroll back — otherwise pausing to
    // read what just happened would be undone by the next line arriving.
    private bool _followFeed = true;

    private void FeedScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (e.ExtentHeightChange == 0)                       // a real user scroll, not new content
            _followFeed = sv.VerticalOffset >= sv.ScrollableHeight - 2;
        else if (_followFeed)
            sv.ScrollToEnd();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UseDarkTitleBar();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Ask DWM for a dark title bar. Without it the window wears light chrome above a black app, which
    /// looks like a rendering fault. Attribute 20 is the current id; 19 is the pre-20H1 one. Both are best-effort
    /// — an older Windows simply ignores them and keeps the light bar.</summary>
    private void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }
}
