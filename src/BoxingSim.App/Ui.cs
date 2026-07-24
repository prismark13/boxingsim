namespace BoxingSim.App;

/// <summary>Small presentation helpers shared across pages.</summary>
public static class Ui
{
    /// <summary>A 1–15 rating's colour, tiered by class: gold for the elite, then cyan, blue and slate.</summary>
    public static string Ovr(int v) =>
        v >= 13 ? "#f0b73e" :   // all-time great / champion gold (13–15)
        v >= 11 ? "#2fd0d8" :   // cyan (contender)
        v >= 8  ? "#3d9bff" :   // accent blue (national/solid)
        v >= 6  ? "#8aa0bd" :   // steel (journeyman)
                  "#727f93";    // slate (fringe)

    /// <summary>A short country code (e.g. "USA", "ENG") instead of a flag emoji.</summary>
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
