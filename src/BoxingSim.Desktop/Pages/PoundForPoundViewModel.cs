using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>The pound-for-pound list: ranked on what they have won rather than on what they weigh, which is
/// why the detail line is a list of honours and not a division placing.</summary>
public sealed class PoundForPoundViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public PoundForPoundViewModel(Func<CareerGame?> game) => _game = game;

    public ObservableCollection<RankRow> PoundForPound { get; } = new();

    public void Rebuild()
    {
        PoundForPound.Clear();
        var game = _game();
        if (game is not null)
        {
            int r = 1;
            foreach (var b in game.PoundForPound(15))
            {
                var a = game.AchievementsOf(b);
                var bits = new List<string> { b.WeightClass.DisplayName() };
                if (a.Undisputed) bits.Add("UNDISPUTED"); else bits.AddRange(a.Belts);
                if (a.Lineal) bits.Add(game.LinealBelt);
                if (a.Defences > 0) bits.Add($"{a.Defences} defence{(a.Defences == 1 ? "" : "s")}");
                if (a.WeightTitles >= 2) bits.Add($"{a.WeightTitles}-weight champ");
                if (a.Belts.Count == 0 && !a.Lineal && a.TitleWins > 0) bits.Add($"ex-champ · {a.TitleWins} title wins");
                PoundForPound.Add(new RankRow(r.ToString(), b.Class, b.Name, string.Join(" · ", bits),
                                              b.Record.ToString(), b.Id == game.Player.Id, game.IsWorldChampion(b), b));
                r++;
            }
        }
        RaiseAll();
    }
}
