using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop;

/// <summary>Presentation helpers. Same rating tiers and country codes as the web build, expressed as WPF brushes.</summary>
public static class Ui
{
    /// <summary>A 1–15 rating's colour, tiered by class: gold for the elite, then cyan, blue and slate.</summary>
    public static Brush Ovr(int v) =>
        v >= 13 ? Freeze("#F0B73E") :   // all-time great / champion gold
        v >= 11 ? Freeze("#2FD0D8") :   // cyan (contender)
        v >= 8 ? Freeze("#3D9BFF") :    // accent blue (national/solid)
        v >= 6 ? Freeze("#8AA0BD") :    // steel (journeyman)
                 Freeze("#727F93");     // slate (fringe)

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
