using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>The feed, and the two filters it is read through.
///
/// Read in two places at once: the News page and the shell's drawer. They deliberately share this one object,
/// so narrowing in the drawer narrows the page and the other way round — the alternative is two feeds that
/// disagree about what the sport has been doing.
///
/// It needs to know whether a career is running as well as whether a world is, because "your division" is only
/// an idea in career mode. That is the shell's fact, so it arrives as a delegate.</summary>
public sealed class NewsViewModel : Observable
{
    private readonly Func<CareerGame?> _game;
    private readonly Func<bool> _inCareer;

    public NewsViewModel(Func<CareerGame?> game, Func<bool> inCareer)
    {
        _game = game;
        _inCareer = inCareer;
        ClearNewsFilter = new Cmd(() =>
        {
            _newsTitlesOnly = false;
            _newsDiv = NewsDivisions.FirstOrDefault();
            Rebuild();
        });
    }

    public ObservableCollection<NewsRow> News { get; } = new();

    // ---- filtering the feed ----
    //
    // Twelve divisions all reporting at once buries the two things anyone actually scans a boxing feed for:
    // what happened to the belts, and what happened in a weight he cares about. CareerEvent has carried Div
    // and Kind all along for exactly this.

    private bool _newsTitlesOnly;
    public bool NewsTitlesOnly
    {
        get => _newsTitlesOnly;
        set { if (_newsTitlesOnly == value) return; _newsTitlesOnly = value; Rebuild(); }
    }

    /// <summary>The divisions the feed can be narrowed to — only those that have actually reported something,
    /// so the list never offers a weight with nothing in it. Heaviest first, matching the rankings.</summary>
    public ObservableCollection<NewsDivChoice> NewsDivisions { get; } = new();

    private NewsDivChoice? _newsDiv;
    public NewsDivChoice? NewsDivision
    {
        get => _newsDiv ??= NewsDivisions.FirstOrDefault();
        set { _newsDiv = value; Rebuild(); }
    }

    public bool NewsIsFiltered => _newsTitlesOnly || NewsDivision?.Div is not null;
    public bool NewsIsEmpty => News.Count == 0;

    public Cmd ClearNewsFilter { get; }

    /// <summary>Rebuild the division list, then the feed. In that order: the feed is filtered by whichever
    /// division choice survives the rebuild.</summary>
    public void Rebuild()
    {
        BuildDivisionChoices();
        BuildNews();
        RaiseAll();
    }

    private void BuildDivisionChoices()
    {
        var game = _game();
        var had = _newsDiv?.Div;
        bool hadMine = _newsDiv?.IsMine == true;
        NewsDivisions.Clear();
        NewsDivisions.Add(new NewsDivChoice("Every division", null));

        // Your own weight, first and named as yours. It is the division anyone checks first, and unlike picking
        // "Middleweight" off the list it follows you when you move up — choose it once and it stays right.
        if (game is not null && _inCareer())
            NewsDivisions.Add(new NewsDivChoice($"Your division · {game.Player.WeightClass.DisplayName()}",
                                               game.Player.WeightClass, IsMine: true));

        if (game is not null)
            foreach (var d in game.Log.Where(e => e.Div is not null)
                                      .Select(e => e.Div!.Value)
                                      .Distinct()
                                      .OrderByDescending(d => (int)d))
                NewsDivisions.Add(new NewsDivChoice(d.DisplayName(), d));

        // Hold the player's choice across a rebuild; the collection is replaced every turn. "Your division" is
        // matched on being YOURS rather than on the weight it happened to mean, so moving up carries it with you
        // instead of quietly pinning you to the division you left.
        _newsDiv = hadMine
            ? NewsDivisions.FirstOrDefault(c => c.IsMine) ?? NewsDivisions.FirstOrDefault()
            : NewsDivisions.FirstOrDefault(c => c.Div == had && !c.IsMine) ?? NewsDivisions.FirstOrDefault();
    }

    private void BuildNews()
    {
        News.Clear();
        var game = _game();
        if (game is null) return;

        var div = NewsDivision?.Div;
        // By DATE, newest first — the world resolves a division at a time, so the order events are logged in is
        // not the order they happened in. Filtered BEFORE the cap, or narrowing to one weight would show only
        // whatever survived from the newest 120 across all of them.
        foreach (var (e, _) in game.Log.Select((e, i) => (e, i))
                                       .Where(x => !_newsTitlesOnly || x.e.Kind == "title")
                                       .Where(x => div is null || x.e.Div == div)
                                       .OrderByDescending(x => x.e.On).ThenByDescending(x => x.i)
                                       .Take(120))
            News.Add(new NewsRow(e.DateLabel, e.Text, e.Kind ?? "", e.PlayerBout, e.Bout, e.Detail ?? ""));
    }
}
