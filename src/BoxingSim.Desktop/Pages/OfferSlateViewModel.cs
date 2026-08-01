using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Analysis;
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

    /// <summary>Who the ratings favour, in the world's own terms. Not a new opinion invented for this screen:
    /// it is the Elo expectation the ranking update itself runs on, so the number that decides what a result
    /// is WORTH is the number quoted before the fight.</summary>
    public string Odds { get; init; } = "";

    /// <summary>Whether the styles make it awkward. The engine has always fought the matchup — it drives the
    /// exchanges — and the tale of the tape has always described it. It was only ever said AFTER you had
    /// picked, which is the wrong side of the decision.</summary>
    public string Styles { get; init; } = "";
    public bool HasStyles => Styles.Length > 0;

    /// <summary>True when the ratings make him the favourite — for drawing the odds line as a warning rather
    /// than as reassurance.</summary>
    public bool Underdog { get; init; }
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

    /// <summary>Shown whenever there is a fight on the table that he has not picked yet — including when the
    /// slate came back with only one name on it. A single option still has to be ACCEPTED; the alternative is
    /// a fight nobody agreed to, which is what this whole screen exists to stop.</summary>
    public bool ShowChoices => Choices.Count > 0 && _stillAnOffer();

    /// <summary>Why there is no belt among these, when he has just moved up. A man who has crossed a division
    /// is shown a slate of tune-ups and told nothing — he cannot tell a deliberate stretch of gatekeepers from
    /// the matchmaker having forgotten him, and a world champion looking at three club fights assumes the
    /// latter. Saying how many are left turns a silence into a countdown.</summary>
    public string TuneUpNote
    {
        get
        {
            int left = _game()?.TuneUpsLeft ?? 0;
            return left <= 0 ? ""
                 : left == 1 ? "One more tune-up at this weight, then a world title is open to you."
                 : $"{left} more tune-ups at this weight, then a world title is open to you.";
        }
    }
    public bool HasTuneUpNote => TuneUpNote.Length > 0;

    /// <summary>Re-read the slate from the world. Ends in RaiseAll rather than a list of property names —
    /// the shell's own RaiseAll cannot reach this object's bindings now that it is a separate one.</summary>
    public void Rebuild()
    {
        var game = _game();
        Choices.Clear();
        if (game is not null && !game.Player.Retired && game.Slate.Count > 0)
        {
            // Biggest first, by what the night is worth — which is how a matchmaker lays a choice out.
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
                    Odds = OddsOn(game.Player, o.Opponent),
                    Underdog = o.Opponent.RankPoints > game.Player.RankPoints,
                    Styles = StyleRead(game.Player, o.Opponent),
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

    /// <summary>What the ratings give you, as a percentage.
    ///
    /// The same expectation term the ranking update uses — 1 / (1 + 10^((them - me) / 400)) — so the screen
    /// and the world agree about who is supposed to win. Ranking points are the right scale for this and the
    /// rating is not: Overall is what a man CAN do, while his standing is what the sport has come to expect of
    /// him, and being the underdog is a fact about expectation.</summary>
    private static string OddsOn(Boxer me, Boxer them)
    {
        int pct = (int)Math.Round(100.0 / (1.0 + Math.Pow(10, (them.RankPoints - me.RankPoints) / 400.0)));
        return pct >= 80 ? $"You are a heavy favourite — the ratings give you {pct}%."
             : pct >= 60 ? $"The ratings favour you, {pct}%."
             : pct >= 45 ? $"Close to even: {pct}% your way."
             : pct >= 25 ? $"He is favoured. The ratings give you {pct}%."
             : $"You would be the underdog — {pct}%.";
    }

    /// <summary>Whether the styles make it awkward, in one clause. Deliberately shorter than the tale of the
    /// tape's version: this is three cards side by side, not a screen of its own.</summary>
    private static string StyleRead(Boxer me, Boxer them)
    {
        var a = StyleClassifier.Of(me);
        var b = StyleClassifier.Of(them);
        double edge = FightingStyles.Advantage(a, b);
        string his = b.DisplayName().ToLowerInvariant();
        return a == b ? $"Two {his}s — nothing in the styles."
             : edge >= 0.45 ? $"The styles are yours — a {his} is exactly who you want in front of you."
             : edge >= 0.15 ? $"The styles lean your way against a {his}."
             : edge > -0.15 ? $"Nothing in the styles against a {his}."
             : edge > -0.45 ? $"Awkward: a {his} knows what to do with your style."
             : $"A bad style night — a {his} is exactly the wrong man for you.";
    }

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
        // Phrased as what the night is WORTH, not how hard it is. The order comes from BoutValue — a
        // promoter's measure: the stakes, the names, how competitive it looks. That is not the same as
        // difficulty, and calling the top one "the hardest" put it directly above an odds line saying the
        // ratings made you a 66% favourite. The odds speak to how hard it is; this speaks to what it is for.
        if (of <= 1) return "The only fight on offer.";
        if (rank == 0) return "The biggest night of the three — the one that moves you.";
        if (rank == of - 1) return "The smallest of them. Keeps you busy, and teaches you nothing.";
        return "Middle ground — worth having, without the weight of the big one.";
    }
}
