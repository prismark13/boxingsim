using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The small shared questions the rest of the class keeps asking.</summary>
public sealed partial class CareerGame
{
    // ---- helpers ----

    private void AddActive(Boxer b) => _roster.Add(b);

    /// <summary>No starter is rated above 75 — a green fighter, however talented, hasn't proven it yet.
    /// His ratings scale down proportionally (keeping his profile) and the cap lifts once he's seasoned.</summary>
    private static void CapStarter(Boxer b)
    {
        if (CareerStages.Of(b) != CareerStage.Starter) return;
        var r = b.Ratings;
        int guard = 0;
        while (b.Overall > 75 && guard++ < 12)
        {
            r.Power = Dn(r.Power); r.Chin = Dn(r.Chin); r.Speed = Dn(r.Speed); r.Defense = Dn(r.Defense);
            r.Stamina = Dn(r.Stamina); r.Accuracy = Dn(r.Accuracy); r.Conditioning = Dn(r.Conditioning);
            r.Aggression = Dn(r.Aggression); r.Heart = Dn(r.Heart); r.CutResistance = Dn(r.CutResistance);
        }
        static int Dn(int v) => Ratings.Clamp((int)Math.Round(v * 0.96));
    }

    /// <summary>Put a bout in a fighter's ledger, on the night it happened.
    ///
    /// The night is an argument. It used to be read off the world clock, which meant staging a fight was done
    /// by ASSIGNING to that clock first and hoping nothing else looked at it in between — see the note on
    /// <see cref="ApplyOutcome"/>.</summary>
    private void Record(Boxer f, string opp, char result, string method, int round, int kdFor, int kdAgainst, string? note, string? cards, IReadOnlyList<BoutRound>? rounds, IReadOnlyList<string>? commentary, WeightClass at, DateOnly on)
    {
        f.History.Add(new BoutLine { Date = on, Opponent = opp, Result = result, Method = method, Round = round, KdFor = kdFor, KdAgainst = kdAgainst, Note = note, Cards = cards, Rounds = rounds, Commentary = commentary, Division = at });
        if (f.History.Count > 60) f.History.RemoveAt(0);   // keep the ledger bounded
    }

    /// <summary>Pull the key play-by-play moments from a full-engine bout (knockdowns, cuts, big shots,
    /// the finish, and a one-line recap per round) so a stored fight can be read back later. Null when the
    /// fight was resolved by the fast NPC model (no tick detail).</summary>
    private static List<string>? ExtractHighlights(FightResult res)
    {
        if (res.Rounds.Count == 0 || res.Rounds[0].Ticks.Count == 0) return null;
        string A = res.A.Name, B = res.B.Name;
        var lines = new List<string>();
        foreach (var rd in res.Rounds)
        {
            int kdA = 0, kdB = 0; bool cutA = false, cutB = false, hurt = false;
            foreach (var t in rd.Ticks)
            {
                if (t.KnockdownsA > kdA) { kdA = t.KnockdownsA; lines.Add($"R{rd.Round} — {A} is DOWN{(t.DownBodyA ? " from a body shot" : "")}!"); }
                if (t.KnockdownsB > kdB) { kdB = t.KnockdownsB; lines.Add($"R{rd.Round} — {B} is DOWN{(t.DownBodyB ? " from a body shot" : "")}!"); }
                if (!cutA && t.CutA >= 0.4) { cutA = true; lines.Add($"R{rd.Round} — {A} is cut."); }
                if (!cutB && t.CutB >= 0.4) { cutB = true; lines.Add($"R{rd.Round} — {B} is cut."); }
                if (!hurt && t.RockB >= 2) { hurt = true; lines.Add($"R{rd.Round} — {A} has {B} badly hurt!"); }
                else if (!hurt && t.RockA >= 2) { hurt = true; lines.Add($"R{rd.Round} — {B} has {A} badly hurt!"); }
                if (t.Fin is StopInfo fin)
                {
                    string w = fin.Winner == 0 ? A : B, l = fin.Winner == 0 ? B : A;
                    lines.Add($"R{rd.Round} — {(fin.Method == "KO" ? $"{w} KNOCKS OUT {l}!" : fin.Method == "DQ" ? $"{l} is disqualified." : $"{w} STOPS {l}!")}");
                }
            }
            // A short recap of who edged the round.
            string recap = rd.LandedA > rd.LandedB + 2 ? $"{A}'s round" : rd.LandedB > rd.LandedA + 2 ? $"{B}'s round" : "even round";
            lines.Add($"R{rd.Round} · {recap} ({rd.LandedA}-{rd.LandedB} landed)");
        }
        return lines.Count > 0 ? lines : null;
    }

