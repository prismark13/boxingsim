using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BoxingSim.Desktop;

public partial class MainWindow : Window
{
    private readonly CareerViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // Pick up an existing save on launch so the app opens where the player left off.
        if (DesktopCareerService.HasSave) _vm.ContinueCareer.Execute(null);
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
