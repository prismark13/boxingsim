using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>Auto-plays a created fighter's heavyweight career to smoke-test the career engine.</summary>
public static class CareerDemo
{
    public static void Run(IReadOnlyList<Boxer> historical, Random rng)
    {
        var player = CareerGame.CreatePlayer(rng, "Tommy \"Kid\" Malone", "USA", WeightClass.Heavyweight, potential: 88);
        var game = new CareerGame(1960, player, historical, rng);

        Console.WriteLine("==========================================================");
        Console.WriteLine($"  CAREER MODE — {player.Name} ({player.Country}), heavyweight");
        Console.WriteLine($"  Debut {game.Year}, age {player.Age}, potential {player.Potential}");
        Console.WriteLine("==========================================================\n");

        Console.WriteLine("--- WORLD RANKING AT DEBUT (after 10-year warm-up) ---");
        int rk = 1;
        foreach (var b in game.Active.Where(CareerGame.WorldRanked).OrderByDescending(CareerGame.RankScore).Take(12))
            Console.WriteLine($"  {rk++,2}. {b.Name,-22} OVR{b.Overall,3}  {b.Record}{(b.IsChampion ? "  [CHAMP]" : "")}");
        Console.WriteLine();

        int bout = 0;
        while (game.Offer is not null && !player.Retired && bout < 70)
        {
            var offer = game.Offer!;
            var stage = CareerStages.Of(player);
            var res = game.TakeOffer();
            if (res is null) break;
            bout++;

            string outcome = res.IsDraw ? "DRAW" : res.Winner!.Id == player.Id
                ? $"WON  {res.Method}" + (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout ? $" rd{res.EndRound}" : "")
                : $"LOST {res.Method}" + (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout ? $" rd{res.EndRound}" : "");

            int rank = game.Active.OrderByDescending(b => b.RankPoints).ToList().FindIndex(b => b.Id == player.Id) + 1;
            string belt = player.IsChampion ? " [CHAMP]" : "";
            string title = offer.TitleFight ? " *TITLE*" : "";
            Console.WriteLine($"{game.Year} a{player.Age} {CareerStages.Label(stage),-11} OVR{player.Overall,3}  vs {offer.Opponent.Name,-22}({offer.Opponent.Overall,2}) {title,-7} {outcome,-12}  {player.Record}  #{rank}{belt}");
        }

        Console.WriteLine($"\n--- CAREER OVER: {player.Name} ---");
        Console.WriteLine($"Final record: {player.Record}  (peak OVR reached implicitly; ended at {player.Overall})");
        Console.WriteLine($"Retired in {game.Year} at age {player.Age}. Titles won: {(player.IsChampion ? "world champion at retirement" : "see timeline")}");

        var injuries = game.Log.Where(e => e.PlayerBout && e.Text.Contains("suffered")).ToList();
        Console.WriteLine($"\n--- INJURIES SUSTAINED ({injuries.Count}) ---");
        foreach (var e in injuries) Console.WriteLine($"  {e.DateLabel}: {e.Text}");

        Console.WriteLine($"\n--- TITLE REIGNS ({game.Reigns.Count}, {game.TitleDefenses} defences, {game.DaysAsChampion / 30} months) ---");
        foreach (var reign in game.Reigns)
            Console.WriteLine($"  {reign.Won:MMM yyyy} – {(reign.Lost is null ? "present" : reign.Lost.Value.ToString("MMM yyyy"))} · {reign.Defenses} defence(s)");

        Console.WriteLine("\n--- WORLD TIMELINE (real fighters debuting + title changes) ---");
        foreach (var e in game.Log.Where(e => !e.PlayerBout).Take(40))
            Console.WriteLine($"  {e.DateLabel}: {e.Text}");
    }
}
