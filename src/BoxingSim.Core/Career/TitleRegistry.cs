using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Who holds what, in every division at once — the four world belts, the regional straps, the
/// defence counts behind each, and the player's own reigns.
///
/// This is state that seven files reached into directly, and they did not all reach in the same way: some
/// moved the primary belt through CrownChampion, which also takes the old champion's flag off him, and some
/// assigned the dictionary, which does not. Both still exist — CrownChampion and SetChamp — but they are
/// named for the difference now rather than being two spellings of the same line.
///
/// It is handed the clock because half of what a belt IS depends on the year: there is no WBC before 1963,
/// no IBF before 1983, the primary belt is "World" until the WBA names itself in 1962, and the lineal title
/// is only called the Ring championship from 1922. Those are not settings, they are the calendar.</summary>
internal sealed class TitleRegistry
{
    private readonly Func<DateOnly> _today;

    // Every division runs at once, so every belt is keyed by weight class. A null value is a vacant belt and
    // a missing key is the same thing — GetValueOrDefault makes the two indistinguishable on purpose.
    private readonly Dictionary<WeightClass, Boxer?> _champions = new();   // WBA / "World"
    private readonly Dictionary<WeightClass, Boxer?> _wbc = new();         // WBC (from 1963)
    private readonly Dictionary<WeightClass, Boxer?> _ibf = new();         // IBF (from 1983)
    // The lineal ("Ring") championship: unsanctioned, so it never passes on a relinquishment or a vacant-belt
    // bout. The holder keeps it until he's beaten in the ring, retires, or leaves the division.
    private readonly Dictionary<WeightClass, Boxer?> _lineal = new();
    private readonly Dictionary<(WeightClass Div, string Region), Boxer> _regional = new();   // (division, region) → belt holder
    // Successful defences of each belt, keyed by (division, belt slot, current holder) — so a new champion
    // automatically starts at zero. Belt slots are "WBA" (the primary/World belt), "WBC", "IBF".
    private readonly Dictionary<(WeightClass Div, string Belt, int Holder), int> _beltDefenses = new();
    private readonly List<TitleReign> _reigns = new();   // the PLAYER's reigns, in the order he won them

    public TitleRegistry(Func<DateOnly> today) => _today = today;

    private int Year => _today().Year;

    /// <summary>The Ring began recognising champions in 1922 — before that the lineal title is just "the man".</summary>
    public string LinealBelt => Year >= 1922 ? "Ring" : "Lineal";
    public string PrimaryBelt => Year < 1962 ? "World" : "WBA";
    public bool WbcActive => Year >= 1963;
    public bool IbfActive => Year >= 1983;   // the IBF is the third sanctioning body; no WBO or minor belts

    // ---- the line of succession ----

    private readonly List<BeltReign> _lineage = new();

    /// <summary>Every reign of every world belt, oldest first. The line of succession a division actually has:
    /// who held it, from when, until whom.</summary>
    public IReadOnlyList<BeltReign> Lineage => _lineage;

    /// <summary>The holders of one belt in one division, oldest first.</summary>
    public IReadOnlyList<BeltReign> LineageOf(WeightClass wc, string belt) =>
        _lineage.Where(r => r.Division == wc && r.Belt == belt).ToList();

