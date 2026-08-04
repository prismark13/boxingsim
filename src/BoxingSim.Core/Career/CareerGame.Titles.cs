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
    private void UpdateLineal(FightResult res, Boxer a, Boxer b, string? note, DateOnly on)
    {
        var wc = a.WeightClass;
        if (wc != b.WeightClass || res.IsDraw || res.Winner is null || res.Loser is null) return;
        var champ = LinealOf(wc);

        if (champ is not null)
        {
            if (res.Loser.Id == champ.Id)
            {
                _titles.SetLineal(wc, res.Winner, on);
                _hall.MarkChampion(res.Winner.Id);
                LogEvent($"{res.Winner.Name} beats the man who beat the man — {res.Loser.Name}'s {LinealBelt} championship changes hands.",
                         res.Winner.Id == Player.Id, kind: "title", div: wc, on: on);
            }
            else if (res.Winner.Id == champ.Id && IsWorldTitleNote(note))
                Defended(wc, "Ring", champ.Id);
            return;
        }

        // Vacant — only a genuine championship bout between the two leading men can establish a new line.
        if (!IsWorldTitleNote(note)) return;
        var top2 = ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(2).Select(x => x.Id).ToHashSet();
        if (!(top2.Contains(a.Id) && top2.Contains(b.Id))) return;
        _titles.SetLineal(wc, res.Winner, on);
        _hall.MarkChampion(res.Winner.Id);
        LogEvent($"{res.Winner.Name} beats {res.Loser.Name} to establish himself as the {LinealBelt} champion at {wc.DisplayName()}.",
                 res.Winner.Id == Player.Id, kind: "title", div: wc, on: on);
    }

    /// <summary>A unified champion holds every belt going, so he IS the man — he takes a vacant lineal title.</summary>
    private void ClaimLinealByUnification(WeightClass wc, DateOnly? on = null)
    {
        if (LinealOf(wc) is not null || UndisputedOf(wc) is not Boxer u) return;
        _titles.SetLineal(wc, u, on);
        LogEvent($"{u.Name} holds every belt at {wc.DisplayName()} and is recognised as {LinealBelt} champion.",
                 u.Id == Player.Id, kind: "title", div: wc, on: on);
    }

    /// <summary>The lineal title can't be inherited: when the champion retires or leaves the division the line
    /// simply ends, and the next two leading men have to start a new one.</summary>
    private void VacateLineal(WeightClass wc, Boxer who, string why)
    {
        if (LinealOf(wc)?.Id != who.Id) return;
        _titles.SetLineal(wc, null);
        LogEvent($"The {LinealBelt} championship at {wc.DisplayName()} falls vacant — {who.Name} {why}.",
                 who.Id == Player.Id, kind: "title", div: wc);
    }

    // ---- regional belts ----

    private static readonly string[] RegionalBelts = { "NABF", "European", "Commonwealth" };

    /// <summary>Is this belt a regional strap rather than a world title? Public because the UI has to ask it:
    /// a world title can be billed as "WBC", as "WBA and IBF" or as "Undisputed" depending on what is in the
    /// ring, so anything testing for one by name gets a unification wrong.</summary>
    public static bool IsRegionalBelt(string belt) => RegionalBelts.Contains(belt);

    /// <summary>Does this man hold a world belt in his own division?
    ///
    /// The undercard pool used to exclude the WBA and WBC champions by name and say "champions sit these
    /// out — they only defend". It never mentioned the IBF, so the third champion of every division was
    /// quietly available to be matched on a club show, and could be beaten there with nothing on the line.
    /// That is how a man beats a reigning champion and walks away with no belt.</summary>
    private bool HoldsAnyWorldBelt(Boxer b) => _titles.HoldsAnyWorldBelt(b);

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
        if (!_hall.WasEverChampion(b.Id)) return true;
        return !WorldRanked(b) && _rng.NextDouble() < 0.10;
    }

    /// <summary>The regional belts the player currently holds (for the UI header).</summary>
    public IEnumerable<string> PlayerRegionalBelts => _titles.RegionalBeltsOf(Player, Division);
    public Boxer? RegionalChampion(string region) => _titles.Regional(Division, region);

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

    // ---- reading a division's belts as a set ----
    //
    // THERE ARE THREE OF THEM AND THE CODE KNEW ABOUT TWO. Every rule about champions was written out once per
    // belt — a defence for the WBA, a defence for the WBC, a defence for the IBF — and the three copies did not
    // say the same thing: the unification only ever merged the WBA and the WBC, so the IBF could not be won in
    // a unification or lost in one, and a division holding three belts between three men had no way to become
    // undisputed except by two of them retiring. These four read the register instead, so a rule about belts is
    // written once and applies to all of them.

    /// <summary>Every world belt a man holds in his own division, in the order they are always listed.</summary>
    private List<string> WorldBeltsOf(Boxer b) =>
        _titles.WorldHolders(b.WeightClass).Where(x => x.Holder?.Id == b.Id).Select(x => x.Belt).ToList();

    /// <summary>The best defence count behind any belt a man holds — how established his reign is.</summary>
    private int BestDefenceOf(WeightClass wc, Boxer b) =>
        _titles.WorldHolders(wc).Where(x => x.Holder?.Id == b.Id)
               .Select(x => DefensesOf(wc, x.Belt, b.Id)).DefaultIfEmpty(0).Max();

    /// <summary>The men holding a world belt in a division, the most decorated first: belts held, then the
    /// length of the reign behind them.</summary>
    private List<Boxer> BeltHoldersOf(WeightClass wc) =>
        _titles.WorldHolders(wc).Select(x => x.Holder).OfType<Boxer>()
               .DistinctBy(b => b.Id)
               .OrderByDescending(b => WorldBeltsOf(b).Count)
               .ThenByDescending(b => BestDefenceOf(wc, b))
               .ThenBy(b => b.Id)                       // determinism: two men, equal claims, stable order
               .ToList();

    /// <summary>What a set of belts is called on the poster. Every belt in the division at once is
    /// "Undisputed"; anything less is named.</summary>
    private string BeltLabel(WeightClass wc, IReadOnlyList<string> belts)
    {
        int existing = _titles.WorldHolders(wc).Count();
        if (belts.Count > 1 && belts.Count >= existing) return UndisputedBelt;
        return belts.Count switch
        {
            0 => "",
            1 => belts[0],
            2 => $"{belts[0]} and {belts[1]}",
            _ => $"{string.Join(", ", belts.Take(belts.Count - 1))} and {belts[^1]}",
        };
    }

    /// <summary>Put a world belt on a man through the crowning path, so the man he took it from stops being
    /// champion. <see cref="SetHolder"/> is the other one and is for a belt nobody held.</summary>
    private void CrownWorld(Boxer who, string belt, DateOnly? on = null)
    {
        if (belt == "WBC") CrownWbc(who, on);
        else if (belt == "IBF") CrownIbf(who, on);
        else CrownChampion(who, on);
    }

    /// <summary>Uniform belt access — routes world belts to their fields, regional belts to the map.</summary>
    private Boxer? BeltHolder(string belt) =>
        belt == "WBC" ? WbcChampion :
        belt == "IBF" ? IbfChampion :
        (belt == PrimaryBelt || belt == "WBA" || belt == "World") ? Champion :
        _titles.Regional(Division, belt);

    private bool PlayerHolds(string belt) => BeltHolder(belt)?.Id == Player.Id;

    /// <summary>Is this bout note a WORLD title (not a regional strap)?</summary>
    private static bool IsWorldTitleNote(string? note) =>
        note is not null && (note == "unification" || (note.EndsWith(" title") && !RegionalBelts.Any(rb => note.StartsWith(rb))));

    /// <summary>Regional champions who have outgrown the level give the belt up.
    ///
    /// A national or continental strap is a step on the way up, not something a genuine contender carries
    /// around for years. A man inside his division's top five is past it: he vacates, and the belt goes back
    /// into circulation for the men still climbing. Run once a year, with the rest of the world's business.</summary>
    private void RetireOutgrownRegionals()
    {
        foreach (var (div, region, holder) in _titles.AllRegional)
        {
            if (holder.Retired) { _titles.ClearRegional(div, region); continue; }
            // ValuePlace, not BoardPlace: a champion has no NUMBER to print, but he has unquestionably
            // outgrown a national strap, and reading his absence of a number as "unranked" would let him keep
            // one for ever. The numbering also matters here — with champions no longer eating places, "inside
            // the top five" now means the five best contenders, which is what this always meant to say.
            int place = ValuePlace(holder);
            if (place is <= 0 or > 5) continue;

            _titles.ClearRegional(div, region);
            if (div == Division)
                LogEvent($"{holder.Name} vacates the {region} title — he has outgrown it.",
                         holder.Id == Player.Id, kind: "title", div: div);
        }
    }

    /// <summary>Give up any regional belts a fighter holds — used when he WINS a world title.</summary>
    private void DropRegionals(Boxer b, DateOnly on)
    {
        foreach (var region in RegionalBelts)
            if (_titles.Regional(b.WeightClass, region)?.Id == b.Id)
            {
                _titles.ClearRegional(b.WeightClass, region);
                if (b.WeightClass == Division) LogEvent($"{b.Name} relinquishes the {region} title to campaign for a world belt.", b.Id == Player.Id, kind: "title", on: on);
            }
    }

    private void SetBeltHolder(string belt, Boxer holder)
    {
        if (belt == "WBC") CrownWbc(holder);
        else if (belt == "IBF") CrownIbf(holder);
        else if (belt == PrimaryBelt || belt == "WBA" || belt == "World") CrownChampion(holder);
        else _titles.SetRegional(holder.WeightClass, belt, holder);
    }

    // ---- vacant belts ----
    //
    // A VACANT BELT IS A FIGHT SOMEBODY HAS TO MAKE, not a hole the world fills the instant it appears. It used
    // to be settled in the same breath as it fell vacant: a champion retired on 1 January and his successor was
    // crowned on 1 January, so a belt never spent a day unclaimed and the fight for it happened before anyone
    // could hear it had been ordered. Real vacancies take months — the body orders it, two men are matched, and
    // the division waits.
    //
    // So it is booked here and fought when the calendar reaches it. Keyed by division and belt so the fortnight
    // tick cannot order the same fight over and over while it stands vacant.
    private readonly List<(WeightClass Div, string Belt, DateOnly On)> _vacantBouts = new();

    private Boxer? HolderOf(WeightClass wc, string belt) => belt switch
    {
        "WBC" => WbcOf(wc),
        "IBF" => IbfOf(wc),
        _ => ChampOf(wc)
    };

    private void SetHolder(WeightClass wc, string belt, Boxer who, DateOnly on)
    {
        if (belt == "WBC") _titles.SetWbc(wc, who, on);
        else if (belt == "IBF") _titles.SetIbf(wc, who, on);
        else { _titles.SetChamp(wc, who, on); who.IsChampion = true; }
        Crowned(who);   // a belt won vacant is still a belt won — see Crowned
    }

    /// <summary>Order a fight for a vacant belt, two to five months out.</summary>
    private void BookVacantTitle(WeightClass wc, string belt)
    {
        if (_vacantBouts.Any(v => v.Div == wc && v.Belt == belt)) return;
        var on = Date.AddDays(60 + _rng.Next(91));   // 2-5 months, the way one is actually made
        _vacantBouts.Add((wc, belt, on));
        LogEvent($"The {belt} {wc.DisplayName()} title is vacant. A fight to fill it is ordered for "
                 + $"{on:MMMM yyyy}.", kind: "title", div: wc);
    }

    /// <summary>Fight any vacant-title bout whose night has come. Called from the world tick, so a belt is won
    /// on the date it was booked for rather than on whatever day the belt happened to fall empty.</summary>
    private void SettleDueVacantTitles()
    {
        for (int i = _vacantBouts.Count - 1; i >= 0; i--)
        {
            var v = _vacantBouts[i];
            if (v.On > Date) continue;
            _vacantBouts.RemoveAt(i);
            if (!DivisionActive(v.Div)) continue;
            // Somebody may have taken it in the meantime — a unification, or a lineal claim. Then there is no
            // vacancy left to fill and the order lapses, which is what happens in the sport too.
            if (HolderOf(v.Div, v.Belt) is not null) continue;

            var others = v.Belt switch
            {
                "WBC" => new[] { ChampOf(v.Div)?.Id ?? 0, IbfOf(v.Div)?.Id ?? 0 },
                "IBF" => new[] { ChampOf(v.Div)?.Id ?? 0, WbcOf(v.Div)?.Id ?? 0 },
                _ => new[] { WbcOf(v.Div)?.Id ?? 0, IbfOf(v.Div)?.Id ?? 0 }
            };
            if (ContestVacantTitle(v.Div, v.Belt, others) is { } won)
                SetHolder(v.Div, v.Belt, won.Winner, won.Night);
        }
    }

    /// <summary>Brings the WBC belt into being in 1963 and orders fights for vacant world/regional belts.</summary>
    private void UpdateBeltsFor(WeightClass wc)
    {
        if (!DivisionActive(wc)) return;   // the division doesn't exist yet — no belts to fill
        if (WbcOf(wc) is Boxer w && w.Retired) _titles.SetWbc(wc, null);
        if (WbcActive && WbcOf(wc) is null) BookVacantTitle(wc, "WBC");
        // The IBF is established in 1983; it is filled the same way, by a fight that is ordered and then waited for.
        if (IbfOf(wc) is Boxer iw && iw.Retired) _titles.SetIbf(wc, null);
        if (IbfActive && IbfOf(wc) is null) BookVacantTitle(wc, "IBF");

        // A line that has ended (its holder retired or moved) is cleared, and a man who now holds every belt
        // going is recognised as the lineal champion — otherwise a division can show an "undisputed" champion
        // while the Ring title sits with someone else, which reads as a bug even though the rules allow it.
        if (_titles.LinealOnRecord(wc) is Boxer lc && (lc.Retired || lc.WeightClass != wc)) _titles.SetLineal(wc, null);
        ClaimLinealByUnification(wc);

        // Regional belts: each region's title goes to its best fighter in this division who isn't a world champion.
        foreach (var region in RegionalBelts)
        {
            var champ = ChampOf(wc); var wbc = WbcOf(wc); var ibf = IbfOf(wc);
            if (_titles.TryRegional(wc, region, out var cur) && (cur.Retired || cur.WeightClass != wc || RegionOf(cur) != region)) _titles.ClearRegional(wc, region);
            if (_titles.Regional(wc, region) is null)
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
                    _titles.SetRegional(wc, region, pick);
                    // Say so. A vacant regional belt used to change hands in silence, so the next holder's
                    // "relinquishes the title" line arrived with no explanation of how he came to have it.
                    LogEvent($"{pick.Name} takes the vacant {region} title.", kind: "title", div: wc);
                }
            }
        }
    }

    private TitleReign? OpenReign(string belt) => _titles.OpenReign(belt);

}
