using System.Text.Json;
using System.Text.Json.Serialization;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using BoxingSim.Core.Roster;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>The real roster, loaded once — deserializing 2200 fighters per test would dominate the run.</summary>
public static class Fixtures
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static IReadOnlyList<Boxer>? _roster;

    public static IReadOnlyList<Boxer> Roster => _roster ??= Load();

    private static IReadOnlyList<Boxer> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "fighters.json");
        var defs = JsonSerializer.Deserialize<List<FighterDefinition>>(File.ReadAllText(path), Opts)!;
        return RosterIo.DedupePeople(RosterIo.ToBoxers(defs));
    }
}

/// <summary>A world run forward through a full seeded history — expensive to build, so every integrity
/// assertion that only reads the finished world shares this one instance.
///
/// This is the one world in the suite that cannot come from Worlds.Fresh, and it is worth saying why: the
/// assertions below read the HALL OF FAME — a hundred title bouts, twenty unifications, a hundred-odd retired
/// champions with their ledgers intact. That is what seedHistory buys and what a twelve-year warm-up has none
/// of, because the Hall is cleared at the player's first day unless the sport's whole past came with it. Seven
/// tests share this one build; that is the saving available here.</summary>
public sealed class SeededWorld
{
    public CareerGame Game { get; }

    public SeededWorld()
    {
        var rng = new Random(7);
        var player = CareerGame.CreatePlayer(rng, "Fixture Player", "USA", WeightClass.Lightweight, 84);
        // Fresh clones: CareerGame mutates the roster it's handed.
        Game = new CareerGame(1978, player, Fixtures.Roster.ToList(), rng, WeightClass.Lightweight, seedHistory: true);
        // Run the world on through a career's worth of years. The seeded history alone only exercises the warmup
        // path; the bugs these tests guard were all in the LIVE path, where the world advances a fortnight at a
        // time around the player's fights.
        for (int i = 0; i < 40 && Game.Offer is not null && !player.Retired; i++) Game.TakeOffer();
    }
}

/// <summary>Invariants about the world the simulation produces — the championship picture has to hold together
/// well enough that a player reading the news, the rankings or a fighter's ledger is never shown something
/// impossible. Each of these guards a bug that actually shipped.</summary>
public class CareerIntegrityTests : IClassFixture<SeededWorld>
{
    private readonly CareerGame _g;
    public CareerIntegrityTests(SeededWorld w) => _g = w.Game;

    private static bool IsSingleBelt(string? n) => n is "WBA title" or "WBC title" or "IBF title" or "World title";
    private static bool IsMerged(string? n) => n is "unification" or "Undisputed title";

    /// <summary>Every ledger in the world, not just the Hall's.
    ///
    /// It read the Hall of Fame because those snapshots were the only COMPLETE records — everyone else's was
    /// capped at sixty bouts and had lost its early years. That cap is gone, so the whole roster is readable
    /// now, and the sample should be the whole roster: a couple of dozen inductees is a small enough sample
    /// that the unification count swung from 32 to 8 on a change that merely reordered the random stream,
    /// which is a test measuring luck rather than the sim.</summary>
    private List<IReadOnlyList<BoutLine>> TitleLedgers() =>
        _g.EveryFighter.Select(b => (IReadOnlyList<BoutLine>)b.History)
                       .Concat(_g.HallOfFame.Select(m => (IReadOnlyList<BoutLine>)m.History))
                       .Where(h => h.Count > 0).ToList();

    [Fact]
    public void TitleBoutsAreNeverCrammedDaysApart()
    {
        int bunched = 0, total = 0;
        foreach (var hist in TitleLedgers())
        {
            var tl = hist.Where(h => IsSingleBelt(h.Note) || IsMerged(h.Note)).OrderBy(h => h.Date).ToList();
            DateOnly? prev = null;
            foreach (var h in tl)
            {
                total++;
                if (prev is DateOnly p && (h.Date.DayNumber - p.DayNumber) is > 0 and < 25) bunched++;
                prev = h.Date;
            }
        }
        Assert.True(total > 100, $"expected a meaningful sample of title bouts, got {total}");
        // A champion needs a real gap between title fights. A handful survive where a belt splits and is
        // immediately contested; a systemic failure looks like dozens.
        Assert.True(bunched * 100 <= total, $"{bunched} of {total} title bouts landed under 25 days apart");
    }

