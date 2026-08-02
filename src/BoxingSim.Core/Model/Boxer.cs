namespace BoxingSim.Core.Model;

/// <summary>A single fighter: identity, attributes, physical state and career record.</summary>
public sealed class Boxer
{
    public int Id { get; init; }
    public required string Name { get; set; }

    /// <summary>Optional ring nickname, e.g. "The Hammer". Shown on fighter cards.</summary>
    public string? Nickname { get; set; }

    /// <summary>An authored style override. When null, the style is inferred from ratings + record.</summary>
    public FightingStyle? DeclaredStyle { get; set; }

    public WeightClass WeightClass { get; set; }

    /// <summary>The highest division this fighter campaigned in over his career. A real fighter who fought
    /// at several weights starts at his lowest and moves up toward this ceiling; null/equal = single-division.</summary>
    public WeightClass? TopWeight { get; set; }

    /// <summary>The division he was campaigning in before his first move up, captured the first time he steps up.
    /// Null until then (he's still in it). Bounds how far up the scale a career can travel.</summary>
    public WeightClass? DebutWeight { get; set; }

    public required Ratings Ratings { get; set; }

    /// <summary>A shallow copy of this fighter carrying different ratings. Used to replay a recorded bout as a
    /// night where a man was better than he usually is — the copy goes into the engine, never into the world.</summary>
    public Boxer WithRatings(Ratings r)
    {
        var c = (Boxer)MemberwiseClone();
        c.Ratings = r;
        return c;
    }

    /// <summary>Arm reach in inches — a physical frame trait, not a skill. It never feeds Overall/Class;
    /// it only shapes the range battle in the ring (see the fight engine).
    ///
    /// Synthesised from the frame when unknown, which it had always claimed to do and never did: it was a
    /// plain auto-property, so every fighter the roster has no measurement for carried a reach of zero and
    /// the tale of the tape read "0″ reach". Derived the same way height is, from the frame and the name, so
    /// it is stable across sessions and costs nothing to store.</summary>
    public int Reach
    {
        get => _reach > 0 ? _reach : Physique.ReachInchesFor(TopWeight ?? WeightClass, Name);
        set => _reach = value;
    }
    private int _reach;

    /// <summary>Height in inches. Derived from the frame and the name, so it costs nothing to store and is
    /// stable across sessions and saves.</summary>
    public int Height => Physique.HeightInchesFor(TopWeight ?? WeightClass, Name);

    /// <summary>-1 slow starter, 0 even, +1 fast starter. Read per round by the engine, not a flat rating.</summary>
    public int StartSpeed => Physique.StartSpeedFor(Name);
    public string StartSpeedLabel => Physique.StartSpeedLabel(StartSpeed);

    public int Age { get; set; }

    /// <summary>Optional date of birth (free text, e.g. "1949-11-03"). Shown on cards.</summary>
    public string? DateOfBirth { get; set; }

    /// <summary>Year of the fighter's first professional bout (career start).</summary>
    public int? DebutYear { get; set; }

    /// <summary>The fighter's prime window (e.g. "1971-1975"); the card's ratings represent this peak.</summary>
    public string? PrimeYears { get; set; }

    /// <summary>Country/nationality (e.g. "USA", "England", "Italy"). Drives regional belt eligibility.</summary>
    public string? Country { get; set; }

    /// <summary>Belts actually held (e.g. "WBC", "British", "European").</summary>
    public List<string>? Titles { get; set; }

    /// <summary>The age at which this fighter's physical peak sits. Used by the aging curve.</summary>
    public int PeakAge { get; set; } = 28;

    /// <summary>Innate ceiling (1..100) that prospects climb toward as they mature.</summary>
    public int Potential { get; set; }

    public FightRecord Record { get; } = new();

    /// <summary>Fight-by-fight ledger built up during a career sim (empty for the static roster).</summary>
    public List<BoutLine> History { get; } = new();

    public bool Retired { get; set; }

    /// <summary>Elo-style points that drive divisional ranking; updated after every bout.</summary>
    public double RankPoints { get; set; } = 1000;

    /// <summary>Set when this fighter holds their division title.</summary>
    public bool IsChampion { get; set; }

    public int Overall => Ratings.Overall;

    /// <summary>Headline class on the 1–15 scale (rare at the top). Used for display; the engine and
    /// matchmaking keep the finer-grained <see cref="Overall"/> internally.</summary>
    public int Class => Ratings.Class;

    public override string ToString() =>
        $"{Name} ({WeightClass.DisplayName()}, {Overall} OVR, {Record})";
}

/// <summary>One line on a fighter's record: a single bout's date, result and knockdowns.</summary>
public sealed class BoutLine
{
    public DateOnly Date { get; init; }
    public required string Opponent { get; init; }
    public char Result { get; init; }        // 'W', 'L' or 'D'
    public string Method { get; init; } = "";
    public int Round { get; init; }
    public int KdFor { get; init; }
    public int KdAgainst { get; init; }
    public string? Note { get; init; }    // fight type, e.g. "WBA title", "eliminator", "NABF title"

    /// <summary>The injury that ended his career on this night, if one did — "a detached retina", and so on.
    /// A man's last fight is the most important line on his record and it used to look like any other loss.</summary>
    public string? CareerEndingInjury { get; init; }

    /// <summary>The weight the fight was made at. A record is not a list of nights at one weight — men move
    /// up, and a title won at welterweight is a different thing from one won at middleweight — so the bout
    /// has to remember its own division rather than borrow whatever the fighter happens to weigh now.
    /// For a superfight across two divisions this is the heavier man's class, which is where it was held.</summary>
    public WeightClass Division { get; init; }
    public string? Cards { get; init; }   // judges' scorecards for a decision, from this fighter's view
    public IReadOnlyList<BoutRound>? Rounds { get; init; }   // per-round breakdown (full-engine bouts only)
    public IReadOnlyList<string>? Commentary { get; init; }  // key play-by-play moments (full-engine bouts only)
}

/// <summary>One round on a stored scorecard: punches landed, knockdowns and the 10-point score,
/// all from the owning fighter's point of view.</summary>
public sealed class BoutRound
{
    public int Round { get; init; }
    public int LandedFor { get; init; }
    public int LandedAgainst { get; init; }
    public int KdFor { get; init; }
    public int KdAgainst { get; init; }
    public int ScoreFor { get; init; }
    public int ScoreAgainst { get; init; }
}