    /// <summary>A fast, statistical resolution of an NPC-vs-NPC bout (no round-by-round sim) so a big
    /// division can be simulated cheaply. The player's own fights always use the full engine.</summary>
    private FightResult FastBout(Boxer a, Boxer b, int rounds)
    {
        // Effective rating favours a young fighter still climbing toward a high ceiling, so a genuine
        // prospect very rarely loses to a journeyman who's already maxed out.
        double gap = (a.Overall + YouthEdge(a)) - (b.Overall + YouthEdge(b));
        double pa = 1.0 / (1.0 + Math.Pow(10, -gap / 8.0));   // a's win probability by effective rating
        // Any given night: between two genuine world-class men, even a dominant champion can be caught — so the
        // underdog always keeps a real puncher's chance. This caps ultra-long unbeaten reigns without letting a
        // prospect get upset by a journeyman (the floor only applies when BOTH are top-tier).
        if (a.Overall >= 66 && b.Overall >= 66) pa = Math.Clamp(pa, 0.07, 0.93);
        double drawP = 0.05 * (1 - Math.Abs(pa - 0.5) * 2);
        Boxer? winner = null, loser = null;
        FightOutcome outcome = FightOutcome.Draw;
        string method = "D";
        int endRound = rounds, kdA = 0, kdB = 0;
        bool draw = _rng.NextDouble() < drawP;
        bool aWins = false, ko = false, stopNoKd = false;   // stopNoKd: a cut/DQ stoppage — trims the card, no knockdown

        if (!draw)
        {
            aWins = _rng.NextDouble() < pa;                       // provisional result, by rating
            var pw = aWins ? a : b; var pl = aWins ? b : a;
            double koP = Ratings.KnockoutChance(pw.Ratings.Power, pl.Ratings.Chin, pw.Overall - pl.Overall);
            if (_rng.NextDouble() < koP)
            {
                ko = true; outcome = FightOutcome.Knockout; method = "KO"; endRound = 1 + _rng.Next(rounds);
                if (aWins) kdB = 1; else kdA = 1;
            }
            else
            {
                // Either man can be cut and pulled out — far more often if he's a bleeder — and the cut man LOSES
                // even if he was ahead (a bleeder's curse). If both cut, the more cut-prone one goes.
                double CutRisk(Boxer f) => 0.005 + Math.Max(0, 80 - f.Ratings.CutResistance) / 100.0 * 0.12;
                bool aCut = _rng.NextDouble() < CutRisk(a);
                bool bCut = _rng.NextDouble() < CutRisk(b);
                if (aCut || bCut)
                {
                    bool aStopped = aCut && (!bCut || a.Ratings.CutResistance <= b.Ratings.CutResistance);
                    aWins = !aStopped;
                    outcome = FightOutcome.TechnicalKnockout; method = "TKO"; stopNoKd = true;
                    endRound = Math.Max(3, rounds - _rng.Next(4));
                }
                // A rare disqualification (~1 in 500 fights) — not a knockout; the fouler is thrown out.
                else if (_rng.NextDouble() < 0.0042)
                {
                    outcome = FightOutcome.Decision; method = "DQ"; stopNoKd = true;
                    endRound = 2 + _rng.Next(Math.Max(1, rounds - 2));
                }
                else outcome = FightOutcome.Decision;
            }
            winner = aWins ? a : b; loser = aWins ? b : a;
        }

        // Sketch a believable card: the winner takes most rounds, punch output tracks ability, and a
        // stoppage is a 10-8 final round. Cheap enough to run for the whole division, so even quick
        // NPC undercards are inspectable round by round.
        int lastRound = (ko || stopNoKd) ? endRound : rounds;
        var rr = new List<RoundResult>(lastRound);
        for (int r = 1; r <= lastRound; r++)
        {
            int landA = FastLanded(a, b), landB = FastLanded(b, a);
            int sA = 10, sB = 10, kA = 0, kB = 0;
            if (ko && r == lastRound)
            {
                if (aWins) { sB = 8; kB = 1; landA += 3; } else { sA = 8; kA = 1; landB += 3; }
            }
            else
            {
                bool aRound = draw ? _rng.NextDouble() < 0.5 : _rng.NextDouble() < (aWins ? 0.66 : 0.34);
                if (aRound) { sB = 9; landA += 2; } else { sA = 9; landB += 2; }
            }
            rr.Add(new RoundResult { Round = r, LandedA = landA, LandedB = landB, ScoreA = sA, ScoreB = sB, KnockdownsA = kA, KnockdownsB = kB });
        }

        IReadOnlyList<(int A, int B)> cards = Array.Empty<(int, int)>();
        if (!ko && !stopNoKd)   // went to the cards (a decision or a draw); a cut/DQ stoppage has none
        {
            method = draw ? "D" : (Math.Abs(pa - 0.5) > 0.14 ? "UD" : _rng.NextDouble() < 0.5 ? "SD" : "MD");
            cards = BuildCards(aWins, rounds, method, draw);
        }

        return new FightResult
        {
            A = a, B = b, Winner = winner, Loser = loser, Outcome = outcome,
            ScheduledRounds = rounds, EndRound = endRound, KnockdownsA = kdA, KnockdownsB = kdB,
            Rounds = rr, Scorecards = cards, Method = method
        };
    }

