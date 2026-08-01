using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The player's own turn: the offer he is looking at, the fight he takes, and the two things
/// that stand beside it — tonight's undercard and the man he is measured against.</summary>
public sealed partial class CareerGame
{
    // ---- player turn ----

    /// <summary>The rest of tonight's show.
    ///
    /// The player's fight happened in an empty building: one bout, on its own, with nothing before it. Every
    /// fight he has ever had has been the only fight in the world that night. A card is four or five men on
    /// the way up boxing in front of a half-full hall before the main event, and their results are on the
    /// same page of the paper as his the next morning.
    ///
    /// These are real bouts in the world - the men involved gain and lose from them exactly as they would on
    /// any other card - staged on his date, in his division, from the men beneath him.</summary>
    private readonly List<UndercardBout> _undercard = new();

    /// <summary>Tonight's supporting bouts, in the order they were fought - the main event is the player's.</summary>
    public IReadOnlyList<UndercardBout> Undercard => _undercard;

    /// <summary>Tonight's card: what kind of show it is, where it is, and where he is on it.</summary>
    public CardTier Tier { get; private set; } = CardTier.ClubShow;
    public Venue? Hall { get; private set; }
    public Billing Billing { get; private set; } = Billing.MainEvent;

    /// <summary>The card in one line, as a poster would put it.</summary>
    public string BillHeader => Hall is null ? ""
        : $"{Hall.Full}  ·  {Tier.Describe()}  ·  {Billing.Describe()}";

    /// <summary>What size of night this is, and where the player stands on it.
    ///
    /// Two questions, and the second is the interesting one. A card's size comes from the biggest fight on
    /// it, and the player's own bout is usually that fight — but not always, and that is the point. A
    /// prospect does not headline the Garden at 6-0; he opens it. Boxing puts young fighters on big shows
    /// precisely so somebody sees them: three from the bottom, six rounds, while the hall fills up.
    ///
    /// So a man whose own fight would only carry a club show sometimes finds himself on a far bigger card
    /// in a far smaller slot — which is a better night for him than headlining a small hall, and should
    /// read like one.</summary>
    private void SetTheCard()
    {
        if (Offer is not { } o) return;

        // The card his own fight would headline.
        CardTier own = o.TitleFight && !RegionalBelts.Contains(o.Belt ?? "") ? CardTier.Championship
                     : o.TitleFight ? CardTier.Regional
                     : o.Context is "eliminator" ? CardTier.National
                     : WorldRanked(Player) && Top20Ids(Player.WeightClass).Contains(Player.Id) ? CardTier.National
                     : ProFights(Player) >= 10 ? CardTier.Regional
                     : CardTier.ClubShow;

        Tier = own;
        Billing = Billing.MainEvent;

        // A fighter not yet headlining anything gets carried onto somebody else's bigger show. The better
        // the prospect the likelier that is — it is most of what being a prospect buys you.
        if (own <= CardTier.Regional && !o.TitleFight)
        {
            double lift = Player.Potential >= 80 ? 0.45 : Player.Potential >= 70 ? 0.30 : 0.12;
            if (_rng.NextDouble() < lift)
            {
                Tier = own == CardTier.ClubShow
                     ? (_rng.NextDouble() < 0.35 ? CardTier.National : CardTier.Regional)
                     : (_rng.NextDouble() < 0.40 ? CardTier.Championship : CardTier.National);
                // Where he ends up ON that show is no longer rolled for here. It is worked out in
                // AnnounceUndercard from how his fight compares with the rest of the bill, which is what
                // decides it in life: you are the main event if yours is the biggest fight in the building.
            }
        }

        // The hall comes off the date, so the same night is always in the same building however often the
        // card is redrawn.
        var pick = new Random(OfferDate.DayNumber * 31 + (int)Player.WeightClass);
        Hall = VenueBook.Pick(Player.Country, Tier, OfferDate.Year, pick);
    }

