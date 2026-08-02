using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Who the player is offered, and why — the experience ladder, the guards that keep him off the
/// division's live wires, and the belts he is allowed to chase.</summary>
public sealed partial class CareerGame
{
    // ---- matchmaking for the player ----

    /// <summary>A world title shot he has been GRANTED: which belt, and whose it was when it was ordered.
    ///
    /// A shot used to be re-decided every time the matchmaker was asked — the gates are deterministic, so the
    /// same offer came back and it worked, but it meant turning a belt down cost nothing at all. You could
    /// pass on the champion, wait three weeks, and be offered him again.
    ///
    /// Held as a fact about the world instead. A sanctioning body has ordered a fight; if you refuse it, they
    /// order somebody else, and you go to the back of the queue for a few fights the way you would if you had
    /// taken it and lost. Persisted, or quitting without saving would put the belt back on the table.</summary>
    private sealed record TitleShot(string Belt, int ChampionId, int GrantedAtFights);
    private TitleShot? _shot;

    /// <summary>The granted shot, if it is still a real fight: the man must still be active and still hold the
    /// belt he was carrying when it was ordered. A champion who has lost it in the meantime takes the
    /// opportunity down with him rather than handing it on — the shot was at HIM.</summary>
    private (string Belt, Boxer Champion)? LiveShot()
    {
        if (_shot is not { } s) return null;
        var champ = _roster.FirstOrDefault(b => b.Id == s.ChampionId);
        if (champ is null || champ.Retired || champ.Id == Player.Id
            || champ.WeightClass != Player.WeightClass || BeltHolder(s.Belt)?.Id != champ.Id)
        {
            _shot = null;
            return null;
        }
        return (s.Belt, champ);
    }

    private FightOffer Grant(string belt, Boxer champ)
    {
        _shot = new TitleShot(belt, champ.Id, ProFights(Player));
        // A division has one champion, so once the rebuild is served the man at the end of it is often the same
        // man — and calling that "WBA title shot" tells the player nothing about the only thing that makes it
        // worth taking. It is a return with the belt on it, and it says so. Same truth as DefenceContext, read
        // from the challenger's side.
        bool again = RecentFoes(Player, 3).Contains(champ.Name);
        return new FightOffer { Opponent = champ, Rounds = 12, TitleFight = true, Belt = belt,
                                Context = again ? $"rematch — the {belt} title, and it is his" : $"{belt} title shot" };
    }

    /// <summary>He turned down a belt. The sanctioning body does not hold it open: the champion is ordered to
    /// defend against somebody else, and the player waits the same few fights he would have waited had he
    /// taken the shot and lost it. This is the rule that makes refusing a title fight a decision.</summary>
    private void ForfeitShot(string belt, Boxer? champion)
    {
        // The rebuild is owed for REFUSING A BELT, not for refusing a granted one. This used to return early
        // when no shot had been formally granted — and a title fight can reach the player without one, because
        // a rematch with a champion is offered as itself and carries the belt with it. Turn that down and it
        // cost nothing at all: the cooldown was never set, so the same championship came back on the next
        // slate, and the next. The rule this whole mechanism exists to enforce had a door in it.
        var named = _shot is { } s ? _roster.FirstOrDefault(b => b.Id == s.ChampionId) : null;
        champion ??= named;
        _shot = null;
        _lastTitleShot = ProFights(Player);   // the same rebuild gap as if he had taken it
        LogEvent(champion is null
                     ? $"{Player.Name} passes on the {belt} title fight. The shot goes elsewhere."
                     : $"{Player.Name} passes on {champion.Name}. The {belt} orders him to defend against somebody else.",
                 playerBout: true, kind: "title");
    }

    /// <summary>How many fights are put in front of the player at once.
    ///
    /// One was the old behaviour: a single offer, take it or wait, so the only decision available was whether
    /// to fight at all. Three makes the choice the thing a career actually is — a tune-up, a step up, or the
    /// man who will hurt you but move you up the board.
    ///
    /// A night with a belt on it does NOT get buried under alternatives. When BigNight fills the slot the
    /// slate is narrowed, because a world title is not one option among three.</summary>
    private const int SlateWidth = 3;
    private const int SlateWidthWithABelt = 2;