    /// <summary>Clean punches a fighter lands in a round: his output (accuracy + work-rate) blunted by
    /// the other man's defence, so an out-boxer smothers a slugger's numbers and vice-versa.</summary>
    private int FastLanded(Boxer x, Boxer opp)
    {
        double output = 4 + x.Ratings.Accuracy * 0.08 + x.Ratings.Aggression * 0.05;
        double defended = opp.Ratings.Defense * 0.06;
        return Math.Clamp((int)Math.Round(output - defended + _rng.Next(-2, 3)), 1, 22);
    }

    /// <summary>Three judges' final tallies, oriented to the winner and consistent with the verdict.</summary>
    private (int A, int B)[] BuildCards(bool aWins, int rounds, string method, bool draw)
    {
        int bt = rounds * 10;
        int[] margins = draw ? new[] { 0, 0, 0 }
            : method switch
            {
                "UD" => new[] { _rng.Next(3, 8), _rng.Next(2, 6), _rng.Next(1, 5) },   // all three for the winner
                "MD" => new[] { _rng.Next(2, 6), _rng.Next(1, 4), 0 },                 // two clear, one even
                "SD" => new[] { _rng.Next(2, 5), _rng.Next(1, 4), -1 },                // two for, one against by a hair
                _    => new[] { 0, 0, 0 }
            };
        var res = new (int A, int B)[3];
        for (int j = 0; j < 3; j++)
        {
            int w = bt - _rng.Next(0, 4);        // a couple of dropped/shared rounds
            int l = w - margins[j];
            res[j] = aWins ? (w, l) : (l, w);
        }
        return res;
    }
    private void ReRank() { /* RankPoints are the ranking; nothing to precompute */ }