    /// <summary>The supporting bouts as ANNOUNCED — matched now, fought on the night.
    ///
    /// A card is a thing you read before it happens. Building it at the opening bell meant the only way to
    /// find out what else was on was to have already fought, which is the wrong way round: the point of a
    /// bill is that you look at it beforehand and decide whether the night is worth your while.</summary>
    private readonly List<(Boxer A, Boxer B, int Rounds)> _billed = new();

    /// <summary>How many supports are billed ABOVE the player's own fight. Derived from what the fights are
    /// worth, not chosen — see <see cref="AnnounceUndercard"/>.</summary>
    private int _playerSlot;

    /// <summary>Match the supporting bouts, and work out where the player stands among them. They are made
    /// here, not fought.
    ///
    /// A CARD IS NOT A DIVISION. This used to draw every supporting bout from the player's own weight, so a
    /// heavyweight's championship bill was seven more heavyweight fights — which is not a promotion, it is a
    /// division's spare men in a room. A real bill runs across the weights, most of it near the main event's
    /// because that is who the promoter has and who the crowd came for, with a spread either side.
    ///
    /// And the running order is the fights' own doing. Every bout is scored by <see cref="BoutValue"/> and the
    /// bill is built biggest-first, which also settles where the player is on it: he headlines if his is the
    /// biggest fight in the building, and opens if six others outrank it. That used to be a coin toss.</summary>
    private void AnnounceUndercard()
    {
        _billed.Clear();
        _undercard.Clear();
        _playerSlot = 0;
        Billing = Billing.MainEvent;
        if (Offer is null || Player.Retired) return;

        // A club show is four fights and a raffle; a championship bill runs all night.
        int want = Tier switch
        {
            CardTier.Championship => 6 + _rng.Next(3),
            CardTier.National => 5 + _rng.Next(2),
            CardTier.Regional => 4 + _rng.Next(2),
            _ => 3 + _rng.Next(2),
        };

        var used = new HashSet<int> { Player.Id, Offer.Opponent.Id };
        var made = new List<(Boxer A, Boxer B, int Rounds, double Value)>();
        foreach (var wc in BillDivisions(want))
        {
            if (MatchOneSupport(wc, used) is not { } bout) continue;
            made.Add(bout);
            used.Add(bout.A.Id); used.Add(bout.B.Id);
        }

        // Top to bottom, biggest first.
        made.Sort((x, y) => y.Value.CompareTo(x.Value));
        foreach (var m in made) _billed.Add((m.A, m.B, m.Rounds));

        double mine = BoutValue.Of(Player, Offer.Opponent, StakesOf(Offer), Player.WeightClass,
                                   ValuePlace(Player), ValuePlace(Offer.Opponent));
        _playerSlot = made.Count(m => m.Value > mine);
        Billing = _playerSlot switch
        {
            0 => Billing.MainEvent,
            1 => Billing.ChiefSupport,
            2 or 3 => Billing.SwingBout,
            _ => Billing.Opener,
        };
    }

    /// <summary>Which weights tonight's supporting bouts come from. The main event's division is the likeliest
    /// and the odds fall away either side of it, so a middleweight bill is mostly middleweights with a couple
    /// of lighter openers — rather than a random sample of the whole sport, which is not a promotion either.
    /// Sampled with replacement, so two or three bouts in one weight is the normal shape.</summary>
    private IEnumerable<WeightClass> BillDivisions(int want)
    {
        var home = (int)Player.WeightClass;
        var live = AllDivisions.Where(DivisionActive).ToList();
        if (live.Count == 0) yield break;
        var odds = live.Select(wc => 1.0 / Math.Pow(1 + Math.Abs((int)wc - home), 1.7)).ToList();
        double total = odds.Sum();
        for (int i = 0; i < want; i++)
        {
            double r = _rng.NextDouble() * total;
            for (int k = 0; k < live.Count; k++)
            {
                r -= odds[k];
                if (r <= 0) { yield return live[k]; break; }
            }
        }
    }