    /// <summary>The fights on the table. Exactly one of them may be a belt.</summary>
    private readonly List<FightOffer> _slate = new();
    public IReadOnlyList<FightOffer> Slate => _slate;

    /// <summary>Draw up this cycle's choices and put the first on the table.
    ///
    /// The scarcity rules have to hold ACROSS the slate, which is why one function sees all of it rather than
    /// the matchmaker being called three times: called three times, a top-five contender is handed three
    /// world title shots on one screen and a belt stops being scarce. BigNight fills at most one slot and the
    /// rest come from the ordinary pool, which is never allowed to contain a title.
    ///
    /// Only the CHOSEN offer is assigned to Offer, because assigning it draws the card and the undercard —
    /// three offers must not draw three bills. See the note on the Offer setter.</summary>
    private void NewSlate(bool evenIfRetired = false)
    {
        _slate.Clear();
        if (Player.Retired && !evenIfRetired) { Offer = null; return; }
        foreach (var o in BuildSlate()) _slate.Add(o);
        Offer = _slate.Count > 0 ? _slate[0] : null;
    }

    /// <summary>Take one of the fights on the table. The others were never made.</summary>
    public void ChooseOffer(FightOffer o)
    {
        if (!_slate.Contains(o)) return;
        Offer = o;
    }

    /// <summary>What a fight on the slate is worth — the same measure the card uses to decide its running
    /// order. Exposed so the choice can be described in terms of what each night IS, rather than leaving the
    /// player to compare two records of men he has never heard of.</summary>
    public double ValueOf(FightOffer o) =>
        BoutValue.Of(Player, o.Opponent, StakesOf(o), Player.WeightClass,
                     ValuePlace(Player), ValuePlace(o.Opponent));

    /// <summary>Where a man stands on his division's board, or 0. For the UI, which cannot see the ranking
    /// internals but has to say "#4" beside a name for it to mean anything.</summary>
    public int PlaceOf(Boxer b) => BoardPlace(b);

    /// <summary>What a defence IS, in the line the player reads.
    ///
    /// PickChallenger's first move is to hand back a man the champion owes a return — a split card, a night he
    /// was dropped and got up — and it deliberately walks past the rule that says "not a man he has just
    /// fought", because settling that is the whole point. The offer then went out labelled "WBA title defence"
    /// with no hint of why the same face was in front of him a fight later. The reason is the interesting part
    /// of the fight; a card that will not say it reads like the matchmaker repeating himself.</summary>
    private string DefenceContext(string plain, Boxer challenger) =>
        RematchFoeFor(Player)?.Id == challenger.Id
            ? $"rematch — {RematchWhy(Player, challenger)}, and the belt is on it"
            : plain;

    /// <summary>This cycle's choices. At most one of them can be a belt.</summary>
    private List<FightOffer> BuildSlate()
    {
        var slate = new List<FightOffer> { BuildOffer() };

        // BuildOffer already put the big night up if he had earned one — a belt he holds, a fight he is owed,
        // a shot he has qualified for, his region's title. That slot is now FULL, and nothing below may add
        // another: the ordinary path cannot produce a title, so the cap is structural rather than a check.
        bool belt = slate[0].TitleFight;
        int want = belt ? SlateWidthWithABelt : SlateWidth;

        // The rest of the pool, minus anyone already on the table. Each is a genuinely different night rather
        // than the same man described twice.
        var taken = new HashSet<int> { slate[0].Opponent.Id };
        var ranked = Active.OrderByDescending(RankScore).ToList();
        int idx = ranked.FindIndex(b => b.Id == Player.Id);
        if (idx < 0 || ranked.Count <= 2) return slate;

        int proFights = ProFights(Player);
        int maxOvr = CeilingFor(proFights);

        for (int i = 0; slate.Count < want && i < 6; i++)
        {
            var alt = OrdinaryOffer(ranked, idx, proFights, maxOvr, taken);
            if (taken.Contains(alt.Opponent.Id)) continue;
            taken.Add(alt.Opponent.Id);
            slate.Add(alt);
        }
        return slate;
    }

