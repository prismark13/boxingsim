using BoxingSim.Core.Analysis;
using BoxingSim.Core.Engine;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>Console formatting for season summaries and final standings.</summary>
public static class Report
{
    public static void PrintSeasonSummary(SeasonReport r)
    {
        double koPct = r.TotalFights > 0 ? 100.0 * r.Knockouts / r.TotalFights : 0;
        Console.WriteLine($"--- Season {r.Year,-2} -------------------------------------");
        Console.WriteLine($"  Fights: {r.TotalFights,4}   KO/TKO: {r.Knockouts,4} ({koPct,4:0.0}%)   " +
                          $"Retired: {r.Retirements,3}   Debuts: {r.Debuts,3}");

        if (r.NewChampions.Count > 0)
            Console.WriteLine($"  New champions: {string.Join(", ", r.NewChampions)}");

        if (r.BiggestUpsetWinner is not null && r.BiggestUpsetGap > 150)
            Console.WriteLine($"  Upset of the year: {r.BiggestUpsetWinner.Name} shocked " +
                              $"{r.BiggestUpsetLoser!.Name} (+{r.BiggestUpsetGap:0} pts gap)");
        Console.WriteLine();
    }

    public static void PrintChampions(World world)
    {
        Console.WriteLine();
        Console.WriteLine("WORLD CHAMPIONS");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine($"{"Division",-18}{"Champion",-22}{"Age",4}  {"OVR",4}  Record");
        Console.WriteLine("---------------------------------------------------------------");
        foreach (var wc in WeightClasses.All)
        {
            var champ = world.Divisions[wc].Champion;
            if (champ is null)
                Console.WriteLine($"{wc.DisplayName(),-18}{"(vacant)",-22}");
            else
                Console.WriteLine($"{wc.DisplayName(),-18}{Trim(champ.Name, 21),-22}{champ.Age,4}  {champ.Overall,4}  {champ.Record}");
        }
    }

    public static void PrintPoundForPound(World world, int top)
    {
        var ranked = world.AllBoxers
            .Where(b => !b.Retired)
            .OrderByDescending(b => b.RankPoints)
            .Take(top)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"POUND-FOR-POUND TOP {top}");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine($"{"#",-3}{"Fighter",-22}{"Division",-18}{"OVR",4}  {"Pts",5}  Record");
        Console.WriteLine("---------------------------------------------------------------");
        int rank = 1;
        foreach (var b in ranked)
        {
            string belt = b.IsChampion ? "*" : " ";
            Console.WriteLine($"{rank,-3}{belt}{Trim(b.Name, 20),-21}{b.WeightClass.DisplayName(),-18}" +
                              $"{b.Overall,4}  {b.RankPoints,5:0}  {b.Record}");
            rank++;
        }
        Console.WriteLine("  (* = reigning world champion)");
    }

    public static void PrintFeaturedBout(World world, Random rng)
    {
        // Pick the two highest-rated active fighters as a marquee, catchweight exhibition.
        var stars = world.AllBoxers.Where(b => !b.Retired)
            .OrderByDescending(b => b.RankPoints).Take(2).ToList();
        if (stars.Count < 2) { Console.WriteLine("Not enough fighters for a featured bout."); return; }

        var a = stars[0];
        var b = stars[1];
        Console.WriteLine($"{a.Name} ({a.WeightClass.DisplayName()}, {a.Overall} OVR, {a.Record})");
        Console.WriteLine($"   vs");
        Console.WriteLine($"{b.Name} ({b.WeightClass.DisplayName()}, {b.Overall} OVR, {b.Record})");
        Console.WriteLine();

        var engine = new FightEngine(rng);
        var res = engine.Simulate(a, b, 12);

        Console.WriteLine($"{"Rd",-4}{a.Name,-22}{b.Name,-22}Notes");
        foreach (var rd in res.Rounds)
        {
            string notes = "";
            if (rd.KnockdownsA > 0) notes += $"{a.Name} down x{rd.KnockdownsA}. ";
            if (rd.KnockdownsB > 0) notes += $"{b.Name} down x{rd.KnockdownsB}. ";
            Console.WriteLine($"{rd.Round,-4}{$"{rd.LandedA} landed ({rd.ScoreA})",-22}" +
                              $"{$"{rd.LandedB} landed ({rd.ScoreB})",-22}{notes}");
        }
        Console.WriteLine();
        if (res.Scorecards.Count > 0)
        {
            var cards = string.Join("  ", res.Scorecards.Select(c => $"{c.A}-{c.B}"));
            Console.WriteLine($"Scorecards: {cards}");
        }
        Console.WriteLine($"RESULT: {res.Headline()}");
    }

