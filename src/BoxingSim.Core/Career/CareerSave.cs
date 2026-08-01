using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Flat, serializable snapshot of a career. Fighters are stored once and referenced by Id
/// so the shared graph (player / champion / offer all point at the same fighter) round-trips cleanly.</summary>
public sealed class CareerSave
{
    public WeightClass Division { get; set; }      // the player's current division
    public string Date { get; set; } = "";         // yyyy-MM-dd
    public string OfferDate { get; set; } = "";    // yyyy-MM-dd, when the current offer is booked for
    public int PlayerId { get; set; }
    public Dictionary<string, int> Champions { get; set; } = new();      // division name → WBA/World holder Id
    public Dictionary<string, int> WbcChampions { get; set; } = new();   // division name → WBC holder Id
    public Dictionary<string, int> IbfChampions { get; set; } = new();   // division name → IBF holder Id
    public Dictionary<string, int> LinealChampions { get; set; } = new();   // division name → lineal/Ring holder Id
    public int LastTitleShot { get; set; } = -100;

    /// <summary>A world title shot he has been granted and not yet taken, and the men he has turned down.
    /// Both are saved because both are things REFUSING costs him — leave them out and quitting without saving
    /// is a way of putting the belt back on the table and forgetting who you passed on.</summary>
    public string? ShotBelt { get; set; }
    public int ShotChampionId { get; set; }
    public int ShotGrantedAtFights { get; set; }
    public List<int> Declined { get; set; } = new();
    public List<BoxerSave> Roster { get; set; } = new();
    public List<HistoricalSave> Historical { get; set; } = new();
    // The player's own year-by-year arc. Absent on saves written before the card showed one.
    public List<ArcPointSave> PlayerArc { get; set; } = new();
    public List<FutureSave> Future { get; set; } = new();
    public List<CareerEventSave> Log { get; set; } = new();
    public List<TitleReignSave> Reigns { get; set; } = new();

    /// <summary>Every belt's line of succession. Written whole rather than diffed — a century of boxing is a
    /// few thousand short records, which is nothing beside the roster it sits next to.</summary>
    public List<BeltReignSave> Lineage { get; set; } = new();
    public Dictionary<string, int> Regional { get; set; } = new();   // "Division|Region" → holder Id
    public List<HallOfFamerSave> HallOfFame { get; set; } = new();
    public List<AwardsYearSave> Awards { get; set; } = new();
    public List<int> EverChampion { get; set; } = new();             // ids that ever held a world belt
    public Dictionary<string, int> PeakOverall { get; set; } = new();// "id" → best overall reached
    public Dictionary<string, int> PeakClass { get; set; } = new();  // "id" → best class reached
    public Dictionary<string, string> TitleDivisions { get; set; } = new();// "id" → "Div1|Div2" world-belt divisions
    public Dictionary<string, int> BeltDefenses { get; set; } = new();// "Division|Belt|HolderId" → defence count
    public OfferSave? Offer { get; set; }
}

public sealed class AwardsYearSave
{
    public int Year { get; set; }
    public List<AwardWinnerSave> FighterOfYear { get; set; } = new();
    public List<AwardWinnerSave> UpsetOfYear { get; set; } = new();
    public List<AwardWinnerSave> KnockoutOfYear { get; set; } = new();
    public List<AwardWinnerSave> FightOfYear { get; set; } = new();
}

public sealed class AwardWinnerSave
{
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Div { get; set; } = "";
    public string Commentary { get; set; } = "";
    // The fight the honour was for. Absent on saves written before awards carried one.
    public string? BoutWinner { get; set; }
    public string? BoutLoser { get; set; }
    public string? BoutDate { get; set; }
}

public sealed class HallOfFamerSave
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public string? Country { get; set; }
    public string Division { get; set; } = "";
    public string Record { get; set; } = "";
    public int PeakOverall { get; set; }
    public int PeakClass { get; set; }
    public int Defenses { get; set; }
    public bool WasChampion { get; set; }
    public int WeightTitles { get; set; }
    public List<string> TitleDivisions { get; set; } = new();
    public int Age { get; set; }
    public int Year { get; set; }
    public List<BoutLineSave> History { get; set; } = new();
}

