namespace BoxingSim.Core.Career;

/// <summary>Everything the sport has reported, and how much of it there has ever been.
///
/// The two are not the same number, and that is the whole reason this is a type. The log is a capped FIFO:
/// once it is full every headline added drops one off the front, so its Count stops moving while the world
/// carries on making news. Code that asked "how many are there now, minus how many there were before" got
/// zero from that moment on, for the rest of the career — fifteen years of a world is about fifteen hundred
/// headlines, so a long career reached the cap and the build-up feed went permanently silent while a
/// division with three thousand active fighters reported a hundred headlines a year, and not one of them was
/// shown.
///
/// A position in a stream cannot be the length of a window onto it. <see cref="Mark"/> is the position and
/// it only ever goes up; the window is what is left. Nothing outside can see the list itself, so nobody can
/// take a mark against the wrong one again.</summary>
internal sealed class NewsLog
{
    private const int Capacity = 1500;   // bounded; eight divisions produce more news

    private readonly List<CareerEvent> _log = new();
    private long _writes;

    /// <summary>Headlines ever written — which is NOT the count of what is kept.</summary>
    public long Mark => _writes;

    public IReadOnlyList<CareerEvent> All => _log;

    public void Write(CareerEvent e)
    {
        _log.Add(e);
        _writes++;
        if (_log.Count > Capacity) _log.RemoveAt(0);
    }

    /// <summary>The headlines written since a mark. Clamped to what the log still holds, because a single step
    /// of a busy world can write more than the whole window keeps.</summary>
    public IReadOnlyList<CareerEvent> Since(long mark)
    {
        int added = (int)Math.Min(_writes - mark, _log.Count);
        return added <= 0 ? Array.Empty<CareerEvent>() : _log.Skip(_log.Count - added).ToList();
    }

    public IReadOnlyList<CareerEvent> Recent(int n) => _log.Skip(Math.Max(0, _log.Count - n)).ToList();

    /// <summary>Start the record clean — the decade of build-up before the player turned pro is not his story.</summary>
    public void Clear()
    {
        _log.Clear();
        _writes = 0;
    }

    /// <summary>Take back a saved career's headlines. A reopened career carries on counting from where the save
    /// left off, so the mark starts at what was restored rather than at zero.</summary>
    public void Restore(IEnumerable<CareerEvent> saved)
    {
        _log.AddRange(saved);
        _writes = _log.Count;
    }
}