    /// <summary>The best man his record has earned. The ceiling RAMPS with experience and never jumps
    /// straight to "anyone": a green fighter spends a dozen bouts on tomato cans before real opposition, and
    /// graduating the apprenticeship earns him contenders rather than an all-time great — a 14-fight novice
    /// offered a class-14 champion is not a step up, it is a mismatch. Title shots bypass it entirely, being
    /// earned by ranking instead. Rolled per offer, so two nights on the same slate can be pitched
    /// differently.</summary>
    private int CeilingFor(int proFights) =>
        !ReadyForContenders(Player)
            ? (proFights < 12 ? 55
               : _rng.Next(2) == 0 ? 55                           // ~half the step-up bouts are jman tune-ups
                                   : Math.Min(78, 55 + (proFights - 12) * 3))   // gatekeeper, not contender
            : Math.Min(99, 70 + (proFights - ContenderApprenticeship(Player)) * 3);

    private FightOffer BuildOffer()
    {
        int gap = Math.Max(DaysForFights(ProFights(Player)), _layoffDays);   // recovery pushes the next bout out
        // A world champion fights on a title schedule — long camps, ~3 defences a year — not a
        // club-show frequency, however young he is.
        // A champion fights on a title schedule; a former champion still picks his nights. Both wait longer
        // between bouts than a contender does.
        if (Player.IsChampion || WbcChampion?.Id == Player.Id || IbfChampion?.Id == Player.Id)
            gap = Math.Max((int)Math.Round(gap * 1.35), (int)Math.Round(112 * (0.5 + _rng.NextDouble())));
        else if (_hall.WasEverChampion(Player.Id))
            gap = (int)Math.Round(gap * 1.18);
        _layoffDays = 0;
        OfferDate = Date.AddDays(gap);
        // Hard cap: no more than 8 bouts in a calendar year. Once he's had his eight, the next one waits for the new year.
        if (FightsThisYear(Player) >= MaxFightsPerYear && OfferDate.Year == Date.Year)
            OfferDate = new DateOnly(Date.Year + 1, 1, 12 + _rng.Next(24));
        var ranked = Active.OrderByDescending(b => RankScore(b)).ToList();   // index 0 = strongest
        int idx = ranked.FindIndex(b => b.Id == Player.Id);
        if (ranked.Count <= 1) return new FightOffer { Opponent = _factory.CreateProspect(Player.WeightClass, GeneratedCap, Year), Rounds = 6, Context = "stay-busy" };

        int proFights = ProFights(Player);
        // Build a career properly: journeyman fodder (class 1–3) for the first stretch, then a step-up phase that
        // MIXES gatekeepers with journeyman tune-ups (a stepping-up prospect still stays busy against tomato cans
        // between the tougher fights), and only the elite once he's served his apprenticeship (short for a wonder
        // kid). So a green fighter spends a dozen bouts on tomato cans before real opposition, like a real prospect.
        // The ceiling RAMPS with experience and never jumps straight to "anyone". Graduating the apprenticeship
        // earns a prospect real contenders, not an all-time great: a 14-fight novice offered a class-14 champion
        // isn't a step up, it's a mismatch. (Title shots bypass this cap entirely — they're earned by ranking.)
        int maxOvr = CeilingFor(proFights);

        if (BigNight(ranked, idx, proFights) is FightOffer big) return big;
        return OrdinaryOffer(ranked, idx, proFights, maxOvr);
    }