/// <summary>One reign in a belt's line of succession, flattened for the save. Names and countries rather than
/// ids into the roster: the roster is pruned of retired men, and a lineage has to outlive the men in it.</summary>
public sealed class BeltReignSave
{
    public string Div { get; set; } = "";
    public string Belt { get; set; } = "";
    public int HolderId { get; set; }
    public string Holder { get; set; } = "";
    public string? Country { get; set; }
    public string Won { get; set; } = "";
    public string? Lost { get; set; }
    public string? TookFrom { get; set; }
    public string? LostTo { get; set; }
    public int Defences { get; set; }
}

public sealed class TitleReignSave
{
    public string Belt { get; set; } = "";
    public string Won { get; set; } = "";
    public string? Lost { get; set; }
    public int Defenses { get; set; }
}

public sealed class RatingsSave
{
    public int Power, Chin, Speed, Defense, Stamina, Accuracy, Conditioning, CutResistance, Aggression, Heart;

    public static RatingsSave From(Ratings r) => new()
    {
        Power = r.Power, Chin = r.Chin, Speed = r.Speed, Defense = r.Defense, Stamina = r.Stamina,
        Accuracy = r.Accuracy, Conditioning = r.Conditioning, CutResistance = r.CutResistance,
        Aggression = r.Aggression, Heart = r.Heart
    };

    public Ratings ToRatings() => new()
    {
        Power = Power, Chin = Chin, Speed = Speed, Defense = Defense, Stamina = Stamina,
        Accuracy = Accuracy, Conditioning = Conditioning, CutResistance = CutResistance,
        Aggression = Aggression, Heart = Heart
    };
}

public sealed class BoxerSave
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public string? Country { get; set; }
    public string? DateOfBirth { get; set; }
    public int? DebutYear { get; set; }
    public string? PrimeYears { get; set; }
    public WeightClass WeightClass { get; set; }
    public string? TopWeight { get; set; }
    public string? DebutWeight { get; set; }
    public int Reach { get; set; }
    public int Age { get; set; }
    public int PeakAge { get; set; }
    public int Potential { get; set; }
    public bool Retired { get; set; }
    public bool IsChampion { get; set; }
    public double RankPoints { get; set; }
    public int Wins, Losses, Draws, KoWins, KoLosses;
    public RatingsSave Ratings { get; set; } = new();
    public List<BoutLineSave> History { get; set; } = new();

    public static BoxerSave From(Boxer b)
    {
        var s = new BoxerSave
        {
            Id = b.Id, Name = b.Name, Nickname = b.Nickname, Country = b.Country, DateOfBirth = b.DateOfBirth,
            DebutYear = b.DebutYear, PrimeYears = b.PrimeYears, WeightClass = b.WeightClass, TopWeight = b.TopWeight?.ToString(),
            DebutWeight = b.DebutWeight?.ToString(), Reach = b.Reach, Age = b.Age,
            PeakAge = b.PeakAge, Potential = b.Potential, Retired = b.Retired, IsChampion = b.IsChampion,
            RankPoints = b.RankPoints, Wins = b.Record.Wins, Losses = b.Record.Losses, Draws = b.Record.Draws,
            KoWins = b.Record.KnockoutWins, KoLosses = b.Record.KnockoutLosses, Ratings = RatingsSave.From(b.Ratings)
        };
        foreach (var h in b.History)
            s.History.Add(new BoutLineSave
            {
                Date = h.Date.ToString("yyyy-MM-dd"), Opponent = h.Opponent, Result = h.Result.ToString(),
                Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst,
                Note = h.Note, Div = h.Division.ToString(), Cards = h.Cards,
                Rounds = h.Rounds?.Select(r => new BoutRoundSave
                {
                    Round = r.Round, LandedFor = r.LandedFor, LandedAgainst = r.LandedAgainst,
                    KdFor = r.KdFor, KdAgainst = r.KdAgainst, ScoreFor = r.ScoreFor, ScoreAgainst = r.ScoreAgainst
                }).ToList(),
                Commentary = h.Commentary?.ToList()
            });
        return s;
    }

    public Boxer ToBoxer()
    {
        var b = new Boxer
        {
            Id = Id, Name = Name, Nickname = Nickname, Country = Country, DateOfBirth = DateOfBirth,
            DebutYear = DebutYear, PrimeYears = PrimeYears, WeightClass = WeightClass,
            TopWeight = Enum.TryParse<WeightClass>(TopWeight, out var tw) ? tw : null,
            DebutWeight = Enum.TryParse<WeightClass>(DebutWeight, out var dw) ? dw : null, Age = Age,
            PeakAge = PeakAge, Potential = Potential, Retired = Retired, IsChampion = IsChampion,
            RankPoints = RankPoints, Ratings = Ratings.ToRatings()
        };
        b.Reach = Reach > 0 ? Reach : Physique.ReachInchesFor(b.TopWeight ?? WeightClass, Name);
        b.Record.Wins = Wins; b.Record.Losses = Losses; b.Record.Draws = Draws;
        b.Record.KnockoutWins = KoWins; b.Record.KnockoutLosses = KoLosses;
        foreach (var h in History)
            b.History.Add(new BoutLine
            {
                Date = DateOnly.TryParse(h.Date, out var d) ? d : default, Opponent = h.Opponent,
                Result = string.IsNullOrEmpty(h.Result) ? 'D' : h.Result[0], Method = h.Method,
                Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst,
                Note = h.Note, Cards = h.Cards,
                Division = Enum.TryParse<WeightClass>(h.Div, out var bw) ? bw : b.WeightClass,
                Rounds = h.Rounds?.Select(r => new BoutRound
                {
                    Round = r.Round, LandedFor = r.LandedFor, LandedAgainst = r.LandedAgainst,
                    KdFor = r.KdFor, KdAgainst = r.KdAgainst, ScoreFor = r.ScoreFor, ScoreAgainst = r.ScoreAgainst
                }).ToList(),
                Commentary = h.Commentary?.ToList()
            });
        return b;
    }
}

