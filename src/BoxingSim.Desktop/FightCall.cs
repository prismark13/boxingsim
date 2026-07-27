using System.Text;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop;

/// <summary>How loud a line of the call is — drives its size and colour on the night.</summary>
public enum CallKind { Round, Action, Big, Drama, Score, Verdict }

/// <summary>One line of the fight being called, with the clock it happened on.</summary>
public sealed record CallLine(string Clock, string Text, CallKind Kind)
{
    public bool IsRound => Kind == CallKind.Round;
    public bool IsDrama => Kind == CallKind.Drama;
    public bool IsBig => Kind == CallKind.Big;
    public bool IsVerdict => Kind == CallKind.Verdict;
    public bool IsScore => Kind == CallKind.Score;
}

/// <summary>Turns the engine's 15-second ticks into a blow-by-blow call.
///
/// The engine already records what actually happened in each segment — the punch thrown, whether it was a
/// combination, whether it was a counter, whether a man was hurt or wobbled or cut. All of that was being
/// thrown away and reduced to "his round (18-4 landed)". This reads it back out as commentary, so a fight
/// plays as an event rather than a table.</summary>
public static class FightCall
{
    public static IReadOnlyList<CallLine> Build(FightResult res, Boxer me)
    {
        bool iAmA = res.A.Id == me.Id;
        string my = me.Name;
        string his = (iAmA ? res.B : res.A).Name;
        var lines = new List<CallLine>();

        foreach (var rd in res.Rounds)
        {
            lines.Add(new CallLine("", $"ROUND {rd.Round}", CallKind.Round));

            int prevKdMine = 0, prevKdHis = 0;
            bool cutMine = false, cutHis = false, hurtCalled = false, staggerCalled = false;
            foreach (var t in rd.Ticks)
            {
                // "A" and "B" are the engine's corners; flip them so the call is always from the player's side.
                int kdAgainstMe = iAmA ? t.KnockdownsA : t.KnockdownsB;
                int kdAgainstHim = iAmA ? t.KnockdownsB : t.KnockdownsA;
                bool bigMine = iAmA ? t.BigA : t.BigB;
                bool bigHis = iAmA ? t.BigB : t.BigA;
                string? punchMine = iAmA ? t.PunchA : t.PunchB;
                string? punchHis = iAmA ? t.PunchB : t.PunchA;
                bool bodyMine = iAmA ? t.BodyShotA : t.BodyShotB;
                bool bodyHis = iAmA ? t.BodyShotB : t.BodyShotA;
                int comboMine = iAmA ? t.ComboA : t.ComboB;
                int comboHis = iAmA ? t.ComboB : t.ComboA;
                bool counterMine = iAmA ? t.CounterA : t.CounterB;
                bool counterHis = iAmA ? t.CounterB : t.CounterA;
                int rockMine = iAmA ? t.RockA : t.RockB;      // how hurt I am
                int rockHis = iAmA ? t.RockB : t.RockA;
                bool staggerMine = iAmA ? t.StaggerA : t.StaggerB;
                bool staggerHis = iAmA ? t.StaggerB : t.StaggerA;
                double cutM = iAmA ? t.CutA : t.CutB;
                double cutH = iAmA ? t.CutB : t.CutA;
                bool handMine = iAmA ? t.HandA : t.HandB;
                bool handHis = iAmA ? t.HandB : t.HandA;

                if (bigMine && punchMine is not null)
                    lines.Add(new CallLine(t.Clock, Power(my, his, punchMine, bodyMine, comboMine, counterMine), CallKind.Big));
                if (bigHis && punchHis is not null)
                    lines.Add(new CallLine(t.Clock, Power(his, my, punchHis, bodyHis, comboHis, counterHis), CallKind.Big));

                if (!hurtCalled && rockHis >= 2)
                { lines.Add(new CallLine(t.Clock, $"{his} is badly hurt — he's on unsteady legs!", CallKind.Drama)); hurtCalled = true; }
                else if (!hurtCalled && rockMine >= 2)
                { lines.Add(new CallLine(t.Clock, $"{my} is in trouble here!", CallKind.Drama)); hurtCalled = true; }

                // Once a round: the engine flags a stagger on every tick a man stays wobbled, and calling it
                // each time turned the drama into a stuck record.
                if (!staggerCalled && staggerHis)
                { lines.Add(new CallLine(t.Clock, $"{my} is all over him, teeing off!", CallKind.Drama)); staggerCalled = true; }
                else if (!staggerCalled && staggerMine)
                { lines.Add(new CallLine(t.Clock, $"{his} smells blood and piles in!", CallKind.Drama)); staggerCalled = true; }

                // Whose knockdown was to the body depends on which corner the player is in.
                bool downBodyHis = iAmA ? t.DownBodyB : t.DownBodyA;
                bool downBodyMine = iAmA ? t.DownBodyA : t.DownBodyB;
                if (kdAgainstHim > prevKdHis)
                {
                    prevKdHis = kdAgainstHim;
                    lines.Add(new CallLine(t.Clock,
                        downBodyHis ? $"{his} IS DOWN — and it was to the body!" : $"{his} IS DOWN!",
                        CallKind.Drama));
                }
                if (kdAgainstMe > prevKdMine)
                {
                    prevKdMine = kdAgainstMe;
                    lines.Add(new CallLine(t.Clock,
                        downBodyMine ? $"{my} IS DOWN to a body shot!" : $"{my} IS DOWN!", CallKind.Drama));
                }

                if (!cutHis && cutH >= 0.4) { cutHis = true; lines.Add(new CallLine(t.Clock, $"{his} has been opened up — there's blood.", CallKind.Drama)); }
                if (!cutMine && cutM >= 0.4) { cutMine = true; lines.Add(new CallLine(t.Clock, $"{my} is cut.", CallKind.Drama)); }

                if (handMine) lines.Add(new CallLine(t.Clock, $"{my} shakes his hand out — that looked painful.", CallKind.Action));
                if (handHis) lines.Add(new CallLine(t.Clock, $"{his} is favouring his hand.", CallKind.Action));

                if (t.Foul is FoulEvent f)
                {
                    string who = (f.Who == 0) == iAmA ? my : his;
                    lines.Add(new CallLine(t.Clock,
                        f.Dq ? $"{who} is DISQUALIFIED for {f.Type}."
                        : f.Deduct ? $"A point comes off {who} for {f.Type}."
                        : $"{who} is warned for {f.Type}.", f.Deduct || f.Dq ? CallKind.Drama : CallKind.Action));
                }

                if (t.Fin is StopInfo fin)
                {
                    string w = (fin.Winner == 0) == iAmA ? my : his;
                    string l = w == my ? his : my;
                    lines.Add(new CallLine(t.Clock, fin.Method switch
                    {
                        "KO" => $"{w} KNOCKS OUT {l}!",
                        "DQ" => $"{l} is disqualified — it's over.",
                        "cut" => $"It's waved off — {l} can't continue with that cut.",
                        _ => fin.Body ? $"{w} STOPS {l} to the body!" : $"{w} STOPS {l}!"
                    }, CallKind.Verdict));
                }
            }

            int myLanded = iAmA ? rd.LandedA : rd.LandedB;
            int hisLanded = iAmA ? rd.LandedB : rd.LandedA;
            int myScore = iAmA ? rd.ScoreA : rd.ScoreB;
            int hisScore = iAmA ? rd.ScoreB : rd.ScoreA;
            string verdict = myLanded > hisLanded + 2 ? $"{my} takes it"
                           : hisLanded > myLanded + 2 ? $"{his} takes it"
                           : "hard round to split";
            lines.Add(new CallLine("", $"End of {rd.Round} — {verdict}. {myScore}–{hisScore}, {myLanded}–{hisLanded} landed.",
                                   CallKind.Score));
        }
        return lines;
    }

    /// <summary>Describe a power shot the way it would be called, using the punch the engine actually threw.
    /// The engine's punch names already carry their own article ("a left hook", "an uppercut inside"), so
    /// nothing here may add one — that produced "lands a an uppercut".</summary>
    private static string Power(string attacker, string target, string punch, bool body, int combo, bool counter)
    {
        string shot = punch.ToLowerInvariant();
        var sb = new StringBuilder(attacker);
        if (counter) sb.Append(" times him with ");
        else if (combo > 1) sb.Append(" opens up — a combination finished with ");
        else sb.Append(" lands ");
        sb.Append(shot);
        // Only say "to the body" when the punch name hasn't already named the target — the engine throws
        // "a right to the ribs" and "a right under the heart", which don't need it spelling out again.
        if (body && !NamesTheBody(shot)) sb.Append(" to the body");
        sb.Append(counter ? "." : "!");
        return sb.ToString();
    }

    private static readonly string[] BodyWords =
        { "body", "ribs", "liver", "heart", "midsection", "solar plexus", "kidney" };

    private static bool NamesTheBody(string shot) => BodyWords.Any(shot.Contains);
}