    /// <summary>One supporting bout from a given weight, or null if that division cannot make one tonight.</summary>
    private (Boxer A, Boxer B, int Rounds, double Value)? MatchOneSupport(WeightClass wc, HashSet<int> used)
    {
        var top20 = Top20Ids(wc);
        var top8 = Top8Ids(wc);
        // A club show cannot put a contender on; a championship bill can, and that is largely what makes it
        // one — a world title with nothing but six-round novices under it is not a championship show.
        bool bigShow = Tier >= CardTier.National;
        var pool = ActiveIn(wc)
            .Where(b => !used.Contains(b.Id) && _medical.Available(b) && !AtYearCap(b) && ProFights(b) >= 2
                     && Rested(b) && (bigShow || !top20.Contains(b.Id)))
            .OrderBy(_ => _rng.Next())
            .ToList();

        for (int i = 0; i + 1 < pool.Count; i += 2)
        {
            var a = pool[i]; var b = pool[i + 1];
            if (BadMatch(a, b, top20, top8)) continue;
            int rounds = top20.Contains(a.Id) && top20.Contains(b.Id) ? 10
                       : ProFights(a) < 8 || ProFights(b) < 8 ? 6 : 8;
            return (a, b, rounds,
                    BoutValue.Of(a, b, BoutStakes.None, wc, ValuePlace(a), ValuePlace(b)));
        }
        return null;
    }

    /// <summary>What the player's own fight has riding on it, in the terms BoutValue understands.</summary>
    private BoutStakes StakesOf(FightOffer o) =>
        !o.TitleFight ? (o.Context is "eliminator" ? BoutStakes.Eliminator : BoutStakes.None)
        : RegionalBelts.Contains(o.Belt ?? "") ? BoutStakes.Regional
        : BoutStakes.WorldTitle;

    /// <summary>Fight the bouts that were announced, and say a word about each.</summary>
    /// <summary>Where a man stands, for a bill: "champion", "#4", or nothing at all if he is not on the board.
    /// A card of unfamiliar names says nothing about who matters without it.</summary>
    private string RankLabel(Boxer b)
    {
        if (IsWorldChampion(b)) return "champion";
        int place = BoardPlace(b);
        return place > 0 ? $"#{place}" : "";
    }

    /// <summary>Fight the supporting bouts, in the order a hall actually fights them: from the FOOT of the
    /// bill upward. The openers box while the seats are still filling and the main event goes last, which is
    /// the whole shape of a fight night — _billed is stored biggest-first for the poster, so this walks it
    /// backwards.</summary>
    private void StageUndercard()
    {
        _undercard.Clear();
        for (int i = _billed.Count - 1; i >= 0; i--)
        {
            var (a, b, rounds) = _billed[i];
            if (a.Retired || b.Retired || !_medical.Available(a) || !_medical.Available(b)) continue;
            var res = FastBout(a, b, rounds);
            var night = ApplyOutcome(res, a, b);
            _undercard.Add(new UndercardBout(
                a.Name, b.Name,
                res.IsDraw ? null : res.Winner!.Name,
                res.Method, res.EndRound, rounds,
                UndercardNote(res, a, b, night)));
        }
    }