    /// <summary>Record a belt changing hands, from inside the four setters every world belt moves through.
    ///
    /// Here rather than at the callers deliberately. A belt changes hands in a title bout, on a relinquishment,
    /// when a champion retires or moves up, when a vacant belt is contested, and when a man is stripped — and
    /// that list is one somebody adds to and forgets. The setter cannot be gone round: if the belt moved, this
    /// saw it.
    ///
    /// A reign is CLOSED by writing the date onto the outgoing man's open record rather than by looking his
    /// reign up, because the same man can hold the same belt twice and the second reign must not overwrite the
    /// first. Vacating writes no new reign — a vacant belt is the absence of one, not a holder called nobody.</summary>
    private void RecordBeltChange(WeightClass wc, string belt, Boxer? outgoing, Boxer? incoming, DateOnly? on)
    {
        if (ReferenceEquals(outgoing, incoming)) return;                       // not a change
        if (outgoing is not null && incoming is not null && outgoing.Id == incoming.Id) return;

        // The NIGHT it happened, not the day the world is standing on. A belt changes hands in a fight, and
        // that fight has its own date: during the warm-up a whole year is laid out while the clock sits on
        // 1 January, so reading the clock dated every reign of the sport's history to January — Eddie Machen
        // held the heavyweight title from "Jan 1966" to "Jan 1966". The same conflation ApplyOutcome was rid
        // of; a caller that knows the night passes it.
        var now = on ?? _today();

        // A LINE MOVES FORWARD. The warm-up lays a whole year of boxing out across its months while the yearly
        // pass that retires and re-crowns runs on 1 January, so the two orderings cross: a man could win a belt
        // in a bout dated July and have the reign closed by a January event, ending it six months before it
        // began. Clamped to the last thing that happened to this belt, because a succession in which the dates
        // run backwards is not a succession — it is two clocks written into one list.
        var last = _lineage.LastOrDefault(r => r.Division == wc && r.Belt == belt);
        if (last is not null)
        {
            var floor = last.Lost ?? last.Won;
            if (now < floor) now = floor;
        }
        if (outgoing is not null)
        {
            var open = _lineage.LastOrDefault(r => r.Division == wc && r.Belt == belt
                                                   && r.HolderId == outgoing.Id && r.Lost is null);
            if (open is not null)
            {
                open.Lost = now;
                open.LostTo = incoming?.Name;
                open.Defences = _beltDefenses.GetValueOrDefault((wc, Slot(belt), outgoing.Id));
            }
        }
        if (incoming is not null)
            _lineage.Add(new BeltReign
            {
                Division = wc, Belt = belt, HolderId = incoming.Id, Holder = incoming.Name,
                Country = incoming.Country, Won = now, TookFrom = outgoing?.Name,
            });
    }

    /// <summary>The defence-count key a belt is filed under. "Ring" keeps its own, the rest are their own name.</summary>
    private static string Slot(string belt) => belt switch { "WBC" => "WBC", "IBF" => "IBF", "Ring" => "Ring", _ => "WBA" };

    /// <summary>Load a reign back from a save, in the order it was written.</summary>
    public void LoadLineage(BeltReign r) => _lineage.Add(r);

    /// <summary>Put a belt back on a man while REHYDRATING a save, writing no reign.
    ///
    /// The line of succession is restored from the save in one piece, so going through the ordinary setters
    /// here would record every belt that happened to be occupied at the moment of saving as a brand new reign
    /// on top of the one already loaded — a champion would gain a second, identical reign every time the game
    /// was opened.</summary>
    public void LoadBelt(WeightClass wc, string belt, Boxer b)
    {
        switch (belt)
        {
            case "WBC": _wbc[wc] = b; break;
            case "IBF": _ibf[wc] = b; break;
            case "Ring": _lineal[wc] = b; break;
            default: _champions[wc] = b; break;
        }
    }

    // ---- world belts ----

    public Boxer? Champ(WeightClass wc) => _champions.GetValueOrDefault(wc);
    public Boxer? Wbc(WeightClass wc) => _wbc.GetValueOrDefault(wc);
    public Boxer? Ibf(WeightClass wc) => _ibf.GetValueOrDefault(wc);

    public void SetWbc(WeightClass wc, Boxer? b, DateOnly? on = null) { RecordBeltChange(wc, "WBC", _wbc.GetValueOrDefault(wc), b, on); _wbc[wc] = b; }
    public void SetIbf(WeightClass wc, Boxer? b, DateOnly? on = null) { RecordBeltChange(wc, "IBF", _ibf.GetValueOrDefault(wc), b, on); _ibf[wc] = b; }

    /// <summary>The three sanctioned world belts of a division and who holds each, for callers that have
    /// business with all of them rather than one — so a rule about champions is written once instead of three
    /// times, which is how the third belt gets forgotten.</summary>
    public IEnumerable<(string Belt, Boxer? Holder)> WorldHolders(WeightClass wc)
    {
        yield return (PrimaryBelt, Champ(wc));
        if (WbcActive) yield return ("WBC", Wbc(wc));
        if (IbfActive) yield return ("IBF", Ibf(wc));
    }

    /// <summary>Put a world belt on a man, or vacate it, by the name the belt goes under.</summary>
    public void SetWorld(WeightClass wc, string belt, Boxer? b, DateOnly? on = null)
    {
        if (belt == "WBC") SetWbc(wc, b, on);
        else if (belt == "IBF") SetIbf(wc, b, on);
        else SetChamp(wc, b, on);
    }

