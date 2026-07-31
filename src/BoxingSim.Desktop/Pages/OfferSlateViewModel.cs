using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>One of the fights on the table, as the player reads it: who, what it is for, and what taking it
/// would mean for the career. The last of those is the whole point — a name and a record is a guess.</summary>
public sealed class OfferChoice
{
    public required FightOffer Offer { get; init; }
    public required string Name { get; init; }
    public required string Under { get; init; }
    public required string What { get; init; }
    public required string Distance { get; init; }
    public required string Why { get; init; }
    public bool IsTitle { get; init; }
    public bool IsBiggest { get; init; }
    public required Cmd Pick { get; init; }
}

/// <summary>The fights on the table.
///
/// A career used to offer one decision: fight, or wait. The matchmaker now puts two or three nights up and the
/// choice between them IS the career — a tune-up, a step up, or the man who will hurt you and move you up the
/// board. What each one is for has to be legible, because two records of men you have never heard of is not a
/// choice, it is a guess.
///
/// It reaches the world through the three delegates rather than holding a CareerViewModel, so the dependency
/// runs one way: the shell knows about the slate, the slate knows nothing about the shell. Game is fetched
/// through a delegate rather than captured because it is replaced wholesale when a career is started, loaded
/// or abandoned — a captured reference would go on describing the previous world.</summary>
public sealed class OfferSlateViewModel : Observable
{
    private readonly Func<CareerGame?> _game;
    private readonly Func<bool> _stillAnOffer;
    private readonly Action _afterPick;

    public OfferSlateViewModel(Func<CareerGame?> game, Func<bool> stillAnOffer, Action afterPick)
    {
        _game = game;
        _stillAnOffer = stillAnOffer;
        _afterPick = afterPick;
    }

    public ObservableCollection<OfferChoice> Choices { get; } = new();

    /// <summary>Only worth showing when there is something to choose BETWEEN, and only before he commits.</summary>
    public bool ShowChoices => Choices.Count > 1 && _stillAnOffer();

    /// <summary>Re-read the slate from the world. Ends in RaiseAll rather than a list of property names —
    /// the shell's own RaiseAll cannot reach this object's bindings now that it is a separate one.</summary>
    public void Rebuild()
    {
        var game = _game();
        Choices.Clear();
        if (game is not null && !game.Player.Retired && game.Slate.Count > 1)
        {
            // Hardest first, which is how a matchmaker lays a choice out and how it reads.
            var scored = game.Slate.Select(o => (Offer: o, Value: game.ValueOf(o)))
                                   .OrderByDescending(x => x.Value).ToList();
            for (int rank = 0; rank < scored.Count; rank++)
            {
                var (o, v) = scored[rank];
                int place = game.PlaceOf(o.Opponent);
                Choices.Add(new OfferChoice
                {
                    Offer = o,
                    Name = o.Opponent.Name,
                    Under = $"{o.Opponent.Country ?? ""}  ·  {o.Opponent.Record}"
                            + (place > 0 ? $"  ·  #{place}" : ""),
                    What = o.TitleFight ? $"{o.Belt} TITLE" : o.Context.ToUpperInvariant(),
                    Distance = $"{o.Rounds} rounds",
                    IsTitle = o.TitleFight,
                    IsBiggest = rank == 0 && scored.Count > 1,
                    Why = WhyTakeIt(o, rank, scored.Count),
                    // Picking is the ONLY thing that assigns Offer, and it assigns it once. Offer's setter
                    // runs SetTheCard and AnnounceUndercard, both of which draw from the world's rng — so
                    // anything that assigned it to preview a choice would silently reshuffle the world every
                    // time the player looked at a different fight.
                    Pick = new Cmd(() => { game.ChooseOffer(o); _afterPick(); }),
                });
            }
        }
        RaiseAll();
    }

    /// <summary>The slate hides the moment the fight is taken, and that depends on a fact the shell owns
    /// (whether he has committed) rather than on anything here — so the shell says when it has changed.</summary>
    public void RaiseVisible() => Raise(nameof(ShowChoices));

    /// <summary>One line on what taking this night would mean. Said in terms of the CAREER rather than the
    /// numbers — the ratings are on the tale of the tape for anyone who wants them.</summary>
    private string WhyTakeIt(FightOffer o, int rank, int of)
    {
        if (o.TitleFight && o.Belt is "WBA" or "WBC" or "IBF")
            return "The belt. Everything has been for this.";
        if (o.TitleFight) return "A regional title — the last step before world level.";
        if (o.Context is "eliminator") return "Win it and you are next in line.";

        // Ranked above him is the fact that matters most, whatever else the night is.
        var game = _game();
        int mine = game?.PlaceOf(game.Player) ?? 0;
        int his = game?.PlaceOf(o.Opponent) ?? 0;
        if (his > 0 && mine > 0 && his < mine)
            return $"Ranked above you at #{his}. Beat him and you take his place.";

        // Otherwise say where it sits AMONG THE OTHERS, which is the actual decision. Comparing each one to
        // an absolute scale gave three fights of similar size the same sentence — "the biggest night on the
        // table", three times, which is no help to anybody.
        if (of <= 1) return "The only fight on offer.";
        if (rank == 0) return "The hardest of them, and the one that moves you.";
        if (rank == of - 1) return "The safest of them. Keeps you winning, and teaches you nothing.";
        return "Middle ground — a test, without the risk of the big one.";
    }
}
