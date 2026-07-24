using BoxingSim.Cli;
using BoxingSim.Core.League;
using BoxingSim.Core.Roster;

// ---- Options ----------------------------------------------------------------
var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintUsage();
    return;
}

var rng = new Random(options.Seed);

if (args.Contains("--career"))
{
    var cdefs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
        RosterIo.ApplyOverrides(cdefs, RosterIo.Load(options.OverridesPath));
    var cdeck = RosterIo.DedupeByName(RosterIo.ToBoxers(cdefs));
    CareerDemo.Run(cdeck, rng);
    return;
}

if (options.Calibrate)
{
    Report.PrintCalibration(rng);
    return;
}

if (options.SimTest)
{
    var defs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
        RosterIo.ApplyOverrides(defs, RosterIo.Load(options.OverridesPath));
    var deck = RosterIo.DedupeByName(RosterIo.ToBoxers(defs));
    SimTest.Run(deck, options.Seed);
    return;
}

if (options.HomePath is not null)
{
    var defs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
        RosterIo.ApplyOverrides(defs, RosterIo.Load(options.OverridesPath));
    var deck = RosterIo.DedupeByName(RosterIo.ToBoxers(defs));
    HomeExporter.Export(options.HomePath, deck, "Heavyweight Boxing — All-Time",
        "heavyweights-master.html", "fight-night.html");
    Console.WriteLine($"Wrote home page to {Path.GetFullPath(options.HomePath)}");
    return;
}

if (options.FightUiPath is not null)
{
    var defs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
        RosterIo.ApplyOverrides(defs, RosterIo.Load(options.OverridesPath));
    var deck = RosterIo.DedupeByName(RosterIo.ToBoxers(defs));
    string ttl = options.RosterPath is not null
        ? Path.GetFileNameWithoutExtension(options.RosterPath.Split(',')[0]).Replace('-', ' ')
        : "Boxing";
    FightSimExporter.Export(options.FightUiPath, deck, ttl);
    Console.WriteLine($"Wrote fight simulator ({deck.Count} fighters) to {Path.GetFullPath(options.FightUiPath)}");
    return;
}

if (options.HtmlPath is not null)
{
    var defs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
    {
        int up = RosterIo.ApplyOverrides(defs, RosterIo.Load(options.OverridesPath));
        Console.WriteLine($"Applied library overrides to {up} fighter(s).");
    }
    var deck = RosterIo.DedupeByName(RosterIo.ToBoxers(defs));
    if (options.OutPath is not null)
    {
        RosterIo.Save(options.OutPath, deck.Select(FighterDefinition.FromBoxer));
        Console.WriteLine($"Wrote consolidated deck of {deck.Count} fighters to {Path.GetFullPath(options.OutPath)}");
    }
    string title = options.RosterPath is not null
        ? Path.GetFileNameWithoutExtension(options.RosterPath).Replace('-', ' ')
        : "Boxing Legends";
    HtmlExporter.Export(options.HtmlPath, deck, title);
    Console.WriteLine($"Wrote {deck.Count} fighter cards to {Path.GetFullPath(options.HtmlPath)}");
    return;
}

if (options.Cards)
{
    var defs = LoadDecks(options.RosterPath);
    if (options.OverridesPath is not null)
    {
        int up = RosterIo.ApplyOverrides(defs, RosterIo.Load(options.OverridesPath));
        Console.WriteLine($"Applied library overrides to {up} fighter(s).");
    }
    var deck = RosterIo.DedupeByName(RosterIo.ToBoxers(defs));
    Console.WriteLine($"FIGHTER CARDS ({deck.Count})");
    Console.WriteLine();
    Report.PrintCardDeck(deck, rng);
    return;
}

Console.WriteLine("====================================================");
Console.WriteLine("           B O X I N G   S I M U L A T O R          ");
Console.WriteLine("====================================================");
Console.WriteLine($"Roster: {options.Boxers} fighters | Seasons: {options.Seasons} | Seed: {options.Seed}");
Console.WriteLine();

// ---- Build the world --------------------------------------------------------
var world = new World(rng);
world.Seed(options.Boxers);
Console.WriteLine($"Generated {world.ActiveCount} fighters across {world.Divisions.Count} divisions.");
Console.WriteLine();

var sim = new SeasonSimulator(world, rng);

// ---- Run the seasons --------------------------------------------------------
for (int s = 0; s < options.Seasons; s++)
{
    var report = sim.RunSeason(options.Boxers);
    Report.PrintSeasonSummary(report);
}

// ---- Final state ------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("####################################################");
Console.WriteLine($"   FINAL STANDINGS after {options.Seasons} season(s)");
Console.WriteLine("####################################################");
Report.PrintChampions(world);
Report.PrintPoundForPound(world, 15);

// ---- A featured bout, round by round ----------------------------------------
if (options.FeatureBout)
{
    Console.WriteLine();
    Console.WriteLine("=========== FEATURED BOUT (round by round) ===========");
    Report.PrintFeaturedBout(world, rng);
}

// Load one or more comma-separated roster files (or the built-in legends deck) into a single deck.
static List<FighterDefinition> LoadDecks(string? rosterPath)
{
    if (rosterPath is null) return Legends.Deck();
    var defs = new List<FighterDefinition>();
    foreach (var p in rosterPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        defs.AddRange(RosterIo.Load(p));
    return defs;
}
