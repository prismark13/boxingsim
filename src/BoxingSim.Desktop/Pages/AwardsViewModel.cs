using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>The year's honours, filtered by category and year.
///
/// Both filters live here rather than on the shell because they are a property of how this page is being
/// READ — nothing else in the app is narrowed by them.</summary>
public sealed class AwardsViewModel : Observable
{
    private const string AllAwards = "All awards";
    private const string AllYears = "All years";

    private readonly Func<CareerGame?> _game;

    public AwardsViewModel(Func<CareerGame?> game) => _game = game;

    public ObservableCollection<AwardRow> Awards { get; } = new();

    public IReadOnlyList<string> AwardCategories { get; } = new[]
    {
        AllAwards, "Fighter of the Year", "Fight of the Year", "Knockout of the Year", "Upset of the Year"
    };

    public ObservableCollection<string> AwardYears { get; } = new();

    private string _awardCategory = AllAwards;
    public string AwardCategory
    {
        get => _awardCategory;
        set { if (_awardCategory == value) return; _awardCategory = value; Raise(); Rebuild(); }
    }

    private string _awardYear = AllYears;
    public string AwardYear
    {
        get => _awardYear;
        set { if (_awardYear == value) return; _awardYear = value; Raise(); Rebuild(); }
    }

    public string AwardsSubtitle => Awards.Count == 0
        ? "Nothing matches this filter — awards are handed out at the end of each year."
        : $"{Awards.Count} categor{(Awards.Count == 1 ? "y" : "ies")} · {AwardCategory} · {AwardYear}";

    public void Rebuild()
    {
        Awards.Clear();
        var game = _game();
        if (game is null) { RaiseAll(); return; }

        // Keep the year list in step with the career without losing the current pick.
        var years = new[] { AllYears }.Concat(game.Awards.Select(a => a.Year.ToString())).ToList();
        if (!AwardYears.SequenceEqual(years))
        {
            var keep = _awardYear;
            AwardYears.Clear();
            foreach (var y in years) AwardYears.Add(y);
            if (!years.Contains(keep)) _awardYear = AllYears;
        }

        foreach (var yr in game.Awards)
        {
            if (_awardYear != AllYears && yr.Year.ToString() != _awardYear) continue;
            void Add(string cat, IReadOnlyList<AwardWinner> list)
            {
                if (list.Count == 0) return;
                if (_awardCategory != AllAwards && cat != _awardCategory) return;
                var places = list.Select((w, i) => new AwardPlace(
                    i == 0 ? "1st" : i == 1 ? "2nd" : "3rd",
                    w.Name, w.Div.DisplayName(), w.Detail, i == 0, cat, yr.Year, w.Commentary, w.Bout)).ToList();
                Awards.Add(new AwardRow(cat, yr.Year, places));
            }
            Add("Fighter of the Year", yr.FighterOfYear);
            Add("Fight of the Year", yr.FightOfYear);
            Add("Knockout of the Year", yr.KnockoutOfYear);
            Add("Upset of the Year", yr.UpsetOfYear);
        }
        RaiseAll();
    }
}
