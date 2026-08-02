using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>What this fighter has done so far, as a wall of tiles.
///
/// Everything below the first eight tiles is aggregated from the per-round cards stored on each bout — the
/// same data the fight detail view shows one fight at a time — so a career with no scored rounds behind it
/// simply has fewer tiles rather than a page of zeroes.</summary>
public sealed class StatsViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public StatsViewModel(Func<CareerGame?> game) => _game = game;

    public ObservableCollection<StatRow> Stats { get; } = new();

    public void Rebuild()
    {
        Stats.Clear();
        var game = _game();
        if (game is null) { RaiseAll(); return; }
        var p = game.Player;
        int fights = p.Record.Wins + p.Record.Losses + p.Record.Draws;
        int koPct = p.Record.Wins > 0 ? (int)Math.Round(100.0 * p.Record.KnockoutWins / p.Record.Wins) : 0;

        var titleWins = p.History.Count(h => h.Result == 'W' && h.Note is not null && h.Note.EndsWith(" title"));
        var reigns = game.Reigns.ToList();
        var divisions = p.History.Count > 0
            ? game.Reigns.Select(r => r.Belt).Distinct().Count()
            : 0;

        // Longest win streak across the whole ledger.
        int best = 0, run = 0;
        foreach (var h in p.History.OrderBy(h => h.Date))
        {
            if (h.Result == 'W') { run++; best = Math.Max(best, run); } else run = 0;
        }

        var bestWin = p.History.Where(h => h.Result == 'W' && h.Note is not null)
                               .OrderByDescending(h => h.Date).FirstOrDefault();

        Stats.Add(new StatRow("Record", p.Record.ToString(), $"{fights} fights"));
        Stats.Add(new StatRow("Knockout wins", $"{p.Record.KnockoutWins}",
                              p.Record.TechnicalKnockoutWins > 0
                                  ? $"{koPct}% of wins, and {p.Record.TechnicalKnockoutWins} more by stoppage"
                                  : $"{koPct}% of wins"));
        Stats.Add(new StatRow("Longest win streak", best.ToString(), best >= 10 ? "a real run" : ""));
        Stats.Add(new StatRow("Title bouts won", titleWins.ToString(), ""));
        Stats.Add(new StatRow("Title reigns", reigns.Count.ToString(),
                              reigns.Count > 0 ? string.Join(", ", reigns.Select(r => r.Belt).Distinct()) : ""));
        Stats.Add(new StatRow("Title defences", game.TitleDefenses.ToString(), ""));
        Stats.Add(new StatRow("Days as champion", game.DaysAsChampion.ToString("N0"),
                              game.DaysAsChampion > 365 ? $"{game.DaysAsChampion / 365} years" : ""));
        Stats.Add(new StatRow("Current rating", $"{p.Overall} OVR", $"class {p.Class}"));

        // Everything below is aggregated from the per-round cards stored on each bout — the same data the
        // fight detail view shows one fight at a time.
        var scored = p.History.Where(h => h.Rounds is { Count: > 0 }).ToList();
        if (scored.Count > 0)
        {
            var rounds = scored.SelectMany(h => h.Rounds!).ToList();
            int lf = rounds.Sum(r => r.LandedFor), la = rounds.Sum(r => r.LandedAgainst);
            int kf = rounds.Sum(r => r.KdFor), ka = rounds.Sum(r => r.KdAgainst);
            int roundsWon = rounds.Count(r => r.ScoreFor > r.ScoreAgainst);

            Stats.Add(new StatRow("Rounds boxed", rounds.Count.ToString(),
                                  $"{roundsWon} won ({100.0 * roundsWon / rounds.Count:0}%)"));
            Stats.Add(new StatRow("Punches landed", lf.ToString("N0"),
                                  $"{(double)lf / rounds.Count:0.0} a round"));
            Stats.Add(new StatRow("Punches absorbed", la.ToString("N0"),
                                  $"{(double)la / rounds.Count:0.0} a round"));
            // The spread, not just the average — a man averaging 14 who ranges 4 to 30 is a different fighter
            // from one who lands 13 or 15 every round.
            Stats.Add(new StatRow("Output range",
                                  $"{rounds.Min(r => r.LandedFor)}–{rounds.Max(r => r.LandedFor)}",
                                  "landed in a round, worst to best"));
            Stats.Add(new StatRow("Absorbed range",
                                  $"{rounds.Min(r => r.LandedAgainst)}–{rounds.Max(r => r.LandedAgainst)}",
                                  "taken in a round, best to worst"));
            Stats.Add(new StatRow("Punch differential", (lf - la >= 0 ? "+" : "") + (lf - la).ToString("N0"),
                                  lf >= la ? "outlanding them" : "being outlanded"));
            Stats.Add(new StatRow("Knockdowns", $"{kf}–{ka}",
                                  $"{kf} scored, {ka} suffered"));
        }

        int koWins = p.History.Count(h => h.Result == 'W' && h.Method is "KO" or "TKO");
        int decWins = p.Record.Wins - koWins;
        int koLosses = p.History.Count(h => h.Result == 'L' && h.Method is "KO" or "TKO");
        Stats.Add(new StatRow("Wins by stoppage", $"{koWins}", $"{decWins} on the cards"));
        Stats.Add(new StatRow("Times stopped", $"{koLosses}",
                              koLosses == 0 && p.Record.Losses > 0 ? "never stopped" : ""));
        Stats.Add(new StatRow("Peak potential", $"{p.Potential}", ""));
        if (bestWin is not null)
            Stats.Add(new StatRow("Latest title win", bestWin.Opponent, $"{bestWin.Note} · {bestWin.Date:d MMM yyyy}"));

        RaiseAll();
    }
}
