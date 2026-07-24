using BoxingSim.Core.Analysis;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>Statistical validation of the fight engine: does a better fighter win as expected,
/// are finish rates realistic, do mismatches and mirror matches behave?</summary>
public static class SimTest
{
    public static void Run(IReadOnlyList<Boxer> deck, int seed)
    {
        var rng = new Random(seed);
        var engine = new FightEngine(rng);
        int n = deck.Count;
        Console.WriteLine($"=== ENGINE VALIDATION — {n} fighters, 12-round bouts ===\n");

        // ---- A) random matchups: finish rates + win-by-gap ----
        const int trials = 40000;
        int ko = 0, dec = 0, draw = 0, totalKd = 0, stoppageRounds = 0, stoppages = 0;
        // gap buckets: favourite (higher OVR) win share, draws count as 0.5
        var buckets = new (int lo, int hi, string label)[] {
            (0,1,"0-1"),(2,3,"2-3"),(4,5,"4-5"),(6,8,"6-8"),(9,12,"9-12"),(13,99,"13+")
        };
        var bWins = new double[buckets.Length];
        var bN = new int[buckets.Length];
        int evenKo = 0, evenDec = 0, evenDraw = 0, evenN = 0;

        for (int i = 0; i < trials; i++)
        {
            var a = deck[rng.Next(n)];
            Boxer b; do { b = deck[rng.Next(n)]; } while (b.Id == a.Id);
            var res = engine.Simulate(a, b, 12);

            bool finish = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
            if (finish) { ko++; stoppages++; stoppageRounds += res.EndRound; }
            else if (res.IsDraw) draw++;
            else dec++;
            totalKd += res.KnockdownsA + res.KnockdownsB;

            int gap = Math.Abs(a.Overall - b.Overall);
            var fav = a.Overall >= b.Overall ? a : b;
            double favScore = res.IsDraw ? 0.5 : (res.Winner!.Id == fav.Id ? 1.0 : 0.0);
            for (int k = 0; k < buckets.Length; k++)
                if (gap >= buckets[k].lo && gap <= buckets[k].hi) { bWins[k] += favScore; bN[k]++; break; }

            if (gap <= 2) { evenN++; if (finish) evenKo++; else if (res.IsDraw) evenDraw++; else evenDec++; }
        }

        Console.WriteLine($"Random matchups ({trials} fights):");
        Console.WriteLine($"  KO/TKO {Pct(ko, trials)}   Decision {Pct(dec, trials)}   Draw {Pct(draw, trials)}");
        Console.WriteLine($"  avg knockdowns/fight: {(double)totalKd / trials:0.00}   avg stoppage round: {(stoppages > 0 ? (double)stoppageRounds / stoppages : 0):0.0}\n");

        Console.WriteLine($"Even matchups (|OVR gap| <= 2, {evenN} fights):");
        Console.WriteLine($"  KO/TKO {Pct(evenKo, evenN)}   Decision {Pct(evenDec, evenN)}   Draw {Pct(evenDraw, evenN)}\n");

        Console.WriteLine("Favourite (higher OVR) win-rate by gap  — expect this to climb steadily:");
        for (int k = 0; k < buckets.Length; k++)
            if (bN[k] > 0)
                Console.WriteLine($"  gap {buckets[k].label,-5}: {100.0 * bWins[k] / bN[k],5:0.0}%   ({bN[k]} fights)");
        Console.WriteLine();

        // ---- B) extreme mismatch ----
        var top = deck.OrderByDescending(x => x.Overall).First();
        var bot = deck.OrderBy(x => x.Overall).First();
        int topWins = 0, topKo = 0, mm = 600;
        for (int i = 0; i < mm; i++)
        {
            var res = engine.Simulate(top, bot, 12);
            if (res.Winner?.Id == top.Id) topWins++;
            if (res.Winner?.Id == top.Id && res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout) topKo++;
        }
        Console.WriteLine($"Extreme mismatch: {top.Name} ({top.Overall}) vs {bot.Name} ({bot.Overall}), {mm} fights:");
        Console.WriteLine($"  favourite wins {Pct(topWins, mm)}   (of those, {Pct(topKo, topWins)} by stoppage)\n");

        // ---- C) mirror match (identical ratings, distinct fighters) ----
        var mid = deck.OrderBy(x => Math.Abs(x.Overall - 80)).First();
        var twin = new Boxer { Id = -777, Name = mid.Name + " (twin)", WeightClass = mid.WeightClass, Ratings = mid.Ratings.Clone(), Age = mid.Age };
        int aWins = 0, bWinsM = 0, drawM = 0, mr = 2000;
        for (int i = 0; i < mr; i++)
        {
            var res = engine.Simulate(mid, twin, 12);
            if (res.IsDraw) drawM++;
            else if (res.Winner!.Id == mid.Id) aWins++; else bWinsM++;
        }
        Console.WriteLine($"Mirror match: {mid.Name} vs himself, {mr} fights (expect ~50/50):");
        Console.WriteLine($"  red corner {Pct(aWins, mr)}   blue corner {Pct(bWinsM, mr)}   draw {Pct(drawM, mr)}\n");

        // ---- verdict ----
        Console.WriteLine("Checks:");
        Check("Even-match finish rate in plausible HW band (35-65%)", evenKo, evenN, 0.35, 0.65);
        double topRate = (double)topWins / mm;
        Console.WriteLine($"  [{(topRate >= 0.95 ? "PASS" : "FAIL")}] Elite beats lowest-rated >= 95%  (got {topRate * 100:0.0}%)");
        double mirrorRed = (double)aWins / mr;
        Console.WriteLine($"  [{(mirrorRed is >= 0.45 and <= 0.55 ? "PASS" : "FAIL")}] Mirror match within 45-55%  (got {mirrorRed * 100:0.0}%)");
        bool monotone = true;
        double prev = -1;
        for (int k = 0; k < buckets.Length; k++) if (bN[k] > 50) { double v = bWins[k] / bN[k]; if (v + 0.02 < prev) monotone = false; prev = v; }
        Console.WriteLine($"  [{(monotone ? "PASS" : "FAIL")}] Favourite win-rate rises monotonically with gap");

        StyleCheck(seed);
    }

