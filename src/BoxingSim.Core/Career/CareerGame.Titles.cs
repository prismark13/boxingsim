using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The belts. The lineal line, the regional straps, and who is entitled to what.</summary>
public sealed partial class CareerGame
{
    // ---- the lineal ("Ring") championship ----

    /// <summary>Move the lineal title, applying "the man who beat the man". It is NOT sanctioned, so unlike the
    /// alphabet belts it never changes hands on a relinquishment, a stripping, or a vacant-title bout — only in
    /// the ring. A draw leaves it where it is. When it's vacant it's filled the way The Ring fills it: by the
    /// division's two leading men meeting for a world title, or by a man unifying the belts.</summary>
    private void UpdateLineal(FightResult res, Boxer a, Boxer b, string? note)
    {
        var wc = a.WeightClass;
        if (wc != b.WeightClass || res.IsDraw || res.Winner is null || res.Loser is null) return;
        var champ = LinealOf(wc);

        if (champ is not null)
        {
            if (res.Loser.Id == champ.Id)
            {
                _lineal[wc] = res.Winner;
                _everChampion.Add(res.Winner.Id);
                LogEvent($"{res.Winner.Name} beats the man who beat the man — {res.Loser.Name}'s {LinealBelt} championship changes hands.",
                         res.Winner.Id == Player.Id, kind: "title", div: wc);
            }
            else if (res.Winner.Id == champ.Id && IsWorldTitleNote(note))
                Defended(wc, "Ring", champ.Id);
            return;
        }

        // Vacant — only a genuine championship bout between the two leading men can establish a new line.
        if (!IsWorldTitleNote(note)) return;
        var top2 = ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(2).Select(x => x.Id).ToHashSet();
        if (!(top2.Contains(a.Id) && top2.Contains(b.Id))) return;
        _lineal[wc] = res.Winner;
        _everChampion.Add(res.Winner.Id);
        LogEvent($"{res.Winner.Name} beats {res.Loser.Name} to establish himself as the {LinealBelt} champion at {wc.DisplayName()}.",
                 res.Winner.Id == Player.Id, kind: "title", div: wc);
    }

    /// <summary>A unified champion holds every belt going, so he IS the man — he takes a vacant lineal title.</summary>
    private void ClaimLinealByUnification(WeightClass wc)
    {
        if (LinealOf(wc) is not null || UndisputedOf(wc) is not Boxer u) return;
        _lineal[wc] = u;
        LogEvent($"{u.Name} holds every belt at {wc.DisplayName()} and is recognised as {LinealBelt} champion.",
                 u.Id == Player.Id, kind: "title", div: wc);
    }

    /// <summary>The lineal title can't be inherited: when the champion retires or leaves the division the line
    /// simply ends, and the next two leading men have to start a new one.</summary>
    private void VacateLineal(WeightClass wc, Boxer who, string why)
    {
        if (LinealOf(wc)?.Id != who.Id) return;
        _lineal[wc] = null;
        LogEvent($"The {LinealBelt} championship at {wc.DisplayName()} falls vacant — {who.Name} {why}.",
                 who.Id == Player.Id, kind: "title", div: wc);
    }

    // ---- regional belts ----

    private readonly Dictionary<(WeightClass Div, string Region), Boxer> _regional = new();   // (division, region) → belt holder
    private static readonly string[] RegionalBelts = { "NABF", "European", "Commonwealth" };

    /// <summary>Who is worth putting in for a regional title. A world-ranked contender obviously, but a good
    /// unbeaten prospect too - that is exactly what these belts are for, and holding a man back until he has
    /// twenty bouts means the belt only ever changes hands between established fighters. It is not everybody
    /// though: a credible challenger has a dozen fights behind him, is rated, and has been winning.</summary>
    private bool CredibleForRegional(Boxer b) =>
        ChasesRegional(b)
        && (WorldRanked(b)
            || (ProFights(b) >= 9 && b.Class >= 5
                && b.Record.Wins * 100 >= Math.Max(1, b.Record.Wins + b.Record.Losses) * 70));

    /// <summary>Whether a fighter would realistically campaign for a regional belt.
    ///
    /// These are a rung on the way UP - a man wins the NABF or the European to prove he belongs, and then goes
    /// after a world title. They were being handed to whoever stood highest in the rankings, which meant former
    /// world champions kept turning up to contest them, and that is not how the sport works. A man who has held
    /// a world title does not go back for a regional one unless his career has genuinely collapsed, and even
    /// then it is rare - it is a rebuilding job, not an ambition.
    ///
    /// A reigning world champion never does it at all.</summary>
    private bool ChasesRegional(Boxer b)
    {
        if (IsWorldChampion(b)) return false;
        if (!_everChampion.Contains(b.Id)) return true;
        return !WorldRanked(b) && _rng.NextDouble() < 0.10;
    }

    /// <summary>The regional belts the player currently holds (for the UI header).</summary>
    public IEnumerable<string> PlayerRegionalBelts => _regional.Where(kv => kv.Key.Div == Division && kv.Value.Id == Player.Id).Select(kv => kv.Key.Region);
    public Boxer? RegionalChampion(string region) => _regional.GetValueOrDefault((Division, region));

