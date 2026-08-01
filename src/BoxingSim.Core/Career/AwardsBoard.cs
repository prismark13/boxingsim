using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>A bout worth remembering at the end of the year — flattened to names, numbers and standings at the
/// moment it happened, deliberately NOT to the fighters themselves. December's honours describe who a man was
/// in March: a citation that held a Boxer would read his rating, his belts and his record as they are when the
/// award is written, and quietly rewrite the year.</summary>
internal sealed record YearBout(int Year, DateOnly Date, string Winner, string Loser, int WinnerId, int LoserId,
                                string Method, int Round, bool Title, int WinnerOvr, int LoserOvr, int Kds,
                                bool Draw, bool Close, WeightClass Div, string LoserStanding,
                                double WinnerPts, double LoserPts)
{
    /// <summary>How to find this night again in either man's record.</summary>
    public BoutRef Ref => new(Winner, Loser, Date);

    /// <summary>What the ratings gave the winner before the bell, as a percentage.
    ///
    /// The same expectation the ranking itself is built on — <c>1 / (1 + 10^((them - me) / 400))</c>, the
    /// Elo term ApplyOutcome uses to decide how many points a result is worth. So the number quoted in the
    /// citation is not a separate opinion about the fight; it is the figure the world was already keeping.</summary>
    public int WinnerChancePercent =>
        (int)Math.Round(100.0 / (1.0 + Math.Pow(10, (LoserPts - WinnerPts) / 400.0)));

    /// <summary>How far below his man the winner stood, in ranking points. Negative if he was the favourite.</summary>
    public double PointsGap => LoserPts - WinnerPts;

    /// <summary>"a 12% shot", but "an 8% shot" — 8, 11 and 18 open with a vowel sound however they are spelt.</summary>
    public string ChancePhrase
    {
        get
        {
            int p = WinnerChancePercent;
            bool an = p is 8 or 11 or 18 or (>= 80 and <= 89);   // eight, eleven, eighteen, eighty-something
            return $"{(an ? "an" : "a")} {p}% shot";
        }
    }
}

/// <summary>The year's honours: the shortlist of bouts worth remembering, and every year already decided.
///
/// It scores and files them; it does not announce them. Who cares that a man won Fighter of the Year — the
/// player, or nobody — is a question about a career rather than about a trophy, so ComputeFor hands the year
/// back and the caller does the talking.</summary>
internal sealed class AwardsBoard
{
    private readonly List<AwardsYear> _awards = new();
    private readonly List<YearBout> _yearBouts = new();   // this year's honourable-mention bouts, cleared each year end

    public IReadOnlyList<AwardsYear> NewestFirst => _awards.OrderByDescending(a => a.Year).ToList();
    public IReadOnlyList<AwardsYear> All => _awards;
    public AwardsYear? For(int year) => _awards.FirstOrDefault(a => a.Year == year);

    public void Capture(YearBout bout) => _yearBouts.Add(bout);

    /// <summary>Forget the shortlist without deciding it — the warm-up years are not the player's story.</summary>
    public void ClearShortlist() => _yearBouts.Clear();
    public void ClearDecided() => _awards.Clear();
    public void Load(AwardsYear year) => _awards.Add(year);

    private sealed class FoyAcc { public string Name = ""; public WeightClass Div; public double Score; public int Wins, Losses, Titles, Kos; public double BestScore = -1; public YearBout? Best; }

    /// <summary>Expand a method abbreviation into words for award commentary.</summary>
    private static string Long(string method) => method switch
    {
        "KO" => "knockout", "TKO" => "stoppage", "UD" => "a unanimous decision", "SD" => "a split decision",
        "MD" => "a majority decision", "DQ" => "disqualification", "D" => "a draw", _ => method
    };

