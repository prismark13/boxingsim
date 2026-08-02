using System.Globalization;
using System.Text;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>A pin in the simulation's behaviour, for refactoring against.
///
/// The other tests here assert PROPERTIES — nobody climbs past his frame, an undisputed champion also holds
/// the lineal title, a save round-trips. Those catch a rule being broken. They do not catch a refactor
/// quietly changing which fighter won a bout in 1981, because they never look at that.
///
/// This one does. It runs a fixed seed through a career and through a universe, writes down everything
/// observable about the finished worlds — every record, ranking, champion, belt, Hall of Famer and headline
/// — and compares that against a stored fingerprint. Any change to the order in which random numbers are
/// drawn, or to any calculation feeding any of it, moves the fingerprint and fails here.
///
/// That makes it the opposite of the other tests in one important way: THIS TEST IS EXPECTED TO FAIL when
/// behaviour is deliberately changed. When it does, read the diff, satisfy yourself the change is the one
/// you meant to make, and update the expected hash. Refusing to update it is not the point; updating it
/// without reading it is.
/// </summary>
public class GoldenMasterTests
{
    /// <summary>Everything a finished world can be asked about, flattened to one deterministic string.</summary>
    private static string Fingerprint(CareerGame g)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.Append("DATE ").Append(g.Date.ToString("yyyy-MM-dd", inv)).Append('\n');
        sb.Append("PLAYER ").Append(g.Player.Name).Append(' ').Append(g.Player.Record)
          .Append(" ovr=").Append(g.Player.Overall.ToString(inv))
          .Append(" cls=").Append(g.Player.Class.ToString(inv))
          .Append(" wc=").Append(g.Player.WeightClass)
          .Append(" retired=").Append(g.Player.Retired).Append('\n');

        // Every fighter the world holds, in a stable order, with what became of him.
        foreach (var b in g.EveryFighter.OrderBy(b => b.Id))
            sb.Append("F ").Append(b.Id.ToString(inv)).Append(' ').Append(b.Name)
              .Append(' ').Append(b.Record)
              .Append(' ').Append(b.WeightClass)
              .Append(" age=").Append(b.Age.ToString(inv))
              .Append(" ovr=").Append(b.Overall.ToString(inv))
              .Append(" pot=").Append(b.Potential.ToString(inv))
              .Append(" ret=").Append(b.Retired)
              .Append(" hist=").Append(b.History.Count.ToString(inv)).Append('\n');

        // The boards, exactly as the UI would read them.
        foreach (var wc in g.Divisions)
        {
            sb.Append("RANK ").Append(wc).Append(' ');
            foreach (var b in g.RankingOf(wc, 15)) sb.Append(b.Name).Append('|');
            sb.Append('\n');
        }
        foreach (var d in g.ChampionsBoard())
            sb.Append("CHAMP ").Append(d.Division)
              .Append(" wba=").Append(d.Wba?.Name ?? "-")
              .Append(" wbc=").Append(d.Wbc?.Name ?? "-")
              .Append(" ibf=").Append(d.Ibf?.Name ?? "-")
              .Append(" lineal=").Append(d.Lineal?.Name ?? "-").Append('\n');

        foreach (var b in g.PoundForPound(15)) sb.Append("P4P ").Append(b.Name).Append('\n');

        foreach (var m in g.HallOfFame.OrderBy(m => m.Name, StringComparer.Ordinal))
            sb.Append("HOF ").Append(m.Name).Append(' ').Append(m.Record)
              .Append(" peak=").Append(m.PeakOverall.ToString(inv))
              .Append(" def=").Append(m.Defenses.ToString(inv))
              .Append(" weights=").Append(m.WeightTitles.ToString(inv)).Append('\n');

        // And the story it told along the way.
        foreach (var e in g.Log)
            sb.Append("LOG ").Append(e.On.ToString("yyyy-MM-dd", inv)).Append(' ').Append(e.Text).Append('\n');

        return sb.ToString();
    }

    private static string Hash(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>Write the fingerprint out beside the test binary so a failure can be diffed rather than
    /// guessed at. Nothing reads it back — it exists purely so that when the hash moves you can see what
    /// moved with it.</summary>
    private static void Dump(string name, string body)
    {
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"golden-{name}.txt"), body); }
        catch (IOException) { /* diagnostics only; never fail the test over this */ }
    }

    [Fact]
    public void ACareerRunsIdentically()
    {
        var rng = new Random(12345);
        var player = CareerGame.CreatePlayer(rng, "Golden Master", "USA", WeightClass.Welterweight, 86);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Welterweight,
                               seedHistory: true);
        for (int i = 0; i < 25 && g.Offer is not null && !player.Retired; i++) g.TakeOffer();

        var body = Fingerprint(g);
        Dump("career", body);
        // A6694BC3EF2FA89F -> DD25B557DFBF1848 when resolving a bout stopped moving the world clock (the same
        // 25 fights now span 5.3 years rather than 7.8), then -> here when the player's card became a real
        // bill: drawn across the divisions and ordered by what each fight is worth. The universe fingerprint
        // did NOT move for this one, which is the confirmation it belongs to the player's card and nothing
        // else — a universe has no player, so it never draws one.
        // ... then -> here when a fortnightly card became fortnightly by the calendar rather than by the step,
        // and a man had to be rested to be MATCHED rather than matched and then moved. See ClockTests.
        // ... then -> here when the ordinary matchmaker stopped picking a man and rewriting that choice six
        // times, and started scoring every eligible opponent once. OpponentTests is what says the intent
        // survived; this only says something changed.
        // ... then -> here when the slate widened to three. The universe fingerprint did NOT move, which is
        // the confirmation it is the player's: a universe's ghost is retired, so he has no division to be
        // ranked in and his slate stays one wide.
        Assert.Equal("AC89232766FC4C40", Hash(body));
    }

    [Fact]
    public void AUniverseRunsIdentically()
    {
        var u = new Universe(new UniverseSettings
        {
            StartYear = 1965, WarmupYears = 4, Seed = 4242,
            Divisions = new[] { WeightClass.Middleweight },
        }, Fixtures.Roster.ToList());
        for (int w = 0; w < 52 * 3; w++) u.PlayWeek();

        var body = Fingerprint(u.World);
        Dump("universe", body);
        Universe.Release();
        // BF3BBA6241417D32 -> F57BB7444F6C045B when the doubled turn of the year came off the universe path,
        // then -> here when a bout stopped moving the clock. 156 weeks of this universe used to cover twelve
        // calendar years because each card dragged the date on; it covers three now, which is what "52 * 3
        // weeks" was always supposed to mean. See ClockTests.
        // ... then -> here with the card cadence and the rest rules. The universe moves for these where it did
        // not for the player's bill, which is right: cards and rest are the world's, a bill is the player's.
        // ... and here for the scored matchmaker, which looks wrong for a world with no player in it. It is
        // not: a universe builds ONE offer, for the ghost it retires before the first week, and that single
        // call draws a different count of random numbers than it used to. Everything after it shifts.
        Assert.Equal("0E780E476CDF3EDC", Hash(body));
    }
}