    /// <summary>A line about a supporting bout. Not commentary in the fight-night sense — the sentence a
    /// report gives a bout it has one line for, which is what these actually get.</summary>
    /// <summary>A line about a supporting bout — the sentence a report gives a fight it has one line for.
    ///
    /// It reads the BOUT rather than its result. The fast resolver already keeps a score for every round and
    /// three judges' cards, so whether a night was a shutout, a robbery, a war, or a man climbing off the
    /// floor to win is all sitting there in numbers nobody was reading. "Workmanlike from Fields" was true of
    /// every decision on every card.
    ///
    /// The variety is drawn from a random seeded on the BOUT — the two names and the night — rather than from
    /// the world's rng, for the same reason the venue pick is: a cosmetic sentence must not consume the
    /// simulation's randomness, or writing a better one shifts every result that comes after it.</summary>
    private string UndercardNote(FightResult res, Boxer a, Boxer b, DateOnly on)
    {
        var pick = new Random(NoteSeed(a.Name, b.Name, on));

        if (res.IsDraw)
            return Spread(res) >= 4 ? "A draw that one judge did not see at all."
                 : pick.Next(2) == 0 ? "Nothing between them."
                 : "A draw, and few argued.";

        var w = res.Winner!; var l = res.Loser!;
        bool inside = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        bool upset = l.Overall > w.Overall + 6;

        bool winnerIsA = w.Id == a.Id;
        int downsAgainstWinner = winnerIsA ? res.KnockdownsA : res.KnockdownsB;
        int downsByWinner = winnerIsA ? res.KnockdownsB : res.KnockdownsA;

        if (upset && inside) return $"{Surname(l)} was not expected to lose, and not like that.";
        if (upset) return $"An upset on the undercard — {Surname(l)} had been the favourite.";

        if (inside)
        {
            if (downsAgainstWinner > 0) return $"{Surname(w)} was down himself before he stopped {Surname(l)} in {res.EndRound}.";
            if (res.EndRound <= 2) return $"{Surname(w)} did not need long.";
            if (TrailedAtTheHalf(res, winnerIsA)) return $"{Surname(w)} was behind before he took him out in {res.EndRound}.";
            return downsByWinner >= 3
                 ? $"{Surname(w)} put him down {downsByWinner} times before it was waved off."
                 : $"{Surname(w)} got him out of there in {res.EndRound}.";
        }

        // It went the distance, so the cards can speak.
        int margin = Margin(res);
        bool split = res.Method is "SD" or "MD";
        bool cameBack = TrailedAtTheHalf(res, winnerIsA);

        if (split && cameBack) return $"{Surname(w)} came from behind and just got it.";
        if (split) return "Close, and the hall did not agree with it.";
        if (cameBack) return $"{Surname(l)} led at halfway. {Surname(w)} took it away from him.";
        if (Shutout(res, winnerIsA)) return $"Every round to {Surname(w)}. {Surname(l)} never got into it.";
        if (downsAgainstWinner > 0) return $"{Surname(w)} climbed off the floor to win it.";
        if (downsByWinner > 0) return $"{Surname(w)} had him down and boxed the rest.";
        if (margin >= 8) return $"One-sided. {Surname(w)} won it as he liked.";
        if (margin <= 2) return "A point in it, and it could have gone either way.";

        int streak = WinStreak(w);
        if (streak >= 8) return $"{Surname(w)} keeps rolling — {streak} without defeat now.";

        return pick.Next(3) switch
        {
            0 => $"{Surname(w)} boxed his way through it.",
            1 => $"Workmanlike from {Surname(w)}.",
            _ => $"{Surname(w)} took the decision without much fuss.",
        };
    }

    /// <summary>The winning margin on the middle card, in points. Zero if it never reached the cards.</summary>
    private static int Margin(FightResult res)
    {
        if (res.Scorecards.Count == 0) return 0;
        var gaps = res.Scorecards.Select(c => Math.Abs(c.A - c.B)).OrderBy(x => x).ToList();
        return gaps[gaps.Count / 2];
    }

    /// <summary>How far apart the widest and narrowest judges were — a wide spread is a card nobody agreed on.</summary>
    private static int Spread(FightResult res)
    {
        if (res.Scorecards.Count == 0) return 0;
        var diffs = res.Scorecards.Select(c => c.A - c.B).ToList();
        return diffs.Max() - diffs.Min();
    }

