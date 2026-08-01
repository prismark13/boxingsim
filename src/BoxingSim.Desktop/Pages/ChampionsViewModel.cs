using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>Every belt in every division.
///
/// A vacant belt is a row like any other, with a null holder — the board has to show that nobody holds it,
/// which is a fact about the division, not an absence of one.</summary>
public sealed class ChampionsViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public ChampionsViewModel(Func<CareerGame?> game)
    {
        _game = game;
        ShowLineage = new Cmd(OpenLineage);
        CloseLineage = new Cmd(() => { Lineage.Clear(); LineageTitle = ""; RaiseLineage(); });
    }

    // ---- the line of succession ----
    //
    // A belt's history was kept and never shown. Opened from the belt itself rather than given a page of its
    // own: the question "who held this before him" is asked while looking at who holds it now.

    public ObservableCollection<ReignRow> Lineage { get; } = new();
    public string LineageTitle { get; private set; } = "";
    public string LineageSubtitle { get; private set; } = "";
    public bool ShowingLineage => Lineage.Count > 0;

    public Cmd ShowLineage { get; }
    public Cmd CloseLineage { get; }

    private void RaiseLineage()
    {
        foreach (var n in new[] { nameof(Lineage), nameof(LineageTitle), nameof(LineageSubtitle),
                                  nameof(ShowingLineage) }) Raise(n);
    }

    private void OpenLineage(object? param)
    {
        Lineage.Clear();
        var game = _game();
        if (game is null || param is not BeltRow row) { RaiseLineage(); return; }

        var line = game.LineageOf(row.Division, row.LineageKey);
        LineageTitle = $"{row.Division.DisplayName()}  ·  {row.Belt}";

        // Newest first. A succession read downward from 1900 buries the men anybody is looking for.
        foreach (var r in line.OrderByDescending(x => x.Won))
        {
            string span = r.IsCurrent
                ? $"{r.Won:MMM yyyy} — present"
                : $"{r.Won:MMM yyyy} — {r.Lost:MMM yyyy}";
            int days = r.DaysHeld(game.Date);
            string held = days >= 365 ? $"{days / 365.0:0.#} years" : $"{Math.Max(1, days / 30)} months";
            string how = (r.TookFrom is null ? "won it vacant" : $"beat {r.TookFrom}")
                       + (r.Defences > 0 ? $"  ·  {r.Defences} def" : "")
                       + (r.LostTo is not null ? $"  ·  lost to {r.LostTo}" : "");
            Lineage.Add(new ReignRow(r.Holder, r.Country ?? "", $"{span}  ·  {held}", how,
                                     r.IsCurrent, r.HolderId == game.Player.Id));
        }

        LineageSubtitle = Lineage.Count == 0
            ? "No one has held this belt yet."
            : $"{Lineage.Count} reigns, back to {line.Min(x => x.Won):yyyy}";
        RaiseLineage();
    }

    public ObservableCollection<DivisionRow> Champions { get; } = new();

    public void Rebuild()
    {
        Champions.Clear();
        var game = _game();
        if (game is not null)
        {
            foreach (var d in game.ChampionsBoard())
            {
                var belts = new List<BeltRow>();
                void Add(string belt, Boxer? holder, int def, bool lineal) =>
                    belts.Add(holder is null
                        ? new BeltRow(belt, "vacant", "", lineal, true, null, d.Division)
                        : new BeltRow(belt, holder.Name, holder.Record + (def > 0 ? $" · {def} def" : ""), lineal, false, holder, d.Division));

                Add(game.LinealBelt, d.Lineal, d.LinealDefenses, true);
                Add(game.PrimaryBelt, d.Wba, d.WbaDefenses, false);
                if (game.WbcActive) Add("WBC", d.Wbc, d.WbcDefenses, false);
                if (game.IbfActive) Add("IBF", d.Ibf, d.IbfDefenses, false);

                Champions.Add(new DivisionRow(d.Division.DisplayName(),
                                              d.Undisputed is Boxer u ? $"undisputed · {u.Name}" : "",
                                              belts, d.Division == game.Player.WeightClass));
            }
        }
        RaiseAll();
    }
}