    /// <summary>Turn a notable NPC undercard result into a news headline — upsets, KO streaks, big stoppages.</summary>
    /// <summary>Where a man stands in his division on the board the player reads, or 0 if he is not on it.
    /// Champions come first, so "C" is 1 and the leading contender is 2 — which is why the caller says
    /// "champion" rather than "#1" when it lands on a title holder.</summary>
    private int BoardPlace(Boxer b)
    {
        var board = RankingBoard(b.WeightClass, 15);
        for (int i = 0; i < board.Count; i++) if (board[i].Id == b.Id) return i + 1;
        return 0;
    }

    /// <summary>Who these two men are, in one quiet line under the headline.
    ///
    /// "Paul Fujii halts Jorgen Hansen in 8" tells you nothing about either of them: whether Fujii is a
    /// prospect or a gatekeeper, whether Hansen was ranked, whether this mattered. Composed here rather than in
    /// the view because a record read later is the record he has NOW, not the one he carried into the ring.</summary>
    private string BoutDetail(Boxer w, Boxer l)
    {
        string Where(Boxer b)
        {
            if (IsWorldChampion(b)) return "champion";
            int p = BoardPlace(b);
            return p > 0 ? $"#{p}" : "unranked";
        }
        return $"{w.Name} {w.Record}, {Where(w)}  ·  {l.Name} {l.Record}, {Where(l)}";
    }

    /// <summary>Put a result in the news, on the night it happened. The night is passed in: a bout resolved
    /// during a season is dated across the year, and is not necessarily the day the world is standing on.</summary>
    private void ReportBout(FightResult res, DateOnly on)
    {
        if (res.IsDraw || res.Winner is null || res.Loser is null) return;
        var w = res.Winner; var l = res.Loser;
        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        string who = BoutDetail(w, l);

        var div = w.WeightClass;   // tag every headline with the division so the news feed filters by weight
        if (WorldRanked(l) && l.Overall - w.Overall >= 8 && _rng.NextDouble() < 0.7)
        {
            LogEvent(Pick($"UPSET! {w.Name} shocks {l.Name}{(ko ? $", stopped in {res.EndRound}" : "")}.",
                          $"Against the odds — {w.Name} outpoints the fancied {l.Name}.",
                          $"{l.Name} is stunned by {w.Name} in a major upset."), kind: "upset", div: div,
                     bout: new BoutRef(w.Name, l.Name, on), detail: who, on: on);
            return;
        }
        // A long unbeaten run is news in itself — reported once it hits 10, then every 5 (15, 20, …).
        int wins = WinStreak(w);
        if (wins >= 10 && wins % 5 == 0 && _rng.NextDouble() < 0.7)
        {
            LogEvent(Pick($"{w.Name} extends his unbeaten run to {wins} straight.",
                          $"Still perfect — {w.Name} makes it {wins} wins in a row and is knocking on the door.",
                          $"{w.Name} runs his streak to {wins} in a row, forcing his way into the picture."), kind: "streak", div: div,
                     bout: new BoutRef(w.Name, l.Name, on), detail: who, on: on);
            return;
        }
        if (ko)
        {
            int streak = KoStreak(w);
            if (streak >= 10 && streak % 5 == 0 && _rng.NextDouble() < 0.7)
            {
                LogEvent(Pick($"{w.Name} rolls on — {streak} straight inside the distance.",
                              $"{w.Name} keeps the KO streak going, now {streak} in a row.",
                              $"Frightening — {w.Name} up to {streak} consecutive knockouts."), kind: "streak", div: div,
                         bout: new BoutRef(w.Name, l.Name, on), detail: who, on: on);
                return;
            }
            if (w.Overall >= 76 && WorldRanked(l) && _rng.NextDouble() < 0.4)
                LogEvent(Pick($"{w.Name} halts {l.Name} in {res.EndRound}.",
                              $"{w.Name} takes out {l.Name} inside the distance."), kind: "ko", div: div,
                         bout: new BoutRef(w.Name, l.Name, on), detail: who, on: on);
            return;
        }
    }

