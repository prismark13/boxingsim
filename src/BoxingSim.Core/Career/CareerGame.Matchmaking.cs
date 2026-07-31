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

    private FightOffer BuildOffer()
    {
        int gap = Math.Max(DaysForFights(ProFights(Player)), _layoffDays);   // recovery pushes the next bout out
        // A world champion fights on a title schedule — long camps, ~3 defences a year — not a
        // club-show frequency, however young he is.
        // A champion fights on a title schedule; a former champion still picks his nights. Both wait longer
        // between bouts than a contender does.
        if (Player.IsChampion || WbcChampion?.Id == Player.Id || IbfChampion?.Id == Player.Id)
            gap = Math.Max((int)Math.Round(gap * 1.35), (int)Math.Round(112 * (0.5 + _rng.NextDouble())));
        else if (_everChampion.Contains(Player.Id))
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
        int maxOvr = !ReadyForContenders(Player)
                   ? (proFights < 12 ? 55
                      : _rng.Next(2) == 0 ? 55                           // ~half the step-up bouts are jman tune-ups
                                          : Math.Min(78, 55 + (proFights - 12) * 3))   // gatekeeper, not contender
                   : Math.Min(99, 70 + (proFights - ContenderApprenticeship(Player)) * 3);

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
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = UndisputedBelt, Context = "undisputed title defence" };
        }
        else if (holdsWba || holdsWbc || holdsIbf)
        {
            string belt = holdsWba ? PrimaryBelt : holdsWbc ? "WBC" : "IBF";
            var chall = PickChallenger(Player, null)   // a top-10 contender he hasn't just fought
                     ?? ranked.FirstOrDefault(b => b.Id != Player.Id);
            if (chall is not null)
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = belt, Context = $"{belt} title defence" };
        }

        // The fight he is owed, or owes. A draw, a split card, a cut that stopped it, a night he was beaten by
        // somebody nobody rated - the public wants it settled, and it is offered to him before the matchmaker
        // goes back to building his record. He can still turn it down; it stays wanted.
        if (RematchFoeFor(Player) is Boxer again)
        {
            // A rematch with a champion is for the belt - it is the same fight, and the belt was on it.
            string? belt = Champion?.Id == again.Id ? PrimaryBelt
                         : WbcChampion?.Id == again.Id ? "WBC"
                         : IbfChampion?.Id == again.Id ? "IBF" : null;
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
        // After a title bout, rebuild with a few wins before the next shot — no back-to-back challenges.
        bool titleCooldownOk = proFights - _lastTitleShot >= 3;
        if (idx >= 0 && idx <= 4 && proFights >= fightsToRank && titleCooldownOk && !RecentlyMovedUp(Player))
        {
            if (!holdsWba && Champion is not null && Champion.Id != Player.Id)
                return new FightOffer { Opponent = Champion, Rounds = 12, TitleFight = true, Belt = PrimaryBelt, Context = $"{PrimaryBelt} title shot" };
            if (!holdsWbc && WbcChampion is not null && WbcChampion.Id != Player.Id)
                return new FightOffer { Opponent = WbcChampion, Rounds = 12, TitleFight = true, Belt = "WBC", Context = "WBC title shot" };
            if (IbfActive && !holdsIbf && IbfChampion is not null && IbfChampion.Id != Player.Id)
                return new FightOffer { Opponent = IbfChampion, Rounds = 12, TitleFight = true, Belt = "IBF", Context = "IBF title shot" };
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
    /// Match by career stage. A prospect (starter/pre-prime) is fed beatable opposition so he can build a
    /// record — a higher target index is a LOWER-ranked, weaker opponent. Once he matures, he fights the men
    /// ranked above him.</summary>
    private FightOffer OrdinaryOffer(List<Boxer> ranked, int idx, int proFights, int maxOvr)
    {
        var stage = CareerStages.Of(Player);

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
        var opp = ranked[Math.Clamp(target, 0, ranked.Count - 1)];
        if (opp.Id == Player.Id) opp = ranked[Math.Max(0, idx - 1)];

        // Enforce the experience ceiling — a hot prospect who's ranked high won't be fed to the elite
        // before he's ready; instead he gets the best opponent his record has earned.
        bool capped = false;
        if (opp.Overall > maxOvr)
        {
            var pool = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr).ToList();
            if (pool.Count > 0) { opp = pool[_rng.Next(Math.Min(pool.Count, 5))]; capped = true; }
        }

        // Avoid a stale rematch — don't keep serving up a man he's just fought or has met many times.
        if (RecentFoes(Player, 3).Contains(opp.Name) || TimesFaced(opp.Name) >= 3)
        {
            var fresh = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr
                                       && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (fresh.Count > 0) opp = NearOne(fresh, opp.Overall);
        }

        // A gatekeeper is a SEASONED fighter (15+ bouts) — a mid-rated man with a thin record is a rising prospect,
        // not a test. If a gatekeeper-tier opponent (not an elite contender, and not a ranked man) is green, swap
        // him for an experienced fighter at a similar level, else drop back to a journeyman tune-up.
        if (opp.Overall is > 55 and < 78 && ProFights(opp) < 15 && !Top20Ids(Player.WeightClass).Contains(opp.Id))
        {
            var seasoned = ranked.Where(b => b.Id != Player.Id && b.Id != opp.Id && b.Overall <= maxOvr
                                          && (ProFights(b) >= 15 || b.Overall <= 55)
                                          && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (seasoned.Count > 0) opp = NearOne(seasoned, opp.Overall);
        }

        // A ranked contender's schedule: NOT a top-10 war every time out, and NEVER fed raw prospects. Most dates
        // are seasoned gatekeeper tune-ups; roughly one in three is a genuine clash with a fellow contender.
        if (WorldRanked(Player) && idx >= 0 && idx <= 20)
        {
            var seasoned = ranked.Where(b => b.Id != Player.Id && !IsProspect(b) && ProFights(b) >= 15
                                          && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            bool clash = _rng.Next(3) == 0;
            var pick = clash ? seasoned.Where(b => b.Overall >= 74).OrderByDescending(RankScore).Take(10).ToList()
                             : seasoned.Where(b => b.Overall is >= 58 and <= 76).OrderByDescending(RankScore).Take(10).ToList();
            if (pick.Count == 0) pick = seasoned;
            if (pick.Count > 0) opp = pick[_rng.Next(pick.Count)];
        }

        // Before he is world-ranked, the OPPOSITION'S experience climbs with his own — and the man in the other
        // corner is a PROFESSIONAL OPPONENT, not another kid starting out.
        //
        // This used to select by career stage, and asked for a "Starter" in the first half-dozen bouts on the
        // reasoning that you begin against green boys who cannot hurt you. That is not how a record is built.
        // A debutant does not fight another debutant - somebody has to lose, and the sport does not throw away
        // two prospects to find out which. He fights a designated opponent: a man with twenty fights and
        // fifteen losses whose trade is losing competitively in somebody else's home town.
        //
        // Measured, the old rule had 79% of a player's first fourteen opponents carrying under a dozen fights
        // and only 17% carrying a losing record - a parade of 0-0 and 3-2 boys, some of whom were other
        // people's prospects. So the band is now drawn on EXPERIENCE AND RECORD, which is what makes a man an
        // opponent, and the career-stage question does not come into it.
        if (!WorldRanked(Player))
        {
            int sofar = ProFights(Player);
            // What he needs in the other corner: fights behind him, and a record that says he loses.
            int wantFights = sofar <= 6 ? 8 : sofar <= 13 ? 10 : 12;
            double wantWinRate = sofar <= 6 ? 0.58 : sofar <= 13 ? 0.65 : 1.00;

            bool Opponent(Boxer b)
            {
                int f = ProFights(b);
                if (f < wantFights) return false;
                int decided = b.Record.Wins + b.Record.Losses;
                return decided == 0 || b.Record.Wins / (double)decided <= wantWinRate;
            }

            var pool = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr
                                      && !DangerousProspect(b)
                                      && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();

            // The men who are genuinely opponents; failing that, anyone seasoned; failing that, leave it be.
            var band = pool.Where(Opponent).ToList();
            if (band.Count == 0) band = pool.Where(b => ProFights(b) >= wantFights).ToList();
            if (band.Count > 0) opp = NearOne(band, opp.Overall);
        }

        // ABSOLUTE final guard, enforcing BOTH hard rules at once. Run as separate passes they each undid the
        // other — the top-15 guard picked a champion, and the champion guard picked a top-15 man.
        //   1. A reigning world champion only ever meets the player with his belt on the line. The NPC world
        //      already keeps champions off undercards; without the same rule here the player could beat the WBA
        //      champion in a stay-busy bout and walk away with nothing.
        //   2. Until he's world-ranked (20 bouts) the player is kept away from the division's top men BY RANKING,
        //      not just by rating — an unproven #1 is often a young fighter whose rating hasn't caught up. This
        //      holds for a fast-tracked wonder kid too: graduating early earns him ranked opposition (#16 and
        //      below), not the very best.
        // Title bouts return far above this, so a genuinely earned challenge is unaffected.
        var offLimits = WorldRanked(Player)
            ? new HashSet<int>()
            : ActiveIn(Player.WeightClass).Where(RankedContender)
                  .OrderByDescending(RankScore).Take(15).Select(b => b.Id).ToHashSet();
        bool Barred(Boxer b) => IsWorldChampion(b) || offLimits.Contains(b.Id);
        if (Barred(opp))
        {
            var ok = ranked.Where(b => b.Id != Player.Id && !Barred(b) && b.Overall <= maxOvr
                                    && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (ok.Count == 0) ok = ranked.Where(b => b.Id != Player.Id && !Barred(b)).ToList();
            if (ok.Count > 0) opp = NearOne(ok, opp.Overall);
        }

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