public sealed class BoutLineSave
{
    public string Date { get; set; } = "";
    public string Opponent { get; set; } = "";
    public string Result { get; set; } = "D";
    public string Method { get; set; } = "";
    public int Round { get; set; }
    public int KdFor { get; set; }
    public int KdAgainst { get; set; }
    public string? Note { get; set; }
    /// <summary>The weight the bout was made at. Absent from saves written before records carried it — those
    /// fall back to the division the fighter is in now, which is right for the many men who never moved.</summary>
    public string? Div { get; set; }
    public string? Cards { get; set; }
    public List<BoutRoundSave>? Rounds { get; set; }
    public List<string>? Commentary { get; set; }
}

public sealed class BoutRoundSave
{
    public int Round { get; set; }
    public int LandedFor { get; set; }
    public int LandedAgainst { get; set; }
    public int KdFor { get; set; }
    public int KdAgainst { get; set; }
    public int ScoreFor { get; set; }
    public int ScoreAgainst { get; set; }
}

/// <summary>One year of the player's development, kept because it cannot be recomputed.</summary>
public sealed class ArcPointSave
{
    public int Fights { get; set; }
    public int Age { get; set; }
    public RatingsSave R { get; set; } = new();
}

public sealed class HistoricalSave
{
    public int Id { get; set; }
    public int Peak { get; set; }
    public RatingsSave Prime { get; set; } = new();
}

public sealed class FutureSave
{
    public int DebutYear { get; set; }
    public int DebutAge { get; set; }
    public int Peak { get; set; }
    public BoxerSave Proto { get; set; } = new();
}

public sealed class OfferSave
{
    public int OpponentId { get; set; }
    public int Rounds { get; set; }
    public bool TitleFight { get; set; }
    public string? Belt { get; set; }
    public string Context { get; set; } = "";
}

public sealed class CareerEventSave
{
    public string On { get; set; } = "";   // yyyy-MM-dd
    public string Text { get; set; } = "";
    public bool PlayerBout { get; set; }
    public string? Kind { get; set; }
    public string? Div { get; set; }
    // The fight the headline reports. Absent on saves written before the feed was clickable.
    public string? BoutWinner { get; set; }
    public string? BoutLoser { get; set; }
}
