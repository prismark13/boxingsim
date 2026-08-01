using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The enshrined, and the long memory that decides who joins them.
///
/// Four of these trackers are here because a fighter's case cannot be reconstructed from what he is on the
/// day he retires. A man is inducted at 38, ten years past his best, having relinquished every belt he ever
/// held: by then his ratings say "journeyman" and his division says nothing about the two he won titles in.
/// So the world writes to this as it happens — his best rating ever, his best class ever, whether he was ever
/// champion, and every division he took a belt in — and the case is still there to read at the end.
///
/// They outlive a session for the same reason: a fighter who held a belt in an earlier sitting still has to
/// qualify when he finally hangs them up, so all four are saved and restored.</summary>
internal sealed class HallOfFame
{
    private readonly List<HallOfFamer> _hof = new();
    private readonly HashSet<int> _everChampion = new();
    private readonly Dictionary<int, int> _peakOverall = new();
    private readonly Dictionary<int, int> _peakClass = new();
    private readonly Dictionary<int, HashSet<WeightClass>> _titleDivisions = new();   // id → every division he held a world belt in

    /// <summary>The roll as it should be read — the greatest first.</summary>
    public IReadOnlyList<HallOfFamer> ByPrestige => _hof.OrderByDescending(m => m.Prestige).ToList();

    /// <summary>The roll in the order they were inducted, for the save.</summary>
    public IReadOnlyList<HallOfFamer> All => _hof;

    // ---- the long memory ----

    public void RecordPeak(int id, int overall, int cls)
    {
        _peakOverall[id] = Math.Max(_peakOverall.GetValueOrDefault(id), overall);
        _peakClass[id] = Math.Max(_peakClass.GetValueOrDefault(id), cls);
    }

    public void MarkChampion(int id) => _everChampion.Add(id);
    public bool WasEverChampion(int id) => _everChampion.Contains(id);

    /// <summary>He held a world belt in this division too — he campaigned up and won here as well.</summary>
    public void MarkTitleDivision(int id, WeightClass wc)
    {
        if (!_titleDivisions.TryGetValue(id, out var divs)) _titleDivisions[id] = divs = new();
        divs.Add(wc);
    }

    public int TitleDivisionCount(int id) => _titleDivisions.TryGetValue(id, out var d) ? d.Count : 0;

    /// <summary>Start the roll clean — the decade before the player turned pro is not his sport's history.</summary>
    public void Clear() => _hof.Clear();

    // ---- induction ----

    /// <summary>Enshrine a retiring great: a world champion with a real body of work, or a genuinely elite
    /// talent. Returns true if he went in.
    ///
    /// Everything it cannot know for itself is passed: how many professional fights he had, how many defences
    /// he made, whether he is holding a belt as he walks away, and the prime of a real fighter who was injected
    /// into the world mid-career rather than growing up in it (zero for anyone else — it is only ever a floor).
    /// The snapshot it keeps is self-contained, so it survives the roster being pruned on save.</summary>
    /// <param name="division">The weight he is REMEMBERED at, decided by the caller, which is not necessarily
    /// the one he happened to be in when he stopped. Pascual Perez went into the Hall as a bantamweight: he
    /// moved up late, so the last weight on his licence was the one recorded, and thirteen world flyweight
    /// title fights counted for nothing against three years of winding down.</param>
    public bool Induct(Boxer b, int proFights, int defenses, bool holdsBeltNow, int primeOverall, int primeClass,
                       int year, WeightClass division)
    {
        if (_hof.Any(x => x.Id == b.Id)) return false;
        int peak = Math.Max(_peakOverall.GetValueOrDefault(b.Id, b.Overall), primeOverall);
        int peakClass = Math.Max(Math.Max(_peakClass.GetValueOrDefault(b.Id), b.Class), primeClass);
        bool wasChamp = _everChampion.Contains(b.Id) || holdsBeltNow;
        _titleDivisions.TryGetValue(b.Id, out var tds);
        int weightTitles = tds is not null ? tds.Count : (wasChamp ? 1 : 0);
        // A real champion with a genuine reign (3+ defences) or a multi-weight champion — but only a true top-tier
        // fighter (peakClass floor keeps journeyman champions of a thin division out) — or an outright elite talent.
        // A Hall of Famer needs a real body of work, not a handful of bouts — plus either a genuine title reign,
        // a multi-weight title, or an elite career-long talent.
        bool worthy = proFights >= 15 && ((((wasChamp && defenses >= 3) || weightTitles >= 2) && peakClass >= 8) || (peak >= 88 && proFights >= 25));
        if (!worthy) return false;

        _hof.Add(new HallOfFamer
        {
            Id = b.Id, Name = b.Name, Nickname = b.Nickname, Country = b.Country, Division = division,
            Record = b.Record.ToString(), PeakOverall = peak, PeakClass = peakClass, Defenses = defenses, WasChampion = wasChamp,
            WeightTitles = weightTitles, TitleDivisions = tds?.OrderBy(d => (int)d).ToList() ?? new(), Age = b.Age, Year = year,
            // Snapshot the ledger (drop the heavy per-round grid/commentary) so the Hall keeps his fight history.
            History = b.History.Select(h => new BoutLine
            {
                // Division travels with the bout. Leaving it off did not leave it blank — WeightClass's
                // default is Flyweight — so every fight in every Hall of Famer's stored record read as a
                // flyweight bout, which only ever looked right for the flyweights.
                Date = h.Date, Opponent = h.Opponent, Result = h.Result, Method = h.Method, Division = h.Division,
                Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
            }).ToList()
        });
        return true;
    }

    // ---- save and load ----

    public IEnumerable<int> EverChampions => _everChampion;
    public IEnumerable<KeyValuePair<int, int>> PeakOveralls => _peakOverall;
    public IEnumerable<KeyValuePair<int, int>> PeakClasses => _peakClass;
    public IEnumerable<KeyValuePair<int, HashSet<WeightClass>>> TitleDivisions => _titleDivisions;

    public void Load(HallOfFamer member) => _hof.Add(member);
    public void LoadPeakOverall(int id, int overall) => _peakOverall[id] = overall;
    public void LoadPeakClass(int id, int cls) => _peakClass[id] = cls;
    public void LoadTitleDivisions(int id, HashSet<WeightClass> divs) => _titleDivisions[id] = divs;
}