    [Fact]
    public void SingleBeltBoutsDoNotFollowAMergeImmediately()
    {
        int offenders = 0, merges = 0;
        foreach (var hist in TitleLedgers())
        {
            var tl = hist.Where(h => IsSingleBelt(h.Note) || IsMerged(h.Note)).OrderBy(h => h.Date).ToList();
            DateOnly? lastMerge = null;
            foreach (var h in tl)
            {
                if (IsMerged(h.Note)) { merges++; lastMerge = h.Date; }
                else if (lastMerge is DateOnly m && h.Date.DayNumber - m.DayNumber < 60) offenders++;
            }
        }
        // A sample-adequacy check, not the property. The old bar of 20 was read off a Hall-of-Fame-only sample
        // that happened to yield 32 on one seed, and a unification count is small and jumpy enough that any
        // change reordering the random stream moves it. Twelve is enough for the ratio below to mean something.
        Assert.True(merges > 12, $"expected a meaningful sample of unifications, got {merges}");
        // Once the belts merge there is one title, and splitting them again means a belt fell vacant and a
        // fight was ordered to fill it. The window used to be 200 days on the reasoning that re-splitting takes
        // a season — which stopped being true when vacancies started being BOOKED two to five months out
        // instead of settled the instant they appeared, so a legitimate refill now lands inside it by design.
        // Sixty days is the floor of that booking: a single-belt bout sooner than a vacancy can even be
        // ordered is still impossible, and that is the part worth guarding.
        Assert.True(offenders * 5 <= merges, $"{offenders} single-belt bouts within 200d of a merge, over {merges} merges");
    }

    [Fact]
    public void LinealChampionsAreCoherent()
    {
        foreach (var d in _g.ChampionsBoard())
        {
            if (d.Lineal is Boxer l)
            {
                Assert.False(l.Retired, $"{d.Division}: lineal champion {l.Name} has retired");
                Assert.Equal(d.Division, l.WeightClass);
            }
            // A man holding every belt going IS the man — the board must not show an undisputed champion
            // while the lineal title sits with somebody else.
            if (d.Undisputed is Boxer u) Assert.Equal(u.Id, d.Lineal?.Id);
        }
    }

    /// <summary>A career climbs as far as a body will carry it, and no further.
    ///
    /// This test used to assert a flat two-division limit, which is what the sim enforced — and which wrote
    /// the sport's best careers out of existence, since Leonard, Duran, Hearns and Pacquiao all climbed four
    /// divisions or more. The limit is now weight gained rather than divisions counted, so it admits those
    /// careers while still refusing to turn a flyweight into a heavyweight. Six divisions from the bottom of
    /// the scale and one from the top are the same forty per cent of bodyweight.</summary>
    [Fact]
    public void NobodyClimbsFurtherThanHisFrameAllows()
    {
        static double ScaleWeight(WeightClass wc) =>
            wc == WeightClass.Heavyweight ? 215 : wc.WeightLimitLbs();

        foreach (var wc in _g.Divisions)
            foreach (var b in _g.RankingOf(wc, 500))
                if (b.DebutWeight is WeightClass from)
                    Assert.True(ScaleWeight(b.WeightClass) <= ScaleWeight(from) * 1.40 + 0.01,
                                $"{b.Name} climbed from {from} ({ScaleWeight(from)}lb) to {b.WeightClass} " +
                                $"({ScaleWeight(b.WeightClass)}lb) — more than 40% above his debut weight");

        // And nobody wins belts in more divisions than the men who actually did it. Six is De La Hoya, and
        // he is the outer edge of what has ever happened.
        foreach (var m in _g.HallOfFame)
            Assert.True(m.WeightTitles <= 6, $"{m.Name} holds titles in {m.WeightTitles} divisions");
    }

    [Fact]
    public void VacantTitleHeadlinesAreDatedToTheirOwnBout()
    {
        // The world resolves a division at a time and back-dates bouts across the season, so an announcement
        // logged by the CALLER — after the clock was restored — could appear months before the fight that
        // decided it. Every such headline must carry its own bout's date.
        var ledgers = new Dictionary<string, IReadOnlyList<BoutLine>>();
        foreach (var wc in _g.Divisions)
            foreach (var b in _g.RankingOf(wc, 500))
                ledgers[b.Name] = b.History;
        foreach (var m in _g.HallOfFame) ledgers.TryAdd(m.Name, m.History);

        int checkedHeadlines = 0;
        foreach (var e in _g.Log)
        {
            int at = e.Text.IndexOf(" wins the vacant", StringComparison.Ordinal);
            if (at < 0) continue;
            if (!ledgers.TryGetValue(e.Text[..at], out var hist)) continue;   // pruned from the live roster
            checkedHeadlines++;
            Assert.True(hist.Any(h => h.Date == e.On && h.Note is not null && h.Note.EndsWith(" title")),
                        $"'{e.Text}' dated {e.On:yyyy-MM-dd} has no title bout in that fighter's ledger that day");
        }
        Assert.True(checkedHeadlines > 10, $"expected vacant-title headlines to verify, got {checkedHeadlines}");
    }