    /// <summary>Count of a fighter's most recent bouts that were consecutive knockout wins.</summary>
    private static int KoStreak(Boxer b)
    {
        int n = 0;
        for (int i = b.History.Count - 1; i >= 0; i--)
        {
            var h = b.History[i];
            if (h.Result == 'W' && (h.Method == "KO" || h.Method == "TKO")) n++;
            else break;
        }
        return n;
    }

    /// <summary>Count of a fighter's most recent consecutive wins (any method).</summary>
    private static int WinStreak(Boxer b)
    {
        int n = 0;
        for (int i = b.History.Count - 1; i >= 0 && b.History[i].Result == 'W'; i--) n++;
        return n;
    }

    private string Pick(params string[] opts) => opts[_rng.Next(opts.Length)];
    /// <summary>File a headline. <paramref name="on"/> is the day it happened; left out, it is today — which
    /// is right for everything the world does in the present, and wrong for a bout being staged onto a night
    /// of its own. Those pass it.</summary>
    private void LogEvent(string text, bool playerBout = false, string? kind = null, WeightClass? div = null,
                          BoutRef? bout = null, string? detail = null, DateOnly? on = null)
    {
        // Say why he should care. A result is a scoreboard until it touches him.
        if (!playerBout && bout is BoutRef br && PlayerAngle(br, div) is string angle)
            text = $"{text} — {angle}";
        _log.Add(new CareerEvent { On = on ?? Date, Text = text, PlayerBout = playerBout, Kind = kind,
                                   Div = div ?? Division, Bout = bout, Detail = detail });
        _logWrites++;
        if (_log.Count > 1500) _log.RemoveAt(0);   // bounded; eight divisions produce more news
    }

    private static DateOnly ParseDate(string s, DateOnly fallback) => DateOnly.TryParse(s, out var d) ? d : fallback;

    private static string LayoffText(int days) => days >= 60 ? $"out ~{Math.Max(2, days / 30)} months" : $"out ~{Math.Max(1, days / 7)} weeks";

    /// <summary>How long until the next fight, from a man's MILEAGE.
    ///
    /// This used to be picked off his career stage, which meant the gap jumped the moment he crossed a
    /// boundary — a fighter was out every nine weeks and then, on his ninth bout, suddenly every eleven. Wear
    /// does not arrive in steps. The wait now lengthens smoothly with every fight he has had: a novice boxes
    /// every couple of months, a man with sixty bouts on him needs a real camp and a real rest between them.
    ///
    /// Nobody's schedule is metronomic either. Opponents pull out, purse bids drag, a cut needs six weeks, and
    /// sometimes a fight comes up at three weeks' notice — so the actual wait is half to one and a half times
    /// the typical, floored at three weeks because nobody boxes again inside that.</summary>
    private int DaysForFights(int fights)
    {
        // Shaped to real activity rather than a straight line: a young fighter boxes five or six times a year,
        // that falls to three or four once he is twenty fights in and matched properly, and it keeps easing off
        // as the mileage tells. The curve is steepest through the twenties, which is where a career actually
        // changes from a club schedule to a campaign.
        double typical =
            fights <= 10 ? 66                                    // 5–6 a year
            : fights <= 20 ? 66 + (fights - 10) * 3.0            // ramping toward a real camp
            : fights <= 40 ? 96 + (fights - 20) * 0.7            // 3–4 a year
            : 110 + Math.Min(fights - 40, 50) * 0.9;             // winding down
        double spread = 0.5 + _rng.NextDouble();
        return Math.Max(21, (int)Math.Round(typical * spread));
    }

    /// <summary>The ceiling a generated fighter may not pass: class 5, the top of the gatekeeper band on the
    /// 1-15 scale. An invented fighter can be a real night's work and can hold a regional belt; he does not
    /// become a world champion, and he never becomes a great. Those tiers are the real roster's and the
    /// player's.</summary>
    private const int GeneratedCap = 75;