    /// <summary>Render a single fighter as an ASCII "card" with rating bars.</summary>
    public static void PrintFighterCard(Boxer b)
    {
        const int width = 48;
        string Line(char fill) => "+" + new string(fill, width) + "+";
        void Row(string s) => Console.WriteLine("|" + s.PadRight(width)[..width] + "|");

        string title = b.Nickname is null ? b.Name : $"{b.Name}  \"{b.Nickname}\"";
        string sub = $"{b.WeightClass.DisplayName()} | Age {b.Age} | {b.Record}";
        var style = StyleClassifier.Of(b);

        Console.WriteLine(Line('='));
        Row($" {title}");
        Row($" {sub}");
        Row($" Style: {style.DisplayName()}");
        Console.WriteLine(Line('-'));
        Bar("Power", b.Ratings.Power);
        Bar("Chin", b.Ratings.Chin);
        Bar("Speed", b.Ratings.Speed);
        Bar("Defense", b.Ratings.Defense);
        Bar("Stamina", b.Ratings.Stamina);
        Bar("Accuracy", b.Ratings.Accuracy);
        Bar("Conditioning", b.Ratings.Conditioning);
        Bar("Cut Resist", b.Ratings.CutResistance);
        Bar("Aggression", b.Ratings.Aggression);
        Bar("Heart", b.Ratings.Heart);
        Console.WriteLine(Line('-'));
        Row($" OVERALL  {b.Overall}");
        Console.WriteLine(Line('='));
        Console.WriteLine();

        void Bar(string label, int value)
        {
            const int barLen = 20;
            int filled = (int)Math.Round(value / 100.0 * barLen);
            string bar = new string('#', filled) + new string('.', barLen - filled);
            Row($" {label,-13}{bar} {value,3}");
        }
    }

    /// <summary>Print a whole deck of cards, then stage a dream bout between the top two.</summary>
    public static void PrintCardDeck(IReadOnlyList<Boxer> deck, Random rng)
    {
        Console.WriteLine("Ratings below are this project's own subjective estimates, not from any");
        Console.WriteLine("published boxing game. Edit them freely or import your own deck via --roster.");
        Console.WriteLine();
        foreach (var b in deck)
            PrintFighterCard(b);

        if (deck.Count >= 2)
        {
            Console.WriteLine("=========== DREAM BOUT (round by round) ===========");
            DreamBout(deck[0], deck[1], rng);
        }
    }

    private static void DreamBout(Boxer a, Boxer b, Random rng)
    {
        Console.WriteLine($"{a.Name} vs {b.Name} (catchweight, 12 rounds)");
        Console.WriteLine();
        var res = new FightEngine(rng).Simulate(a, b, 12);
        Console.WriteLine($"{"Rd",-4}{a.Name,-24}{b.Name,-24}Notes");
        foreach (var rd in res.Rounds)
        {
            string notes = "";
            if (rd.KnockdownsA > 0) notes += $"{a.Name} down x{rd.KnockdownsA}. ";
            if (rd.KnockdownsB > 0) notes += $"{b.Name} down x{rd.KnockdownsB}. ";
            Console.WriteLine($"{rd.Round,-4}{$"{rd.LandedA} landed ({rd.ScoreA})",-24}" +
                              $"{$"{rd.LandedB} landed ({rd.ScoreB})",-24}{notes}");
        }
        Console.WriteLine();
        if (res.Scorecards.Count > 0)
            Console.WriteLine($"Scorecards: {string.Join("  ", res.Scorecards.Select(c => $"{c.A}-{c.B}"))}");
        Console.WriteLine($"RESULT: {res.Headline()}");
    }

    /// <summary>Diagnostic: finish rates by division for evenly-matched elite fighters.</summary>
    public static void PrintCalibration(Random rng)
    {
        const int n = 3000;
        var engine = new FightEngine(rng);
        Console.WriteLine($"Finish-rate calibration ({n} 12-round bouts per division, two 78-rated fighters)");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine($"{"Division",-18}{"KO/TKO",9}{"Decision",10}{"Draw",8}");
        foreach (var wc in WeightClasses.All)
        {
            var a = MakeEven("A", wc);
            var b = MakeEven("B", wc);
            int ko = 0, dec = 0, draw = 0;
            for (int i = 0; i < n; i++)
            {
                var res = engine.Simulate(a, b, 12);
                if (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout) ko++;
                else if (res.IsDraw) draw++;
                else dec++;
            }
            Console.WriteLine($"{wc.DisplayName(),-18}{Pct(ko, n),9}{Pct(dec, n),10}{Pct(draw, n),8}");
        }
    }

    private static Boxer MakeEven(string name, WeightClass wc) => new()
    {
        Id = name.GetHashCode(),
        Name = name,
        WeightClass = wc,
        Ratings = new Ratings
        {
            Power = 78, Chin = 78, Speed = 78, Defense = 78, Stamina = 78,
            Accuracy = 78, Conditioning = 78, CutResistance = 78, Aggression = 78, Heart = 78
        },
        Age = 28
    };

    private static string Pct(int x, int n) => $"{100.0 * x / n,5:0.0}%";

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
