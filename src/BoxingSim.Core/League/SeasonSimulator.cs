using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.League;

/// <summary>Runs one calendar year: matchmaking, bouts, ranking updates, aging and retirements.</summary>
public sealed class SeasonSimulator
{
    private readonly World _world;
    private readonly FightEngine _engine;
    private readonly Random _rng;
    private const double EloK = 32.0;

    public SeasonSimulator(World world, Random rng)
    {
        _world = world;
        _rng = rng;
        _engine = new FightEngine(rng);
    }

    public SeasonReport RunSeason(int targetRosterSize)
    {
        var report = new SeasonReport { Year = _world.Year };

        foreach (var div in _world.Divisions.Values)
            SimulateDivision(div, report);

        AgeAndRetire(report);
        Replenish(targetRosterSize, report);

        _world.Year++;
        return report;
    }

    private void SimulateDivision(Division div, SeasonReport report)
    {
        var dReport = new DivisionReport { WeightClass = div.WeightClass };
        var fought = new HashSet<int>();

        var contenders = div.Contenders();

        // --- Title fight ---
        if (div.Champion is not null && contenders.Count > 0)
        {
            var challenger = contenders[0];
            var res = Bout(div.Champion, challenger, 12, report);
            dReport.TitleFight = res;
            fought.Add(div.Champion.Id);
            fought.Add(challenger.Id);

            if (res.Winner is not null && res.Winner.Id == challenger.Id)
                CrownChampion(div, challenger, dReport, report);
            // champion retains on a win or a draw
        }
        else if (div.Champion is null && contenders.Count >= 2)
        {
            // Vacant belt: top two contenders fight for it.
            var x = contenders[0];
            var y = contenders[1];
            var res = Bout(x, y, 12, report);
            dReport.TitleFight = res;
            dReport.TitleVacantFight = true;
            fought.Add(x.Id);
            fought.Add(y.Id);

            var newChamp = res.Winner ?? x; // a drawn vacant fight defaults the belt to the higher seed
            CrownChampion(div, newChamp, dReport, report);
        }

        // --- Undercard: pair the rest by adjacent ranking for competitive matchups ---
        var pool = div.Ranked().Where(b => !fought.Contains(b.Id)).ToList();
        for (int i = 0; i + 1 < pool.Count; i += 2)
        {
            int rounds = i < 6 ? 10 : 8;
            Bout(pool[i], pool[i + 1], rounds, report);
        }

        dReport.Champion = div.Champion;
        dReport.TopContenders = div.Contenders().Take(5).ToList();
        report.Divisions.Add(dReport);
    }

    private void CrownChampion(Division div, Boxer newChamp, DivisionReport dReport, SeasonReport report)
    {
        if (div.Champion is not null) div.Champion.IsChampion = false;
        div.Champion = newChamp;
        newChamp.IsChampion = true;
        dReport.TitleChanged = true;
        report.NewChampions.Add($"{div.WeightClass.DisplayName()}: {newChamp.Name}");
    }

    private FightResult Bout(Boxer a, Boxer b, int rounds, SeasonReport report)
    {
        double aBefore = a.RankPoints, bBefore = b.RankPoints;

        var res = _engine.Simulate(a, b, rounds);
        report.TotalFights++;
        if (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout)
            report.Knockouts++;

        ApplyRecord(res);

        double scoreA = res.IsDraw ? 0.5 : (res.Winner!.Id == a.Id ? 1.0 : 0.0);
        UpdateElo(a, b, scoreA, res);

        TrackUpset(res, aBefore, bBefore, report);
        return res;
    }

    private void ApplyRecord(FightResult res)
    {
        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        if (res.IsDraw)
        {
            res.A.Record.Draws++;
            res.B.Record.Draws++;
            return;
        }

        res.Winner!.Record.Wins++;
        res.Loser!.Record.Losses++;
        if (ko)
        {
            res.Winner.Record.KnockoutWins++;
            res.Loser.Record.KnockoutLosses++;
            _world.Careers.RegisterKnockoutLoss(res.Loser);
        }
    }

    private void UpdateElo(Boxer a, Boxer b, double scoreA, FightResult res)
    {
        double ea = 1.0 / (1.0 + Math.Pow(10, (b.RankPoints - a.RankPoints) / 400.0));
        a.RankPoints += EloK * (scoreA - ea);
        b.RankPoints += EloK * ((1 - scoreA) - (1 - ea));

        // A statement finish earns a little extra ranking shine.
        if (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout && res.Winner is not null)
            res.Winner.RankPoints += 6;
    }

    private void TrackUpset(FightResult res, double aBefore, double bBefore, SeasonReport report)
    {
        if (res.IsDraw || res.Winner is null) return;
        double winnerBefore = res.Winner.Id == res.A.Id ? aBefore : bBefore;
        double loserBefore = res.Winner.Id == res.A.Id ? bBefore : aBefore;
        double gap = loserBefore - winnerBefore;
        if (gap > report.BiggestUpsetGap)
        {
            report.BiggestUpsetGap = gap;
            report.BiggestUpsetWinner = res.Winner;
            report.BiggestUpsetLoser = res.Loser;
        }
    }

    private void AgeAndRetire(SeasonReport report)
    {
        foreach (var div in _world.Divisions.Values)
        {
            foreach (var b in div.Boxers.Where(b => !b.Retired).ToList())
            {
                _world.Careers.AdvanceOneYear(b);
                if (_world.Careers.ShouldRetire(b, b.IsChampion))
                {
                    b.Retired = true;
                    report.Retirements++;
                    if (b.IsChampion)
                    {
                        b.IsChampion = false;
                        div.Champion = null; // vacated below
                    }
                }
            }

            // A retired champion's belt passes to the top remaining contender.
            if (div.Champion is null)
            {
                var heir = div.Contenders().FirstOrDefault();
                if (heir is not null)
                {
                    div.Champion = heir;
                    heir.IsChampion = true;
                }
            }
        }
    }

    private void Replenish(int targetRosterSize, SeasonReport report)
    {
        int needed = targetRosterSize - _world.ActiveCount;
        if (needed <= 0) return;
        _world.InjectProspects(needed);
        report.Debuts += needed;
    }
}