    /// <summary>Hand out the end-of-year honours (top three per category) from the year's captured bouts, and
    /// return them — null if the year had nothing worth honouring, which is a real answer in a young world.</summary>
    public AwardsYear? ComputeFor(int year)
    {
        var bouts = _yearBouts.Where(x => x.Year == year).ToList();
        _yearBouts.RemoveAll(x => x.Year <= year);
        if (bouts.Count == 0) return null;

        // Fighter of the Year rewards the QUALITY of results — beating high-rated men, winning titles, pulling
        // upsets — not volume, and a loss that year is a heavy negative (a Fighter of the Year rarely loses).
        var acc = new Dictionary<int, FoyAcc>();
        FoyAcc Get(int id, string name, WeightClass div)
        {
            if (!acc.TryGetValue(id, out var a)) acc[id] = a = new FoyAcc { Name = name, Div = div };
            return a;
        }
        foreach (var x in bouts)
        {
            if (x.Draw) continue;
            bool inside = x.Method is "KO" or "TKO";
            var w = Get(x.WinnerId, x.Winner, x.Div);
            w.Score += 6 + x.LoserOvr * 0.4 + (x.Title ? 45 : 0) + Math.Max(0, x.LoserOvr - x.WinnerOvr) * 0.9 + (inside ? 5 : 0);
            w.Wins++; if (x.Title) w.Titles++; if (inside) w.Kos++;
            double q = x.LoserOvr + (x.Title ? 25 : 0);
            if (q > w.BestScore) { w.BestScore = q; w.Best = x; }
            var l = Get(x.LoserId, x.Loser, x.Div);
            l.Score -= 32 + Math.Max(0, x.WinnerOvr - x.LoserOvr) * 0.7 + (x.Title ? 8 : 0);   // a defeat sinks his case
            l.Losses++;
        }
        var foy = acc.Values.Where(a => a.Wins > a.Losses && a.Best is not null)   // must have had a winning year
            .OrderByDescending(a => a.Score).Take(3)
            .Select(a => new AwardWinner { Name = a.Name, Div = a.Div, Bout = a.Best!.Ref,
                Detail = $"{a.Wins}-{a.Losses}{(a.Titles > 0 ? $", {a.Titles} title win{(a.Titles == 1 ? "" : "s")}" : "")}",
                Commentary = $"A standout {year} in {a.Div.DisplayName()} — {a.Wins}-{a.Losses} with {a.Kos} inside the distance{(a.Titles > 0 ? $", including {a.Titles} world-title win{(a.Titles == 1 ? "" : "s")}" : "")}. His best: beating {a.Best!.Loser}{(string.IsNullOrEmpty(a.Best.LoserStanding) ? $" (rated {a.Best.LoserOvr})" : $", {a.Best.LoserStanding},")}{(a.Best.Title ? " for the belt" : "")}." }).ToList();

        // An upset is measured on RANKING POINTS, not on the rating.
        //
        // Overall is what a man can do; ranking points are what the sport had come to expect of him, which is
        // the thing an upset actually confounds. They are the same number the matchmaker, the boards and the
        // Elo update all work in, so "nobody saw it coming" is now the world's own estimate rather than a
        // second opinion pulled from a different scale. It also fixes a real mismatch: a 78-rated contender on
        // a fourteen-fight win run beating an 80-rated man who had lost three was being called the year's
        // upset, when the ratings gap was two points and the standing said he was the favourite.
        //
        // The title bonus is 60 rather than 15 because points run on a far wider scale than Overall — 60 is
        // about the edge two evenly matched contenders open on each other over a good year, so a belt changing
        // hands outweighs a slightly wider gap in a bout that decided nothing, and nothing more than that.
        var upset = bouts.Where(x => !x.Draw && x.PointsGap > 0)
            .OrderByDescending(x => x.PointsGap + (x.Title ? 60 : 0)).Take(3)
            .Select(x => new AwardWinner { Name = x.Winner, Div = x.Div, Bout = x.Ref,
                Detail = $"beat {x.Loser} · {x.ChancePhrase}{(x.Title ? " · title" : "")}",
                Commentary = $"Nobody saw it coming: the ratings gave {x.Winner} {x.ChancePhrase.Replace(" shot", "")} against {x.Loser}{(string.IsNullOrEmpty(x.LoserStanding) ? "" : $", {x.LoserStanding},")} and he won by {Long(x.Method)}{(x.Title ? " to rip away the world title" : "")} in {x.Div.DisplayName()}." }).ToList();

        var ko = bouts.Where(x => x.Method is "KO" or "TKO")
            .OrderByDescending(x => x.LoserOvr + (x.Title ? 12 : 0) + Math.Max(0, 9 - x.Round) * 2 + x.Kds * 3).Take(3)
            .Select(x => new AwardWinner { Name = x.Winner, Div = x.Div, Bout = x.Ref,
                Detail = $"KO{(x.Round > 0 ? $" rd{x.Round}" : "")} {x.Loser}{(x.Title ? " · title" : "")}",
                Commentary = $"{x.Winner} flattened {x.Loser}{(string.IsNullOrEmpty(x.LoserStanding) ? "" : $", {x.LoserStanding},")}{(x.Round > 0 ? $" in round {x.Round}" : "")}{(x.Title ? " in a world-title fight" : "")} — the year's most emphatic knockout in {x.Div.DisplayName()}." }).ToList();

        var foty = bouts.OrderByDescending(x => Math.Min(x.WinnerOvr, x.LoserOvr) + (x.Title ? 15 : 0) + (x.Close ? 12 : 0) + x.Kds * 4).Take(3)
            .Select(x => new AwardWinner { Name = $"{x.Winner} vs {x.Loser}", Div = x.Div, Bout = x.Ref,
                Detail = $"{(x.Draw ? "draw" : x.Method)}{(x.Title ? " · title" : "")}{(x.Kds > 0 ? $" · {x.Kds} KD" : "")}",
                Commentary = $"{x.Winner} and {x.Loser} went to war in {x.Div.DisplayName()}{(x.Title ? " with the world title on the line" : "")}{(x.Kds > 0 ? $", trading {x.Kds} knockdown{(x.Kds == 1 ? "" : "s")}" : "")} — settled by {(x.Draw ? "a draw" : Long(x.Method))}." }).ToList();

        var decided = new AwardsYear { Year = year, FighterOfYear = foy, UpsetOfYear = upset, KnockoutOfYear = ko, FightOfYear = foty };
        _awards.Add(decided);
        return decided;
    }
}