    /// <summary>Put the primary belt on a man, or vacate it with null. Setting it plainly — nobody's
    /// <see cref="Boxer.IsChampion"/> flag is touched here, because two callers deliberately crown a man and
    /// raise the flag themselves and a third wants the belt moved without it.</summary>
    public void SetChamp(WeightClass wc, Boxer? b, DateOnly? on = null)
    {
        RecordBeltChange(wc, "WBA", _champions.GetValueOrDefault(wc), b, on);
        _champions[wc] = b;
    }

    /// <summary>Crown a new primary champion: the belt moves AND the man he took it from stops being one.</summary>
    public void CrownChampion(Boxer b, DateOnly? on = null)
    {
        var wc = b.WeightClass;
        if (Champ(wc) is Boxer old) old.IsChampion = false;
        SetChamp(wc, b, on);      // through the setter, so the line of succession sees it
        b.IsChampion = true;
    }

    /// <summary>The lineal holder, ignoring a stale reference: the line ends the moment he retires or leaves the
    /// division, and a seeded-history warmup can retire a champion without passing through the yearly hooks.</summary>
    public Boxer? Lineal(WeightClass wc) =>
        _lineal.GetValueOrDefault(wc) is Boxer b && !b.Retired && b.WeightClass == wc ? b : null;

    /// <summary>The name against the line whether or not it still stands — for the sweep that CLEARS a stale
    /// one, which cannot use the guarded reader without deciding there is nothing there to clear.</summary>
    public Boxer? LinealOnRecord(WeightClass wc) => _lineal.GetValueOrDefault(wc);
    public void SetLineal(WeightClass wc, Boxer? b, DateOnly? on = null)
    {
        RecordBeltChange(wc, "Ring", _lineal.GetValueOrDefault(wc), b, on);
        _lineal[wc] = b;
    }

    /// <summary>True if the fighter currently holds any world belt (WBA/WBC/IBF) in his division.</summary>
    public bool HoldsAnyWorldBelt(Boxer b) =>
        Champ(b.WeightClass)?.Id == b.Id || Wbc(b.WeightClass)?.Id == b.Id || Ibf(b.WeightClass)?.Id == b.Id;

    /// <summary>The man holding every belt going in a division — the true undisputed champion, or null.</summary>
    public Boxer? Undisputed(WeightClass wc)
    {
        var a = Champ(wc);
        if (a is null) return null;
        if (WbcActive && Wbc(wc)?.Id != a.Id) return null;
        if (IbfActive && Ibf(wc)?.Id != a.Id) return null;
        return a;
    }

    /// <summary>True when one man holds both world belts in a division.</summary>
    public bool Unified(WeightClass wc) => Champ(wc) is Boxer a && Wbc(wc) is Boxer b && a.Id == b.Id;

    /// <summary>Strip a man of every world belt he holds in a division — he has retired, or moved out of it.</summary>
    public void VacateWorldBelts(WeightClass wc, Boxer b, DateOnly? on = null)
    {
        // Through the setters, all three. Assigning the dictionaries here left a retiring champion's reign
        // hanging open for ever, so the next man's reign began while the previous one had never ended and the
        // line said two men held one belt at once.
        if (Champ(wc)?.Id == b.Id) SetChamp(wc, null, on);
        if (Wbc(wc)?.Id == b.Id) SetWbc(wc, null, on);
        if (Ibf(wc)?.Id == b.Id) SetIbf(wc, null, on);
    }

    // ---- defence counts ----

    /// <summary>Which counter a belt's defences go on. Every name the sim uses for a belt — the era-dependent
    /// "World"/"WBA" pair, "Ring" and "Lineal" for the same line — collapses to one slot here, so a champion's
    /// count cannot be split across two spellings of the strap he is defending. Mapping an already-mapped slot
    /// leaves it alone, which is what lets counting and reading both go through it.</summary>
    private static string BeltSlot(string belt) =>
        belt switch { "WBC" => "WBC", "IBF" => "IBF", "Ring" or "Lineal" => "Ring", _ => "WBA" };

    public void Defended(WeightClass wc, string belt, int holder)
    {
        var key = (wc, BeltSlot(belt), holder);
        _beltDefenses[key] = _beltDefenses.GetValueOrDefault(key) + 1;
    }