    /// <summary>Which regional belt a fighter's nationality makes him eligible for (null = none).</summary>
    private static string? RegionOf(Boxer b) => b.Country switch
    {
        "USA" or "United States" or "Canada" or "Mexico" or "Puerto Rico" or "Cuba" or "Argentina"
            or "Brazil" or "Venezuela" or "Colombia" or "Panama" or "Dominican Republic" => "NABF",
        "England" or "Scotland" or "Wales" or "Ireland" or "Northern Ireland" or "Australia"
            or "New Zealand" or "Nigeria" or "Ghana" or "South Africa" or "Jamaica" or "Canada (CW)" => "Commonwealth",
        "Germany" or "Italy" or "France" or "Spain" or "Russia" or "Soviet Union" or "Ukraine"
            or "Poland" or "Sweden" or "Denmark" or "Netherlands" or "Kazakhstan" or "Romania" or "Croatia" or "Finland" => "European",
        _ => null
    };

    /// <summary>Uniform belt access — routes world belts to their fields, regional belts to the map.</summary>
    private Boxer? BeltHolder(string belt) =>
        belt == "WBC" ? WbcChampion :
        belt == "IBF" ? IbfChampion :
        (belt == PrimaryBelt || belt == "WBA" || belt == "World") ? Champion :
        _regional.GetValueOrDefault((Division, belt));

    private bool PlayerHolds(string belt) => BeltHolder(belt)?.Id == Player.Id;

    /// <summary>Is this bout note a WORLD title (not a regional strap)?</summary>
    private static bool IsWorldTitleNote(string? note) =>
        note is not null && (note == "unification" || (note.EndsWith(" title") && !RegionalBelts.Any(rb => note.StartsWith(rb))));

    /// <summary>Give up any regional belts a fighter holds — used when he contests a world title.</summary>
    private void DropRegionals(Boxer b)
    {
        foreach (var region in RegionalBelts)
            if (_regional.GetValueOrDefault((b.WeightClass, region))?.Id == b.Id)
            {
                _regional.Remove((b.WeightClass, region));
                if (b.WeightClass == Division) LogEvent($"{b.Name} relinquishes the {region} title to campaign for a world belt.", b.Id == Player.Id, kind: "title");
            }
    }

    private void SetBeltHolder(string belt, Boxer holder)
    {
        if (belt == "WBC") CrownWbc(holder);
        else if (belt == "IBF") CrownIbf(holder);
        else if (belt == PrimaryBelt || belt == "WBA" || belt == "World") CrownChampion(holder);
        else _regional[(holder.WeightClass, belt)] = holder;
    }

    /// <summary>Brings the WBC belt into being in 1963 and re-crowns vacant world/regional belts in a division.</summary>
    private void UpdateBeltsFor(WeightClass wc)
    {
        if (!DivisionActive(wc)) return;   // the division doesn't exist yet — no belts to fill
        if (WbcOf(wc) is Boxer w && w.Retired) _wbc[wc] = null;
        if (WbcActive && WbcOf(wc) is null)
        {
            var winner = ContestVacantTitle(wc, "WBC", ChampOf(wc)?.Id ?? 0, IbfOf(wc)?.Id ?? 0);
            if (winner is not null) _wbc[wc] = winner;   // announced by ContestVacantTitle, dated to fight night
        }
        // The IBF is established in 1983; fill it from the leading contender who isn't already a world champ.
        if (IbfOf(wc) is Boxer iw && iw.Retired) _ibf[wc] = null;
        if (IbfActive && IbfOf(wc) is null)
        {
            var winner = ContestVacantTitle(wc, "IBF", ChampOf(wc)?.Id ?? 0, WbcOf(wc)?.Id ?? 0);
            if (winner is not null) _ibf[wc] = winner;   // announced by ContestVacantTitle, dated to fight night
        }

        // A line that has ended (its holder retired or moved) is cleared, and a man who now holds every belt
        // going is recognised as the lineal champion — otherwise a division can show an "undisputed" champion
        // while the Ring title sits with someone else, which reads as a bug even though the rules allow it.
        if (_lineal.GetValueOrDefault(wc) is Boxer lc && (lc.Retired || lc.WeightClass != wc)) _lineal[wc] = null;
        ClaimLinealByUnification(wc);

        // Regional belts: each region's title goes to its best fighter in this division who isn't a world champion.
        foreach (var region in RegionalBelts)
        {
            var champ = ChampOf(wc); var wbc = WbcOf(wc); var ibf = IbfOf(wc);
            if (_regional.TryGetValue((wc, region), out var cur) && (cur.Retired || cur.WeightClass != wc || RegionOf(cur) != region)) _regional.Remove((wc, region));
            if (!_regional.ContainsKey((wc, region)))
            {
                var contenders = ActiveIn(wc).Where(b => b.Id != Player.Id && RegionOf(b) == region
                                          && b.Id != champ?.Id && b.Id != wbc?.Id && b.Id != ibf?.Id
                                          && WorldRanked(b) && ChasesRegional(b))
                                 .OrderByDescending(RankScore).ToList();
                // Skip the very top of the list where possible: the best contender in a division is already
                // fighting for a world title, not collecting a regional one on his way past.
                var pick = contenders.Skip(2).FirstOrDefault() ?? contenders.FirstOrDefault();
                if (pick is not null)
                {
                    _regional[(wc, region)] = pick;
                    // Say so. A vacant regional belt used to change hands in silence, so the next holder's
                    // "relinquishes the title" line arrived with no explanation of how he came to have it.
                    LogEvent($"{pick.Name} takes the vacant {region} title.", kind: "title", div: wc);
                }
            }
        }
    }

    private TitleReign? OpenReign(string belt) => _reigns.LastOrDefault(r => r.Belt == belt && r.Lost is null);

}
