using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop;

/// <summary>Presentation helpers. Same rating tiers and country codes as the web build, expressed as WPF brushes.</summary>
public static class Ui
{
    /// <summary>A 1-15 rating's colour, tiered by class. It runs warm-to-cool DOWN the scale rather than
    /// through the player's blue: blue now means "you" everywhere else in the app, and a badge on a stranger's
    /// name reading in it said the wrong thing. Gold at the top, and the tiers below cool and fade toward the
    /// surfaces, so a fringe fighter's number recedes and a great one's does not.</summary>
    public static Brush Ovr(int v) =>
        v >= 13 ? Freeze("#FFC24D") :   // all-time great / champion gold
        v >= 11 ? Freeze("#E8A05A") :   // burnished (contender)
        v >= 8 ? Freeze("#B99B79") :    // brass (national / solid)
        v >= 6 ? Freeze("#8A7F70") :    // pewter (journeyman)
                 Freeze("#6C6357");     // faint (fringe)

    private static readonly Dictionary<string, Brush> Cache = new();
    private static Brush Freeze(string hex)
    {
        if (Cache.TryGetValue(hex, out var b)) return b;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }

    public static string Code(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return "";
        if (Codes.TryGetValue(country, out var c)) return c;
        return country.Length >= 3 ? country[..3].ToUpperInvariant() : country.ToUpperInvariant();
    }

    private static readonly Dictionary<string, string> Codes = new()
    {
        ["USA"] = "USA", ["England"] = "ENG", ["Scotland"] = "SCO", ["Wales"] = "WAL", ["Ireland"] = "IRL",
        ["Canada"] = "CAN", ["Mexico"] = "MEX", ["Argentina"] = "ARG", ["Cuba"] = "CUB", ["Venezuela"] = "VEN",
        ["Puerto Rico"] = "PUR", ["Brazil"] = "BRA", ["Germany"] = "GER", ["Italy"] = "ITA", ["France"] = "FRA",
        ["Sweden"] = "SWE", ["Russia"] = "RUS", ["Ukraine"] = "UKR", ["Poland"] = "POL", ["Nigeria"] = "NGA",
        ["South Africa"] = "RSA", ["Australia"] = "AUS", ["New Zealand"] = "NZL", ["Kazakhstan"] = "KAZ"
    };
}

/// <summary>Colours a class number by its tier, so the rating pills read the same as the web build.</summary>
/// <summary>Text in capitals, for the small badges. Not to be confused with <see cref="ShoutConverter"/>,
/// which returns a BOOL for whether a line of commentary is dramatic — binding a badge through that one
/// printed the word "False" where a belt should have been.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => (v as string ?? "").ToUpperInvariant();
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>A country as the three letters shown everywhere else. Exists so a bill line can render a
/// fighter's nationality straight from the model without the view model having to pre-format every corner
/// of every supporting bout.</summary>
public sealed class CountryCodeConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => Ui.Code(v as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class ClassColourConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => Ui.Ovr(value is int i ? i : 0);
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>true → Visible, false → Collapsed. Pass "invert" as the parameter to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        bool v = value is bool b && b;
        if (p as string == "invert") v = !v;
        return v ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>true -> "on", so a style Trigger can highlight the selected item of a small segmented control.</summary>
public sealed class OnOffConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is true ? "on" : "off";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>True for the loud moments in a round's commentary — a knockdown, a man hurt, a stoppage — so they
/// stand out from the routine "his round (12-8 landed)" recap.</summary>
public sealed class ShoutConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is string s && (s.Contains("DOWN") || s.Contains("STOPS") || s.Contains("KNOCKS OUT")
                              || s.Contains("badly hurt") || s.Contains("disqualified") || s.Contains("is cut"));
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>A weight class's display name ("Light Heavyweight"), not the raw enum name ("LightHeavyweight").</summary>
public sealed class WeightClassNameConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is WeightClass w ? w.DisplayName() : value?.ToString() ?? "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Shows an element only when the bound enum equals the ConverterParameter — how the shell swaps pages
/// and how the sidebar highlights the current one.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.Equals(value?.ToString(), p as string, StringComparison.Ordinal)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible once a collection has anything in it.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is int n && n > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible only when every bound condition is true. The scorecards want two: the fight is over,
/// and it went to the cards at all.</summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type t, object? p, CultureInfo c) =>
        values.All(v => v is bool b && b) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object[] ConvertBack(object? v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>A 0–1 fraction and the available width → an actual pixel width, for the tale-of-the-tape bars.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type t, object? p, CultureInfo c)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double available) return 0d;
        if (double.IsNaN(available) || double.IsInfinity(available) || available <= 0) return 0d;
        return Math.Max(0, Math.Min(1, fraction)) * available;
    }
    public object[] ConvertBack(object? value, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Empty or null string → Collapsed. Keeps optional detail lines from leaving a gap.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Paints a stamina bar from how much a man has left. Full, it is his corner's colour; as the tank
/// empties it warms through amber and finishes red, so a fighter in trouble is obvious from the colour alone
/// without reading a number. The brush is a gradient rather than a flat fill — the inner end, nearest the
/// centre of the ring where the two bars meet, is the deeper shade, so the bar reads as draining toward the
/// middle rather than as a block that happens to be short.
///
/// The parameter picks the corner: "mine" for the player's blue, anything else for the opponent's red.</summary>
public sealed class GasBrushConverter : IValueConverter
{
    private static readonly Color Blue = Color.FromRgb(0x4F, 0xA3, 0xFF);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x7A, 0x47);
    private static readonly Color Warn = Color.FromRgb(0xFF, 0xC2, 0x4D);
    private static readonly Color Spent = Color.FromRgb(0xFF, 0x4D, 0x4D);

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t),
                             (byte)(a.G + (b.G - a.G) * t),
                             (byte)(a.B + (b.B - a.B) * t));
    }

    private static Color Shade(Color c, double by)
    {
        by = Math.Clamp(by, 0, 1);
        return Color.FromRgb((byte)(c.R * by), (byte)(c.G * by), (byte)(c.B * by));
    }

    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        double gas = value is double d ? Math.Clamp(d, 0, 1) : 1;
        bool mine = (parameter as string) == "mine";
        Color healthy = mine ? Blue : Red;

        // A man with most of his tank left should look like HIMSELF. The first cut started blending toward
        // amber the moment he dropped below full, so a fighter at 80% was already drawn in a muddy khaki that
        // said "in trouble" three rounds before he was. He now holds his corner's colour down to seven-tenths,
        // warms through the middle of the fight, and only goes red when he is genuinely running on empty.
        Color now = gas >= 0.70 ? healthy
            : gas >= 0.40 ? Mix(Warn, healthy, (gas - 0.40) / 0.30)
            : Mix(Spent, Warn, Math.Max(0, gas - 0.12) / 0.28);

        // Deeper at the end nearest the centre, brighter at the outer end.
        var g = new LinearGradientBrush
        {
            StartPoint = mine ? new Point(1, 0) : new Point(0, 0),
            EndPoint = mine ? new Point(0, 0) : new Point(1, 0)
        };
        g.GradientStops.Add(new GradientStop(Shade(now, 0.72), 0));
        g.GradientStops.Add(new GradientStop(now, 1));
        g.Freeze();
        return g;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