    /// <summary>The one fight that matters this cycle, if he has earned one — or null, and the matchmaker goes
    /// back to building his record.
    ///
    /// A priority chain, first match wins: a belt he holds, then the fight he is owed, then the belt he has
    /// earned a crack at, then his region's. Lifted out of BuildOffer unchanged, because it is the part that
    /// must NOT become a score. Everything here is gated on things he did — a ranking, an apprenticeship, a
    /// rebuild since the last one — and a scoring pass would turn a title shot from something earned into
    /// something drawn. Scoring belongs to picking between ordinary opponents; this is about entitlement.</summary>
    private FightOffer? BigNight(List<Boxer> ranked, int idx, int proFights)
    {
        bool holdsWba = Player.IsChampion;
        bool holdsWbc = WbcChampion?.Id == Player.Id;
        bool holdsIbf = IbfChampion?.Id == Player.Id;

        // Defend a belt I already hold. Holding both senior belts means a single unified defence.
        if (holdsWba && holdsWbc)
        {
            var chall = PickChallenger(Player, null)   // a top-10 contender he hasn't just fought
                     ?? ranked.FirstOrDefault(b => b.Id != Player.Id);
            if (chall is not null)
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = UndisputedBelt,
                                        Context = DefenceContext("undisputed title defence", chall) };
        }
        else if (holdsWba || holdsWbc || holdsIbf)
        {
            string belt = holdsWba ? PrimaryBelt : holdsWbc ? "WBC" : "IBF";
            var chall = PickChallenger(Player, null)   // a top-10 contender he hasn't just fought
                     ?? ranked.FirstOrDefault(b => b.Id != Player.Id);
            if (chall is not null)
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = belt,
                                        Context = DefenceContext($"{belt} title defence", chall) };
        }

        // The fight he is owed, or owes. A draw, a split card, a cut that stopped it, a night he was beaten by
        // somebody nobody rated - the public wants it settled, and it is offered to him before the matchmaker
        // goes back to building his record. He can still turn it down; it stays wanted.
        // After a title bout, rebuild with a few wins before the next shot — no back-to-back challenges.
        bool titleCooldownOk = proFights - _lastTitleShot >= 3;

        if (RematchFoeFor(Player) is Boxer again)
        {
            // A rematch with a champion is for the belt - it is the same fight, and the belt was on it.
            string? belt = Champion?.Id == again.Id ? PrimaryBelt
                         : WbcChampion?.Id == again.Id ? "WBC"
                         : IbfChampion?.Id == again.Id ? "IBF" : null;
            // WHICH MAKES IT A TITLE SHOT, and the rules a title shot lives under apply to it. This branch sat
            // above them and answered to none: refuse a world title and the same champion came back on the
            // very next slate as "the fight you are owed", so the forfeit that exists to make refusing a belt
            // cost something cost nothing at all. He is not offered a man he has just turned down either —
            // the ordinary path has always steered round _declined, and this walked straight through it.
            bool blocked = belt is not null && (!titleCooldownOk || _declined.Contains(again.Id));
            if (!blocked)
                return new FightOffer
                {
                    Opponent = again,
                    Rounds = belt is not null || WorldRanked(again) ? 12 : 10,
                    TitleFight = belt is not null,
                    Belt = belt,
                    Context = $"rematch — {RematchWhy(Player, again)}"
                };
        }

        // A title shot is earned by being world-ranked (top 5) with a real body of work behind you:
        // an exceptional talent breaks in after 20 fights, an ordinary fighter needs 24. (Champions are
        // 80+, so this never lets a novice near the belt inside his first 20 bouts.)
        int fightsToRank = ContenderApprenticeship(Player) switch { 12 => 14, 15 => 17, _ => Player.Potential >= 85 ? 20 : 24 };
        // A man who has held a world belt has served it. The count is here to keep a green fighter out of a
        // championship ring and nothing else, and he has already been in one — which mattered most to the very
        // fighter it was never meant to catch: moving up rebalances a man for the heavier weight and then resets
        // his potential to the reduced figure, so a champion could cross a division and land UNDER the 85 that
        // buys the shorter apprenticeship. He then owed twenty-four fights to challenge for a belt he had been
        // wearing a month earlier, and the tune-up count everyone was looking at had nothing to do with it.
        bool servedHisApprenticeship = proFights >= fightsToRank || _hall.WasEverChampion(Player.Id);
        if (idx >= 0 && idx <= 4 && servedHisApprenticeship && titleCooldownOk && !RecentlyMovedUp(Player))
        {
            // The opportunity he already has, if it is still real, before any new one is minted. A shot is a
            // fact about the world — a sanctioning body has ordered a fight — and not a thing re-decided every
            // time the matchmaker is asked a question. See TitleShot.
            if (LiveShot() is (string heldBelt, Boxer heldChamp))
                return new FightOffer { Opponent = heldChamp, Rounds = 12, TitleFight = true, Belt = heldBelt,
                                        Context = $"{heldBelt} title shot" };

            if (!holdsWba && Champion is not null && Champion.Id != Player.Id) return Grant(PrimaryBelt, Champion);
            if (!holdsWbc && WbcChampion is not null && WbcChampion.Id != Player.Id) return Grant("WBC", WbcChampion);
            if (IbfActive && !holdsIbf && IbfChampion is not null && IbfChampion.Id != Player.Id) return Grant("IBF", IbfChampion);
        }

        var stage = CareerStages.Of(Player);

        // Regional title — a stepping stone before world level. A ranked contender who isn't yet a
        // top-5 world man goes after (or defends) his region's belt.
        var region = RegionOf(Player);
        if (region is not null && proFights >= fightsToRank && idx > 4 && idx <= 25
            && titleCooldownOk && stage is CareerStage.PrePrime or CareerStage.Prime && _rng.Next(2) == 0)
        {
            // A regional belt is a stepping stone, and the guards that keep a man off the division's live
            // wires apply to it exactly as they do anywhere else. This path used to bypass them entirely,
            // which is how a 17-0 novice came to be offered a NABF title fight with an unbeaten 14-0
            // class-11 fighter. Somebody's future all-time great is not a stepping stone.
            if (PlayerHolds(region))   // defend the regional belt against a fellow regional contender
            {
                var chall = ranked.FirstOrDefault(b => b.Id != Player.Id && RegionOf(b) == region
                                                    && !IsWorldChampion(b) && !DangerousProspect(b));
                if (chall is not null)
                    return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = region, Context = $"{region} title defence" };
            }
            else                       // or challenge for it as a stepping stone to world level
            {
                var rc = BeltHolder(region);
                if (rc is not null && rc.Id != Player.Id && RegionOf(rc) == region && !DangerousProspect(rc))
                    return new FightOffer { Opponent = rc, Rounds = 12, TitleFight = true, Belt = region, Context = $"{region} title shot" };
            }
        }

        return null;
    }

    /// <summary>An ordinary night: who he is matched with when there is no belt in it.
    ///
    /// This used to pick one man off the ranking and then rewrite that choice through six guard passes, each
    /// re-running the search with a different filter. The passes could not see each other, so they undid each
    /// other's work — a comment on the last of them recorded two that did it outright, "the top-15 guard picked
    /// a champion, and the champion guard picked a top-15 man" — and the answer depended on the order they
    /// happened to be written in. Adding a seventh rule meant reasoning about six invisible predecessors.
    ///
    /// It is a scoring problem, and it is written as one now:
    ///
    ///   1. Decide what KIND of night this is. That is a real roll and stays a roll.
    ///   2. Throw out everyone he must not be matched with. Hard rules are conjunctive, so they cannot fight.
    ///   3. Score everyone left against the night we are making, and take the best.
    ///
    /// Every rule carries its own reason, which is what makes a wrong-looking matchup answerable: you can ask
    /// why a man was refused instead of reasoning backwards through the passes that might have swapped him.</summary>
    /// <param name="alreadyOffered">Men already on this cycle's slate. Two offers naming the same opponent is
    /// not a choice.</param>
    private FightOffer OrdinaryOffer(List<Boxer> ranked, int idx, int proFights, int maxOvr,
                                     HashSet<int>? alreadyOffered = null)
    {
        var stage = CareerStages.Of(Player);

        // 1. WHAT KIND OF NIGHT. A prospect is fed beatable opposition so he can build a record; a contender
        //    takes mostly seasoned tune-ups with roughly one clash in three. The roll is unchanged.
        int target = stage switch
        {
            CareerStage.Starter => idx + 3 + _rng.Next(5),   // clearly weaker — build the record
            CareerStage.PrePrime => idx + 1 + _rng.Next(3),  // a touch below — keep winning, learn
            // Top contenders don't meet each other every time out — roughly 1 in 3 is a real top-20
            // clash, the rest are stay-busy tune-ups against lower opposition.
            CareerStage.Prime => (idx < 20 && _rng.Next(3) != 0) ? idx + 3 + _rng.Next(8) : idx - 1 - _rng.Next(4),
            _ => idx - _rng.Next(3)                          // around his level late in the career
        };
        target = Math.Clamp(target, 0, ranked.Count - 1);
        if (target == idx) target = idx + 1 <= ranked.Count - 1 ? idx + 1 : idx - 1;
        int wantOvr = ranked[Math.Clamp(target, 0, ranked.Count - 1)].Overall;

        bool ranking = WorldRanked(Player);
        bool clash = ranking && idx >= 0 && idx <= 20 && _rng.Next(3) == 0;

        // 2. WHO HE MUST NOT BE MATCHED WITH. Conjunctive, so no rule can undo another, and each says why.
        //    Until he is world-ranked he is kept off the division's leading men BY RANKING as well as by
        //    rating: an unproven #1 is often a young fighter whose rating has not caught up.
        // THE SAME TOP FIFTEEN THE BOARD PRINTS. This built its own list — the contender-filtered ranking,
        // taken fifteen deep — while the board tops itself up with men who have eight bouts when the division
        // is thin on qualified contenders. So a man could be #4 on the rankings page and absent from the list
        // that is supposed to keep an unranked fighter away from him, and the two only ever agreed by luck.
        // One derivation, so "the division's leading men" means one thing.
        // AND IT KEYS OFF WHERE HE STANDS, not off how many times he has boxed. This lifted on WorldRanked,
        // which is twenty bouts — a body of work, not a standing. A man can have thirty fights and be fortieth
        // in his division, and for him the guard was simply off: he was matched with the leading men on the
        // strength of having turned up twenty times. He is on the board or he is not.
        HashSet<int> offLimits;
        if (BoardPlace(Player) > 0) offLimits = new HashSet<int>();
        else
        {
            var (champs, contenders) = BoardOf(Player.WeightClass, 15);
            offLimits = champs.Concat(contenders).Select(b => b.Id).ToHashSet();
        }
        var recent = RecentFoes(Player, 3);

        var hard = new (string Why, Func<Boxer, bool> No)[]
        {
            ("it is him",                 b => b.Id == Player.Id),
            ("a champion, and no belt",   IsWorldChampion),
            ("top of a division he has not earned", b => offLimits.Contains(b.Id)),
            ("over his ceiling",          b => b.Overall > maxOvr),
            ("he has just fought him",    b => recent.Contains(b.Name)),
            ("they have met three times", b => TimesFaced(b.Name) >= 3),
            ("somebody else's prospect",  b => !ranking && DangerousProspect(b)),
            ("already on the slate",      b => alreadyOffered?.Contains(b.Id) == true),
        };

        var pool = ranked.Where(b => !hard.Any(r => r.No(b))).ToList();
        // Nothing at all fits: relax the preferences, never the two guards that exist to stop a mismatch.
        if (pool.Count == 0)
            pool = ranked.Where(b => b.Id != Player.Id && !IsWorldChampion(b) && !offLimits.Contains(b.Id)).ToList();
        // Still nothing. The last resort was "anyone at all", which walked straight through the guard the line
        // above had just promised not to relax — and that is how an unranked fighter came to be offered the
        // division's #4 as a record-builder. A journeyman nobody has heard of is a poor night; a mismatch the
        // matchmaker exists to prevent is a bug. So the guard holds and the sport finds him somebody.
        if (pool.Count == 0)
            return new FightOffer { Opponent = _factory.CreateProspect(Player.WeightClass, GeneratedCap, Year),
                                    Rounds = proFights <= 6 ? 6 : 8, Context = "stay-busy" };

        // 3. SCORE WHAT IS LEFT. Closeness to the night we are making, then the things that make a man the
        //    right OPPONENT rather than merely the right rating.
        int sofar = ProFights(Player);
        int wantFights = sofar <= 6 ? 8 : sofar <= 13 ? 10 : 12;
        double wantWinRate = sofar <= 6 ? 0.58 : sofar <= 13 ? 0.65 : 1.00;

        double Fitness(Boxer b)
        {
            double s = -Math.Abs(b.Overall - wantOvr) * 3.0;   // the fight we set out to make

            // A gatekeeper is a SEASONED fighter. A mid-rated man with a thin record is a rising prospect,
            // not a test, and putting the player in with him is somebody else's career being risked.
            if (b.Overall is > 55 and < 78) s += ProFights(b) >= 15 ? 25 : -35;

            if (ranking)
            {
                // A contender's schedule: never raw prospects, and the band depends on whether tonight is a
                // real clash or a stay-busy.
                if (IsProspect(b) || ProFights(b) < 15) s -= 60;
                s += clash ? (b.Overall >= 74 ? 45 : -20)
                           : (b.Overall is >= 58 and <= 76 ? 45 : -20);
            }
            else
            {
                // Before he is ranked the man opposite is a PROFESSIONAL OPPONENT: fights behind him, and a
                // record that says he loses. Measured once at 79% of a player's first fourteen opponents
                // carrying under a dozen bouts and only 17% a losing record — a parade of other people's
                // prospects, which is not how a record is built.
                int f = ProFights(b);
                s += f >= wantFights ? 30 : -50;
                int decided = b.Record.Wins + b.Record.Losses;
                if (decided > 0 && b.Record.Wins / (double)decided <= wantWinRate) s += 35;
            }
            return s;
        }

        // Best fit, with the near-misses shuffled so a division does not serve the same man every time his
        // rating happens to sit closest.
        var shortlist = pool.OrderByDescending(Fitness).Take(Math.Min(5, pool.Count)).ToList();
        var opp = shortlist[_rng.Next(shortlist.Count)];
        bool capped = opp.Overall < wantOvr - 6;   // he was steered down to something his record has earned

        // The distance a man is trusted with follows his mileage, the way a real career does: six-rounders to
        // begin with, then eight, and ten once he is established. It used to come off the career STAGE, which
        // gave six rounds until his ninth fight and then eight for everything up to about thirty - so a
        // twenty-one fight professional was still being matched over eight, with no way to a ten-rounder
        // unless he was already ranked in the top five.
        //
        // Six is the floor. A four-rounder is a novice show and there is nothing in one to watch - the card
        // is barely long enough for a fight to develop, and the sim's own commentary has no room to say
        // anything before the bell. Then eight, ten at a dozen fights where a man stops being a prospect on
        // a club show and starts headlining one, and the championship distance from nineteen.
        int had = ProFights(Player);
        int rounds = had <= 6 ? 6
                   : had <= 12 ? 8
                   : had <= 18 ? 10
                   : 12;
        string ctx = capped ? "building a record"
                   : target < idx ? (idx <= 5 ? "eliminator" : "step-up")
                   : stage == CareerStage.Starter || stage == CareerStage.PrePrime ? "building a record"
                   : "stay-busy";
        // A final eliminator at the top of the division is fought over the championship distance — the winner
        // is going straight to a title shot and has to prove he can last it.
        if (ctx == "eliminator") rounds = 12;
        return new FightOffer { Opponent = opp, Rounds = rounds, Context = ctx };
    }

}