    public int Defenses(WeightClass wc, string belt, int holderId) =>
        _beltDefenses.GetValueOrDefault((wc, BeltSlot(belt), holderId));

    /// <summary>Every sanctioned defence a fighter has made anywhere, for a career total.
    ///
    /// The lineal line is not a fourth belt to defend — a Ring defence IS one of the sanctioned defences, so
    /// it's tracked for the champions board but must never be double-counted into a career total.</summary>
    public int CareerDefenses(int fighterId) =>
        _beltDefenses.Where(kv => kv.Key.Holder == fighterId && kv.Key.Belt != "Ring").Sum(kv => kv.Value);

    /// <summary>The world belts a fighter currently holds in his division, with the defence count of each —
    /// for the card's championship line and to show all straps on a unified champion.</summary>
    public IEnumerable<(string Belt, int Defenses)> BeltsHeld(Boxer b)
    {
        var wc = b.WeightClass;
        if (Champ(wc)?.Id == b.Id) yield return (PrimaryBelt, Defenses(wc, "WBA", b.Id));
        if (Wbc(wc)?.Id == b.Id) yield return ("WBC", Defenses(wc, "WBC", b.Id));
        if (Ibf(wc)?.Id == b.Id) yield return ("IBF", Defenses(wc, "IBF", b.Id));
        if (Lineal(wc)?.Id == b.Id) yield return (LinealBelt, Defenses(wc, "Ring", b.Id));
        foreach (var kv in _regional.Where(kv => kv.Key.Div == wc && kv.Value.Id == b.Id))
            yield return (kv.Key.Region, 0);
    }

    // ---- regional belts ----

    public Boxer? Regional(WeightClass wc, string region) => _regional.GetValueOrDefault((wc, region));
    public bool TryRegional(WeightClass wc, string region, out Boxer holder) => _regional.TryGetValue((wc, region), out holder!);
    public void SetRegional(WeightClass wc, string region, Boxer holder) => _regional[(wc, region)] = holder;
    public void ClearRegional(WeightClass wc, string region) => _regional.Remove((wc, region));

    /// <summary>Every regional belt on the books, snapshotted — the caller is expected to be dropping some of
    /// them as it goes, which it cannot do while enumerating the live map.</summary>
    public IReadOnlyList<(WeightClass Div, string Region, Boxer Holder)> AllRegional =>
        _regional.Select(kv => (kv.Key.Div, kv.Key.Region, kv.Value)).ToList();

    public IEnumerable<string> RegionalBeltsOf(Boxer b, WeightClass wc) =>
        _regional.Where(kv => kv.Key.Div == wc && kv.Value.Id == b.Id).Select(kv => kv.Key.Region);

    // ---- the player's reigns ----

    public IReadOnlyList<TitleReign> Reigns => _reigns;
    public int TitleDefenses => _reigns.Sum(r => r.Defenses);
    public int DaysAsChampion => _reigns.Sum(r => (r.Lost ?? _today()).DayNumber - r.Won.DayNumber);
    public TitleReign? OpenReign(string belt) => _reigns.LastOrDefault(r => r.Belt == belt && r.Lost is null);
    public void BeginReign(string belt, DateOnly won) => _reigns.Add(new TitleReign { Belt = belt, Won = won });

    /// <summary>Close every reign still running — he has left the division, so they all end on the same day.</summary>
    public void EndOpenReigns(DateOnly on)
    {
        foreach (var r in _reigns.Where(r => r.Lost is null)) r.Lost = on;
    }

    // ---- save and load ----
    //
    // The save is a DTO boundary and stays one: these hand out and take back the raw contents, and the
    // shapes are the save's business rather than the registry's.

    public IEnumerable<KeyValuePair<WeightClass, Boxer?>> AllChampions => _champions;
    public IEnumerable<KeyValuePair<WeightClass, Boxer?>> AllWbc => _wbc;
    public IEnumerable<KeyValuePair<WeightClass, Boxer?>> AllIbf => _ibf;
    public IEnumerable<KeyValuePair<WeightClass, Boxer?>> AllLineal => _lineal;
    public IEnumerable<KeyValuePair<(WeightClass Div, string Belt, int Holder), int>> AllDefenses => _beltDefenses;

    public void LoadDefenses(WeightClass wc, string belt, int holder, int count) => _beltDefenses[(wc, belt, holder)] = count;
    public void LoadReign(TitleReign r) => _reigns.Add(r);
}
