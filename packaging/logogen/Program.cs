using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Render the app's bell mark to the PNG sizes the Microsoft Store and Windows shell want.
//
// The mark only ever existed as vector geometry inside Theme.xaml — deliberately, so it stays sharp from a
// 16px taskbar icon upward and there is no image file to ship or license. A Store package cannot take
// geometry, though: it wants a specific set of PNGs at specific sizes. So they are generated from the same
// path data rather than drawn by hand, which means the Store tile and the in-app logo can never drift apart.
internal static class Program
{
    // The bell, exactly as Theme.xaml declares it, on a 24x24 canvas.
    private const string Dome = "M12,4.6 C7.4,4.6 4.7,8.3 4.7,13 L4.7,15.6 L19.3,15.6 L19.3,13 C19.3,8.3 16.6,4.6 12,4.6 Z";

    private static Geometry Bell()
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new RectangleGeometry(new Rect(11.1, 1.4, 1.8, 2.4), 0.6, 0.6));   // the post
        g.Children.Add(new RectangleGeometry(new Rect(8.4, 3.2, 7.2, 1.5), 0.7, 0.7));    // the bracket
        g.Children.Add(Geometry.Parse(Dome));                                              // the dome
        g.Children.Add(new RectangleGeometry(new Rect(3.1, 15.9, 17.8, 2.1), 1, 1));      // the rim
        g.Children.Add(new EllipseGeometry(new Point(12, 20.6), 1.7, 1.7));               // the striker
        return g;
    }

    /// <param name="pad">Fraction of the tile left empty around the mark. The Store's own guidance is that a
    /// logo should not run to the edge of its tile; square tiles want more air than the small icons do.</param>
    private static void Write(string path, int w, int h, bool transparent, double pad)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // The app's own background, so a tile sits in the dark the way the app does.
            if (!transparent)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x0F)), null, new Rect(0, 0, w, h));

            double box = Math.Min(w, h) * (1 - pad * 2);
            double scale = box / 24.0;
            var bell = Bell();

            dc.PushTransform(new TranslateTransform((w - 24 * scale) / 2, (h - 24 * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            // The same gold gradient the app uses, top to bottom.
            var brush = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xD2, 0x77), Color.FromRgb(0xE0, 0x9A, 0x28), 90);
            dc.DrawGeometry(brush, null, bell);
            dc.Pop();
            dc.Pop();
        }

        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
        Console.WriteLine($"  {Path.GetFileName(path),-34} {w}x{h}");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "assets";
        Console.WriteLine("Store and shell assets, generated from the same geometry the app draws:");

        // The Store's required set. Square logos carry the background; the 44x44 app icon is transparent so
        // it sits correctly on the taskbar and in the title bar.
        Write(Path.Combine(outDir, "Square44x44Logo.png"), 44, 44, true, 0.10);
        Write(Path.Combine(outDir, "Square44x44Logo.targetsize-24_altform-unplated.png"), 24, 24, true, 0.04);
        Write(Path.Combine(outDir, "Square44x44Logo.targetsize-16_altform-unplated.png"), 16, 16, true, 0.02);
        Write(Path.Combine(outDir, "Square44x44Logo.targetsize-32_altform-unplated.png"), 32, 32, true, 0.04);
        Write(Path.Combine(outDir, "Square44x44Logo.targetsize-48_altform-unplated.png"), 48, 48, true, 0.06);
        Write(Path.Combine(outDir, "Square44x44Logo.targetsize-256_altform-unplated.png"), 256, 256, true, 0.10);
        Write(Path.Combine(outDir, "Square150x150Logo.png"), 150, 150, false, 0.24);
        Write(Path.Combine(outDir, "Wide310x150Logo.png"), 310, 150, false, 0.24);
        Write(Path.Combine(outDir, "Square310x310Logo.png"), 310, 310, false, 0.26);
        Write(Path.Combine(outDir, "Square71x71Logo.png"), 71, 71, false, 0.20);
        Write(Path.Combine(outDir, "StoreLogo.png"), 50, 50, false, 0.16);
        Write(Path.Combine(outDir, "SplashScreen.png"), 620, 300, false, 0.30);
    }
}
