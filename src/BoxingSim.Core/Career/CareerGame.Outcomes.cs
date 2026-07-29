using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>What a result does to the two men in it: records, ratings, injuries, rankings and the news.</summary>
public sealed partial class CareerGame
{
    // ---- outcomes & ratings ----

    private void ApplyOutcome(FightResult res, Boxer a, Boxer b, string? note = null)
    {
        if (_watch is not null)
        {
            var w = res.Winner ?? a; var l = res.Loser ?? b;
            _watch.Add(new WorldBout(Date, a.WeightClass, RegionOf(a) ?? "Rest of the world", a.Country ?? "",
                                     w.Name, l.Name, res.Method, res.EndRound, res.IsDraw, note));
        }
        // Stepping up to the world stage means giving up any national/regional strap you were carrying.
        if (IsWorldTitleNote(note)) { DropRegionals(a); DropRegionals(b); }

        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        if (res.IsDraw) { a.Record.Draws++; b.Record.Draws++; }
        else
        {
            res.Winner!.Record.Wins++;
            res.Loser!.Record.Losses++;
            if (ko)
            {
                res.Winner.Record.KnockoutWins++;
                res.Loser.Record.KnockoutLosses++;
                _careers.RegisterKnockoutLoss(res.Loser);
                // A knockout means a medical suspension — a fragile fighter (low durability: chin/heart/conditioning)
                // is hurt worse and sits out far longer; a granite-chinned man is back in a month or two.
                int dura = Durability(res.Loser.Ratings);
                _outUntil[res.Loser.Id] = Date.AddDays(35 + Math.Max(0, 85 - dura) * 2 + _rng.Next(45));
            }
        }
        // Cuts and hand injuries can sideline either man, win or lose — a fighter with poor cut resistance is far
        // more injury-prone, so brittle fighters miss real time while durable ones almost never do.
        foreach (var f in new[] { a, b })
        {
            if (ko && f.Id == res.Loser?.Id) continue;   // the KO'd man is already on the shelf
            double proneness = 0.012 + (1.0 - f.Ratings.CutResistance / 100.0) * 0.06;
            if (_rng.NextDouble() < proneness)
                _outUntil[f.Id] = Date.AddDays(28 + _rng.Next(63));
        }

        // Each fighter's ledger: date, result, method, round, knockdowns scored / suffered.
        char ra = res.IsDraw ? 'D' : res.Winner!.Id == a.Id ? 'W' : 'L';
        char rb = res.IsDraw ? 'D' : res.Winner!.Id == b.Id ? 'W' : 'L';
        string? cardsA = null, cardsB = null;
        if (res.Scorecards.Count > 0)
        {
            cardsA = string.Join(" · ", res.Scorecards.Select(c => $"{c.A}-{c.B}"));
            cardsB = string.Join(" · ", res.Scorecards.Select(c => $"{c.B}-{c.A}"));
        }

        // Full-engine bouts carry a round-by-round breakdown; the fast NPC resolver has none.
        List<BoutRound>? roundsA = null, roundsB = null;
        if (res.Rounds.Count > 0)
        {
            roundsA = res.Rounds.Select(r => new BoutRound { Round = r.Round, LandedFor = r.LandedA, LandedAgainst = r.LandedB, KdFor = r.KnockdownsB, KdAgainst = r.KnockdownsA, ScoreFor = r.ScoreA, ScoreAgainst = r.ScoreB }).ToList();
            roundsB = res.Rounds.Select(r => new BoutRound { Round = r.Round, LandedFor = r.LandedB, LandedAgainst = r.LandedA, KdFor = r.KnockdownsA, KdAgainst = r.KnockdownsB, ScoreFor = r.ScoreB, ScoreAgainst = r.ScoreA }).ToList();
        }
        // The full per-round grid is kept only for bouts a player is likely to inspect — his own fights,
        // title fights, and any involving a world-ranked fighter. The rest keep just the (cheap) card
        // string, so a long career's save doesn't balloon with round data for journeyman undercards.
        bool keepRounds = a.Id == Player.Id || b.Id == Player.Id || note is not null || WorldRanked(a) || WorldRanked(b);
        var commentary = ExtractHighlights(res);   // null for the fast NPC resolver (no tick detail)
        // The weight it was made at. Equal for an ordinary bout; for a superfight between two divisions it is
        // the heavier man's, because that is the weight the lighter man came up to.
        var at = (WeightClass)Math.Max((int)a.WeightClass, (int)b.WeightClass);
        Record(a, b.Name, ra, res.Method, res.EndRound, res.KnockdownsB, res.KnockdownsA, note, cardsA, keepRounds ? roundsA : null, commentary, at);
        Record(b, a.Name, rb, res.Method, res.EndRound, res.KnockdownsA, res.KnockdownsB, note, cardsB, keepRounds ? roundsB : null, commentary, at);

        double scoreA = res.IsDraw ? 0.5 : res.Winner!.Id == a.Id ? 1.0 : 0.0;
        const double k = 32.0;
        double ea = 1.0 / (1.0 + Math.Pow(10, (b.RankPoints - a.RankPoints) / 400.0));
        a.RankPoints += k * (scoreA - ea);
        b.RankPoints += k * ((1 - scoreA) - (1 - ea));
        // Momentum matters — a win run forces a fighter into contention — but ONLY against real opposition, and
        // only in capped amounts. These bonuses are the one part of the rating that isn't zero-sum, so paying them
        // for every win turned the ratings into a fight counter: a busy journeyman out-earned an elite simply by
        // boxing more often, and a 60-fight record beat a 23-0 champion.
        if (res.Winner is Boxer wn && res.Loser is Boxer ls && WorldRanked(ls) && ls.Overall >= wn.Overall - 10)
        {
            if (ko) wn.RankPoints += 4;
            int ws = WinStreak(wn);   // includes the bout just recorded
            if (ws >= 3) wn.RankPoints += Math.Min(ws, 10) * 1.2;
        }
        if (res.Loser is not null) res.Loser.RankPoints -= 12;   // a defeat that ends a run stings the standing

        UpdateLineal(res, a, b, note);

        // Rare permanent wear carries forward (only matters for non-historical fighters, whose ratings
        // are recomputed from their prime each year — so apply to the player and generated fighters).
        foreach (var le in res.Lasting)
        {
            var f = le.Name == a.Name ? a : b;
            if (_historical.ContainsKey(f.Id)) continue;
            ApplyLasting(f.Ratings, le);
        }

        // Every bout is a chance for either man, from his prime on, to decide he's outgrowing the weight.
        ConsiderStepUp(a);
        ConsiderStepUp(b);

        NoteRematchDemand(res, a, b, note);   // did this one leave a question?
        CaptureBout(res, a, b, note);         // a candidate for the year-end awards
    }

    private static void ApplyLasting(Ratings r, LastingEffect le)
    {
        switch (le.Attr)
        {
            case "Chin": r.Chin = Ratings.Clamp(r.Chin + le.Delta); break;
            case "Power": r.Power = Ratings.Clamp(r.Power + le.Delta); break;
            case "CutResistance": r.CutResistance = Ratings.Clamp(r.CutResistance + le.Delta); break;
        }
    }

    private void CrownChampion(Boxer b)
    {
        _cursor = b.WeightClass;
        if (Champ is not null) Champ.IsChampion = false;
        Champ = b;
        b.IsChampion = true;
    }

    private void CrownWbc(Boxer b) { _cursor = b.WeightClass; Wbc = b; }
    private void CrownIbf(Boxer b) { _cursor = b.WeightClass; Ibf = b; }

}