    [Fact]
    public void ReignLengthsStayRealistic()
    {
        var defences = _g.HallOfFame.Where(m => m.WasChampion).Select(m => m.Defenses).OrderBy(x => x).ToList();
        Assert.NotEmpty(defences);
        int median = defences[defences.Count / 2];
        // Guards two regressions: reigns that never end, and defences counted twice because the lineal title
        // was treated as a fourth belt rather than the same defence under another name.
        Assert.InRange(median, 1, 9);

        // Judge the DISTRIBUTION, not the single longest reign. Anything that shifts the world's random
        // trajectory — a new fighter trait, a changed player attribute — reshuffles which champions get long
        // runs, and one freak 32-defence reign among 140 champions is not a regression. Pinning the maximum
        // made this test a tripwire for any sim change at all; the 95th percentile is what actually says
        // whether reigns have stopped ending.
        int p95 = defences[(int)(defences.Count * 0.95)];
        Assert.True(p95 <= 24, $"95th percentile reign was {p95} defences");
        Assert.True(defences[^1] <= 45, $"longest reign was {defences[^1]} defences — reigns are not ending");
    }
}

/// <summary>Invariants about what the game OFFERS the player — the matchmaking ladder. These guard bugs that
/// reached the player directly: being fed an all-time great as a novice, and beating a world champion in a
/// bout that turned out to have no belt on it.</summary>
public class MatchmakingIntegrityTests
{
    /// <summary>A warmed copy of a top prospect's world. Neither of these reads the Hall of Fame — they judge
    /// the OFFERS a career is shown, which the live matchmaker makes out of the current rankings. Seeding the
    /// sport's whole past first bought them nothing and cost eighty seconds.</summary>
    private static CareerGame Career(int seed, out Boxer player)
    {
        var g = Worlds.Fresh(potential: 94, seed: seed);
        player = g.Player;
        return g;
    }

    [Fact]
    public void AReigningChampionIsNeverOfferedWithoutHisBeltOnTheLine()
    {
        // The NPC world already keeps champions off undercards. Without the same rule for the player you could
        // beat the WBA champion in a "stay-busy" bout and walk away with nothing.
        var game = Career(11, out var player);
        int offers = 0, titleBouts = 0;
        for (int i = 0; i < 60 && game.Offer is not null; i++)
        {
            var o = game.Offer!;
            offers++;
            if (o.Belt is not null) titleBouts++;
            Assert.False(game.IsWorldChampion(o.Opponent) && o.Belt is null,
                         $"offered champion {o.Opponent.Name} with no belt on the line ({o.Context})");
            game.TakeOffer();
            if (player.Retired) break;
        }
        Assert.True(offers > 25, $"expected a full career of offers, got {offers}");
        Assert.True(titleBouts > 0, "expected the player to reach title level");
    }

    [Fact]
    public void AProspectIsNotMatchedWithTheDivisionsBest()
    {
        // A wonder kid (high potential) graduates his apprenticeship early. That earns him ranked contenders,
        // NOT the division's very best — the ceiling has to ramp rather than jump straight to "anyone".
        var game = Career(11, out var player);
        int judged = 0;
        for (int i = 0; i < 24 && game.Offer is not null; i++)
        {
            var o = game.Offer!;
            int fights = player.Record.Wins + player.Record.Losses + player.Record.Draws;
            if (fights < 20 && o.Belt is null)
            {
                judged++;
                var top15 = game.RankingOf(player.WeightClass, 15).Select(b => b.Id).ToHashSet();
                Assert.DoesNotContain(o.Opponent.Id, top15);
                // The rating ceiling ramps with experience. A 14-fight novice was once handed a class-14
                // all-time great because graduating the apprenticeship lifted the cap straight to 99.
                if (fights < 15)
                    Assert.True(o.Opponent.Overall <= 80,
                                $"{fights}-fight prospect offered {o.Opponent.Name} (ovr {o.Opponent.Overall})");
            }
            game.TakeOffer();
            if (player.Retired) break;
        }
        // Every assertion above sits behind "he is still a novice with no belt on the table"; without this the
        // test passes for a career that skipped straight past the window it is about.
        Assert.True(judged > 5, $"only {judged} novice offers were judged; the ramp was barely looked at");
    }
}