    /// <summary>A fighter is only "world-ranked" once he's built a real body of work — 20 pro bouts.</summary>
    public static bool WorldRanked(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws >= 20;

    /// <summary>A ranked contender: a real body of work AND a genuinely winning record (65%+). A man hovering
    /// around .500 is a gatekeeper however long he's been around — he belongs on the undercard, not the top 15.</summary>
    public static bool RankedContender(Boxer b) =>
        WorldRanked(b) && b.Record.Wins * 100 >= (b.Record.Wins + b.Record.Losses) * 65;

    /// <summary>How many pro bouts a fighter must build before he'll be matched with ranked contenders. Most
    /// serve the full apprenticeship (20); a rare phenom (a "wonder kid" — a very high ceiling) is fast-tracked
    /// into contention far sooner, the way the odd real great wins a belt inside 12–15 fights. Roughly the top
    /// ~1% (Potential ≥ 93) go at 12, the top ~5% (≥ 87) at 15.</summary>
    public static int ContenderApprenticeship(Boxer b) => b.Potential >= 92 ? 12 : b.Potential >= 87 ? 15 : 20;

    /// <summary>True once a fighter has served his apprenticeship and is ready to share the ring with contenders.</summary>
    public static bool ReadyForContenders(Boxer b) => (b.Record.Wins + b.Record.Losses + b.Record.Draws) >= ContenderApprenticeship(b);

    /// <summary>The current top-20 of a division (world-ranked fighters by ranking score).</summary>
    private HashSet<int> Top20Ids(WeightClass wc) => ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(20).Select(b => b.Id).ToHashSet();
    private HashSet<int> Top8Ids(WeightClass wc) => ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(8).Select(b => b.Id).ToHashSet();

    /// <summary>Ranking score: ability-anchored Elo, nudged by the fighter's win/loss margin so a padded Elo can't
    /// keep a losing record near the top. The margin is deliberately a light touch — weighted any heavier it just
    /// rewards volume, and a 60-fight journeyman outranks an unbeaten champion.</summary>
    public static double RankScore(Boxer b) => b.RankPoints + (b.Record.Wins - b.Record.Losses) * 2.5;

    private static int Scale(int v, double dev) => Ratings.Clamp((int)Math.Round(v * dev));
    private static double Lerp(double dev, double floor) => floor + (1.0 - floor) * dev;

    private static int PeakOf(Boxer proto, int birth)
    {
        if (proto.PrimeYears is string py && birth > 0)
        {
            int y0 = FirstYear(py);
            if (y0 > 0) return Math.Clamp((y0 + 2) - birth, 25, 34);
        }
        return 28;
    }

    private static int FirstYear(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        for (int i = 0; i + 4 <= s.Length; i++)
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                return int.Parse(s.Substring(i, 4));
        return 0;
    }

    /// <summary>Build a fresh player fighter: a green starter with the ceiling still ahead of them.</summary>
    public static Boxer CreatePlayer(Random rng, string name, string country, WeightClass wc, int potential, int startAge = 18)
    {
        int peak = 28;
        double dev = BoxerFactory.Development(startAge, peak);
        // Built from the same shape as every other fighter in the world, with the player's own numbers.
        // These were two separate copies of the same eleven lines until a change to one missed the other
        // twice — see FighterShape.
        var r = FighterShape.Compose(rng, potential, dev, young: true,
                                     FighterShape.PlayerSpreads, FighterShape.PlayerFloors);

        var player = new Boxer
        {
            Id = -1, Name = name, Country = country, WeightClass = wc,
            Ratings = r, Age = startAge, PeakAge = peak, Potential = potential, RankPoints = 1000
        };
        // Every other fighter gets a reach; the player was left on zero, and ReachEdge treats zero as "unknown"
        // and returns no effect — so the range battle simply never applied to the player's fights, in either
        // direction. Give him a frame like anyone else's.
        player.Reach = Physique.ReachInchesFor(wc, name);
        return player;
    }
}