    /// <summary>Round-robin of equal-tier archetypes — does each style win at the rate its OVR implies?</summary>
    public static void StyleCheck(int seed)
    {
        var engine = new FightEngine(new Random(seed));
        (string Name, Ratings R)[] arch =
        {
            // Tuned so every archetype sits at roughly the same OVR (~84) — isolates engine bias from the rating formula.
            ("Slugger",      new Ratings{Power=97,Chin=92,Speed=70,Defense=62,Stamina=80,Accuracy=76,Conditioning=78,CutResistance=80,Aggression=92,Heart=92}),
            ("Out-Boxer",    new Ratings{Power=70,Chin=80,Speed=90,Defense=92,Stamina=84,Accuracy=90,Conditioning=82,CutResistance=82,Aggression=58,Heart=80}),
            ("Counter",      new Ratings{Power=78,Chin=82,Speed=86,Defense=92,Stamina=82,Accuracy=90,Conditioning=82,CutResistance=82,Aggression=55,Heart=82}),
            ("Boxer-Punch",  new Ratings{Power=84,Chin=84,Speed=82,Defense=82,Stamina=82,Accuracy=84,Conditioning=82,CutResistance=82,Aggression=72,Heart=84}),
            ("Swarmer",      new Ratings{Power=82,Chin=86,Speed=80,Defense=72,Stamina=90,Accuracy=80,Conditioning=88,CutResistance=80,Aggression=92,Heart=90}),
        };
        var b = arch.Select((a, i) => new Boxer { Id = 1000 + i, Name = a.Name, WeightClass = WeightClass.Heavyweight, Ratings = a.R, Age = 28 }).ToList();
        int n = b.Count, N = 1500;
        var wins = new int[n]; var fights = new int[n]; var kos = new int[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                for (int f = 0; f < N; f++)
                {
                    var res = engine.Simulate(b[i], b[j], 12);
                    fights[i]++;
                    if (res.Winner?.Id == b[i].Id) { wins[i]++; if (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout) kos[i]++; }
                }
            }
        Console.WriteLine("\n=== STYLE CHECK (equal-tier archetypes, round-robin) ===");
        for (int i = 0; i < n; i++)
            Console.WriteLine($"  {b[i].Name,-12} OVR {b[i].Overall,2}   win {100.0 * wins[i] / fights[i],5:0.0}%   KO-of-wins {(wins[i] > 0 ? 100.0 * kos[i] / wins[i] : 0),5:0.0}%   [{StyleClassifier.Of(b[i]).DisplayName()}]");
    }

    private static void Check(string label, int part, int whole, double lo, double hi)
    {
        double v = whole > 0 ? (double)part / whole : 0;
        Console.WriteLine($"  [{(v >= lo && v <= hi ? "PASS" : "FAIL")}] {label}  (got {v * 100:0.0}%)");
    }

    private static string Pct(int x, int n) => n > 0 ? $"{100.0 * x / n,5:0.0}%" : "  n/a";
}
