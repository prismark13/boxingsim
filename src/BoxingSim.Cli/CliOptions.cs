namespace BoxingSim.Cli;

/// <summary>Minimal command-line parsing for the simulator.</summary>
public sealed class CliOptions
{
    public int Boxers { get; private set; } = 1000;
    public int Seasons { get; private set; } = 10;
    public int Seed { get; private set; } = 12345;
    public bool FeatureBout { get; private set; } = true;
    public bool Calibrate { get; private set; }
    public bool SimTest { get; private set; }
    public bool Cards { get; private set; }
    public string? RosterPath { get; private set; }
    public string? OverridesPath { get; private set; }
    public string? HtmlPath { get; private set; }
    public string? FightUiPath { get; private set; }
    public string? HomePath { get; private set; }
    public string? OutPath { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    o.ShowHelp = true;
                    break;
                case "-b" or "--boxers":
                    o.Boxers = Next(args, ref i, o.Boxers);
                    break;
                case "-s" or "--seasons":
                    o.Seasons = Next(args, ref i, o.Seasons);
                    break;
                case "--seed":
                    o.Seed = Next(args, ref i, o.Seed);
                    break;
                case "--no-feature":
                    o.FeatureBout = false;
                    break;
                case "--calibrate":
                    o.Calibrate = true;
                    break;
                case "--simtest":
                    o.SimTest = true;
                    break;
                case "--cards":
                    o.Cards = true;
                    break;
                case "--roster":
                    if (i + 1 < args.Length) o.RosterPath = args[++i];
                    break;
                case "--html":
                    if (i + 1 < args.Length) o.HtmlPath = args[++i];
                    break;
                case "--fightui":
                    if (i + 1 < args.Length) o.FightUiPath = args[++i];
                    break;
                case "--home":
                    if (i + 1 < args.Length) o.HomePath = args[++i];
                    break;
                case "--overrides":
                    if (i + 1 < args.Length) o.OverridesPath = args[++i];
                    break;
                case "--out":
                    if (i + 1 < args.Length) o.OutPath = args[++i];
                    break;
            }
        }
        o.Boxers = Math.Max(16, o.Boxers);
        o.Seasons = Math.Max(1, o.Seasons);
        return o;
    }

    private static int Next(string[] args, ref int i, int fallback) =>
        i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? (i++, v).v : fallback;

    public static void PrintUsage()
    {
        Console.WriteLine("Boxing Simulator");
        Console.WriteLine("Usage: BoxingSim.Cli [options]");
        Console.WriteLine();
        Console.WriteLine("  -b, --boxers <n>    Number of fighters in the world (default 1000)");
        Console.WriteLine("  -s, --seasons <n>   Seasons to simulate (default 10)");
        Console.WriteLine("      --seed <n>      RNG seed for reproducible worlds (default 12345)");
        Console.WriteLine("      --no-feature    Skip the round-by-round featured bout");
        Console.WriteLine("      --cards         Print fighter cards (demo legends deck) and exit");
        Console.WriteLine("      --roster <file> Use a JSON deck of fighter cards (with --cards/--html)");
        Console.WriteLine("      --html <file>   Export a sleek self-contained HTML card viewer and exit");
        Console.WriteLine("      --overrides <f> Upgrade matching fighters from a curated library JSON (e.g. _library.json)");
        Console.WriteLine("  -h, --help          Show this help");
    }
}