    /// <summary>Was the eventual winner behind at halfway? The round-by-round is kept even by the fast
    /// resolver, so a fight that turned is knowable rather than guessed at.</summary>
    private static bool TrailedAtTheHalf(FightResult res, bool winnerIsA)
    {
        if (res.Rounds.Count < 4) return false;
        int half = res.Rounds.Count / 2, mine = 0, his = 0;
        for (int i = 0; i < half; i++)
        {
            var r = res.Rounds[i];
            mine += winnerIsA ? r.ScoreA : r.ScoreB;
            his += winnerIsA ? r.ScoreB : r.ScoreA;
        }
        return his > mine;
    }

    /// <summary>Every round to the winner.</summary>
    private static bool Shutout(FightResult res, bool winnerIsA)
    {
        if (res.Rounds.Count < 4) return false;
        foreach (var r in res.Rounds)
            if ((winnerIsA ? r.ScoreA : r.ScoreB) <= (winnerIsA ? r.ScoreB : r.ScoreA)) return false;
        return true;
    }

    /// <summary>A seed belonging to this bout and no other, so the same night always reads the same way and
    /// the world's rng is never touched to say it.</summary>
    private static int NoteSeed(string x, string y, DateOnly on)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in x) h = h * 31 + c;
            foreach (char c in y) h = h * 31 + c;
            return (h * 31 + on.DayNumber) & 0x7FFFFFFF;
        }
    }

    private static string Surname(Boxer b)
    {
        int sp = b.Name.LastIndexOf(' ');
        return sp > 0 ? b.Name[(sp + 1)..] : b.Name;
    }

    /// <summary>Tonight's bill, top to bottom, with the player's own fight in its place. Available from the
    /// moment the offer is made — and carrying the results once they exist.</summary>
    public IReadOnlyList<BillLine> Bill
    {
        get
        {
            if (Offer is not { } o || Hall is null) return Array.Empty<BillLine>();

            // The bill reads as a poster does: biggest fight at the top, down to the opener. _billed is
            // already in that order, and the player's own fight goes in at the place its value earned.
            int over = Math.Clamp(_playerSlot, 0, _billed.Count);

            BillLine Support(int i)
            {
                var (a, b, r) = _billed[i];
                var done = _undercard.FirstOrDefault(u => u.A == a.Name && u.B == b.Name);
                return new BillLine($"{a.Name} vs {b.Name}", r, false, "",
                                    done?.Verdict ?? "", done?.Line ?? "", done?.Note ?? "",
                                    a.Name, a.Country ?? "", a.Record.ToString(),
                                    b.Name, b.Country ?? "", b.Record.ToString(),
                                    IsTitle: false, Div: a.WeightClass,
                                    ARank: RankLabel(a), BRank: RankLabel(b));
            }

            var all = new List<BillLine>();
            for (int i = 0; i < over && i < _billed.Count; i++) all.Add(Support(i));
            all.Add(new BillLine($"{Player.Name} vs {o.Opponent.Name}", o.Rounds, true,
                                 o.TitleFight ? $"{o.Belt} title" : o.Context, "", "", "",
                                 Player.Name, Player.Country ?? "", Player.Record.ToString(),
                                 o.Opponent.Name, o.Opponent.Country ?? "", o.Opponent.Record.ToString(),
                                 o.TitleFight,
                                 Div: Player.WeightClass,
                                 ARank: RankLabel(Player), BRank: RankLabel(o.Opponent)));
            for (int i = over; i < _billed.Count; i++) all.Add(Support(i));

            // Name the places on the card. Only the ones a poster would actually print.
            for (int i = 0; i < all.Count; i++)
            {
                string slot = i == 0 ? "MAIN EVENT"
                            : i == 1 && all.Count > 2 ? "CHIEF SUPPORT"
                            : i == all.Count - 1 && all.Count > 2 ? "OPENER"
                            : "";
                if (slot.Length > 0) all[i] = all[i] with { Slot = slot };
            }
            return all;
        }
    }

    /// <summary>The line under the poster. The hall is already named in the header above it, so this says
    /// what the night MEANS rather than repeating where it is.</summary>
    public string CardNote
    {
        get
        {
            if (Offer is null || Hall is null) return "";
            return (Tier, Billing) switch
            {
                (CardTier.Championship, Billing.MainEvent) =>
                    "A world title. This is the night everything was for.",
                (CardTier.Championship, Billing.Opener) =>
                    "You open a world championship bill, boxing while the hall fills up. Nobody remembers the "
                    + "opener. They do remember who was on the card.",
                (CardTier.Championship, _) =>
                    "A world title on top, and you are on the undercard of it — the biggest night of your life so far.",
                (CardTier.National, Billing.MainEvent) =>
                    "You headline. The country is watching this one.",
                (CardTier.National, _) =>
                    "A national card, and you are on it. A long way from the small halls.",
                (CardTier.Regional, Billing.MainEvent) =>
                    "Top of the bill. Your name is the one on the poster.",
                (CardTier.Regional, _) =>
                    "A decent local card, and a decent slot on it.",
                (_, Billing.MainEvent) =>
                    "A small hall, a few hundred people, and your name at the top of it.",
                _ =>
                    "A club show — six rounds, half a hall, and everyone here to see somebody else.",
            };
        }
    }

    /// <summary>The man whose career runs alongside the player's.
    ///
    /// A division was a table that reshuffled. Fifteen names moved up and down it and not one of them was
    /// anybody - there was no man you were measured against, no name you looked for first in the results.
    /// Every sport has one, and boxing more than most: the other guy in your weight, at your stage, who
    /// keeps winning while you keep winning.
    ///
    /// He is chosen by what actually makes a rivalry - a man who has beaten you outranks everything, then a
    /// man you have beaten who is still climbing, then simply the best fighter at your own stage of a career
    /// in your own division. He is re-read as the sport moves, so when he retires or falls away, somebody
    /// else becomes the man to watch.</summary>
    public Boxer? Rival
    {
        get
        {
            if (Player.Retired) return null;
            var here = ActiveIn(Player.WeightClass).Where(b => b.Id != Player.Id && !b.Retired).ToList();
            if (here.Count == 0) return null;

            // A man who beat you and is still going. This one needs no seniority — the first loss of a
            // career makes a rivalry on its own, whenever it comes. Most recent first: the freshest wound.
            foreach (var h in Enumerable.Reverse(Player.History))
                if (h.Result == 'L' && here.FirstOrDefault(b => b.Name == h.Opponent) is Boxer beat)
                    return beat;

            // Otherwise there is nobody yet, and the honest answer is to say so.
            //
            // The first cut named a rival from the debut — some 6-0 prospect the sim had decided was "the
            // man to watch" because he happened to be the same age. That is not a rivalry, it is a stranger
            // with a similar record, and putting his name at the top of the dashboard from fight one cheapens
            // the thing entirely. A rivalry is either somebody who has beaten you or somebody you are
            // actually racing, and you cannot be racing anyone until you are far enough up to be in the race.
            if (!WorldRanked(Player)) return null;
            var order = RankingOf(Player.WeightClass, 15);
            int mine = order.ToList().FindIndex(b => b.Id == Player.Id);
            if (mine < 0) return null;

            // The nearest ranked man to him, above or below — the one he is actually measured against.
            return order.Where(b => b.Id != Player.Id)
                        .OrderBy(b => Math.Abs(order.ToList().FindIndex(x => x.Id == b.Id) - mine))
                        .FirstOrDefault();
        }
    }

    /// <summary>Where the rival stands, in a line: his record, and whether he is above or below the player.</summary>
    public string RivalStanding(Boxer r)
    {
        var order = RankingOf(Player.WeightClass, 15).Select(b => b.Name).ToList();
        int his = order.IndexOf(r.Name), mine = order.IndexOf(Player.Name);
        string rank = his >= 0 ? $"#{his + 1}" : "unranked";
        if (his >= 0 && mine >= 0)
            return his < mine ? $"{rank} — {mine - his} place{(mine - his == 1 ? "" : "s")} above you"
                 : $"{rank} — {his - mine} place{(his - mine == 1 ? "" : "s")} below you";
        if (his >= 0 && mine < 0) return $"{rank}, and you are not ranked yet";
        return rank;
    }

    /// <summary>Why he is the one being watched — beaten you, or climbing beside you.</summary>
    public string RivalReason(Boxer r) =>
        Player.History.Any(h => h.Result == 'L' && h.Opponent == r.Name) ? "he beat you"
        : Player.History.Any(h => h.Result == 'W' && h.Opponent == r.Name) ? "you have beaten him"
        : "he is next to you in the rankings";


    /// <summary>Take the current offer: the calendar rolls forward to fight night, then the bout is fought.</summary>
    public FightResult? TakeOffer()
    {
        if (Offer is null || Player.Retired) return null;
        PlayerInjury = null;                         // he's fit again by fight night
        AdvanceTo(OfferDate);                       // run the world's fortnightly cards up to fight night
        if (Player.Retired) { Offer = null; return null; }

        var opp = Offer.Opponent;
        string? belt = Offer.Belt;
        string? note = belt is not null ? $"{belt} title"
                     : Offer.Context is "eliminator" ? "eliminator"
                     : Offer.Context.StartsWith("rematch") ? "rematch"
                     : null;
        StageUndercard();                           // the rest of the show, fought before he walks out
        var res = _engine.Simulate(Player, opp, Offer.Rounds);
        _declined.Clear();
        ClearRematch(Player, opp);          // whatever it was, it has now been settled once more
        ApplyOutcome(res, Player, opp, note);

        string verb = res.IsDraw ? "drew with" : (res.Winner!.Id == Player.Id ? "beat" : "lost to");
        string how = res.IsDraw ? Offer.Rounds + "-round draw" : res.Method;
        LogEvent($"{Player.Name} {verb} {opp.Name} ({how}){(belt is not null ? $" — {belt} TITLE" : "")}", playerBout: true,
                 bout: res.Winner is null ? null : new BoutRef(res.Winner.Name, res.Loser!.Name, Date));

        if (belt == UndisputedBelt && !res.IsDraw)
        {
            // Both world belts rode on this one. Win = defend both; loss = the challenger takes the lot.
            bool playerWon = res.Winner!.Id == Player.Id;
            if (playerWon)
            {
                Defended(Player.WeightClass, "WBA", Player.Id); Defended(Player.WeightClass, "WBC", Player.Id);
                foreach (var bl in new[] { PrimaryBelt, "WBC" }) { var r = OpenReign(bl); if (r is not null) r.Defenses++; }
            }
            else
            {
                SetBeltHolder(PrimaryBelt, opp); SetBeltHolder("WBC", opp);
                foreach (var bl in new[] { PrimaryBelt, "WBC" }) { var r = OpenReign(bl); if (r is not null) r.Lost = Date; }
                LogEvent($"{Player.Name} loses the unified {PrimaryBelt} and WBC titles to {opp.Name}.", true);
            }
        }
        else if (belt is not null && !res.IsDraw)
        {
            bool playerWon = res.Winner!.Id == Player.Id;
            bool held = PlayerHolds(belt);
            if (playerWon && !held)
            {
                SetBeltHolder(belt, Player);
                _titles.BeginReign(belt, Date);
                LogEvent($"{Player.Name} WINS THE {belt} TITLE, beating {opp.Name}!", true);
            }
            else if (playerWon && held)
            {
                Defended(Player.WeightClass, belt, Player.Id);
                var r = OpenReign(belt); if (r is not null) r.Defenses++;   // successful defence
            }
            else if (!playerWon && held)
            {
                SetBeltHolder(belt, opp);
                var r = OpenReign(belt); if (r is not null) r.Lost = Date;
                LogEvent($"{Player.Name} loses the {belt} title to {opp.Name}.", true);
            }
        }
        if (belt is not null) _lastTitleShot = ProFights(Player);   // start the rebuild clock before the next title bout

        // A serious injury keeps him on the shelf — his next fight is pushed out to recovery.
        var inj = res.Injuries.Where(i => i.Name == Player.Name).OrderByDescending(i => i.LayoffDays).FirstOrDefault();
        if (inj is not null)
        {
            LogEvent($"{Player.Name} suffered {inj.Type} — {LayoffText(inj.LayoffDays)}.", playerBout: true);
            if (inj.Retires) { Player.Retired = true; LogEvent($"{Player.Name} is forced to retire on medical advice.", playerBout: true); }
            else { PlayerInjury = inj; _layoffDays = inj.LayoffDays; }
        }

        // The bill as it stood tonight, results and all, captured before the next offer replaces it.
        //
        // Bill is derived from the CURRENT offer and the undercard drawn alongside it, and the assignment
        // below draws a fresh card for the next fight — so the moment that line runs, tonight's supporting
        // bouts and what became of them are gone for good. Anything that wants to show the night after the
        // event has to read it from here.
        LastNightsCard = Bill.ToList();

        NewSlate();
        return res;
    }

    /// <summary>Turn the offer down and wait — the calendar still moves and a new offer comes in.</summary>
    /// <summary>Turn a fight down. The man you passed on is remembered, so holding out gets you a DIFFERENT
    /// name rather than the same one again: a fighter who has said no does not keep getting the same offer
    /// from the same matchmaker week after week.</summary>
    public void DeclineOffer()
    {
        if (Player.Retired) return;
        if (Offer is { } turned)
        {
            _declined.Add(turned.Opponent.Id);
            while (_declined.Count > 4) _declined.RemoveAt(0);   // he comes back round eventually
            // Turning down a world title is not the same as turning down a Tuesday in Bethnal Green.
            if (turned.TitleFight && turned.Belt is "WBA" or "WBC" or "IBF") ForfeitShot();
        }
        AdvanceTo(Date.AddDays(21 + _rng.Next(21)));
        NewSlate();
    }

    // Men the player has recently turned down. Cleared when he actually takes a fight - once he is boxing
    // again the matchmaker has no reason to keep steering round them.
    private readonly List<int> _declined = new();

    /// <summary>Give up the WBC belt rather than defend it — the senior belt (and that reign) stays intact.
    /// Only meaningful for a unified champion; the vacant WBC passes to the leading contender.</summary>
    public void RelinquishWbc()
    {
        if (WbcChampion?.Id != Player.Id) return;
        var r = OpenReign("WBC"); if (r is not null) r.Lost = Date;
        _titles.SetWbc(Division, null);
        LogEvent($"{Player.Name} relinquishes the WBC title.", true);
        UpdateBeltsFor(Division);
        if (!Player.Retired) NewSlate();   // the offer is no longer a unified defence
    }

    /// <summary>The division the player could move up to (null at heavyweight; skips not-yet-founded classes).</summary>
    public WeightClass? NextDivision => NextActiveUp(Player.WeightClass);
    public bool CanMoveUp => NextDivision is not null && !Player.Retired;

    /// <summary>Campaign up a weight: keep the record, rebalance for bigger men, vacate belts and reigns in
    /// the old division, and start fresh (unranked) in the new one.</summary>
    public void MoveUp()
    {
        if (NextDivision is not WeightClass to || Player.Retired) return;
        var from = Player.WeightClass;
        _titles.EndOpenReigns(Date);   // old-division reigns end
        MoveUpTo(Player, to);
        Player.IsChampion = false;
        _lastTitleShot = -100;
        UpdateBeltsFor(from);   // the belts he vacated pass on
        LogEvent($"{Player.Name} moves up to the {to.DisplayName()} division.", true, kind: "title");
        NewSlate();
    }

}
