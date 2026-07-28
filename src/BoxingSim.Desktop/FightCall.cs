using BoxingSim.Core.Engine;
using System.Linq;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop;

/// <summary>How loud a line of the call is — drives its size and colour on the night.</summary>
public enum CallKind { Round, Action, Big, Drama, Score, Verdict, Pattern, Corner, Crowd, Position }

/// <summary>What actually HAPPENED on a line, independent of how it was worded. Sound and effects key off this,
/// never off the prose — the phrasing rotates, so matching on words silently missed half the knockdowns.</summary>
public enum CallEvent { None, RoundBell, Knockdown, Stoppage, Cut, Hurt, HardPunch }

/// <summary>One line of the fight being called, carrying the state of the fight AT that moment so the scoreboard
/// can move with the call rather than sitting still while text scrolls past.
///
/// <c>Actor</c> is 0 for the player's man, 1 for his opponent and -1 for neither. It drives a thin
/// corner-coloured marker down the edge of the line, so you can see whose work a line describes without
/// having to read the name in it.</summary>
public sealed record CallLine(string Clock, string Text, CallKind Kind,
                              int Round = 0, int MyLanded = 0, int HisLanded = 0,
                              int MyHurt = 0, int HisHurt = 0, CallEvent Event = CallEvent.None,
                              double MyGas = 1, double HisGas = 1, int Actor = -1)
{
    public bool IsRound => Kind == CallKind.Round;
    public bool IsDrama => Kind == CallKind.Drama;
    public bool IsBig => Kind == CallKind.Big;
    public bool IsVerdict => Kind == CallKind.Verdict;
    public bool IsScore => Kind == CallKind.Score;
    public bool IsPattern => Kind == CallKind.Pattern;
    public bool IsCorner => Kind == CallKind.Corner;
    public bool IsCrowd => Kind == CallKind.Crowd;
    public bool IsPosition => Kind == CallKind.Position;
    public bool IsMine => Actor == 0;
    public bool IsHis => Actor == 1;
    /// <summary>The moments that deserve to stop the room.</summary>
    public bool IsMoment => Kind is CallKind.Drama or CallKind.Verdict;
}

/// <summary>One round of the call: its lines while it is being fought, folded down to a summary once it is.</summary>
public sealed class RoundBlock : Observable
{
    public int Round { get; init; }
    public string Title => Round > 0 ? $"ROUND {Round}" : "";

    public System.Collections.ObjectModel.ObservableCollection<CallLine> Lines { get; } = new();

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        set { _summary = value; Raise(); Raise(nameof(HasSummary)); }
    }
    public bool HasSummary => !string.IsNullOrEmpty(_summary);

    private bool _expanded = true;
    public bool IsExpanded
    {
        get => _expanded;
        set { _expanded = value; Raise(); Raise(nameof(IsCollapsed)); }
    }
    public bool IsCollapsed => !_expanded;
}

/// <summary>Turns the engine's 15-second ticks into a blow-by-blow call.
///
/// The engine records what actually happened in each segment — the punch thrown, whether it was a combination
/// or a counter, whether a man was hurt or wobbled or cut. This reads it back out as commentary.
///
/// Two things keep it from reading like a machine. Phrasing rotates, so the same event is never described the
/// same way twice running. And repetition is treated as the STORY rather than a defect: a fighter who keeps
/// landing the same punch gets called for it — "he's found a home for the right hand" — which is exactly what
/// a commentator would notice.</summary>
public static class FightCall
{
    /// <summary>Which band the ring position is in, with real hysteresis: a band is ENTERED at a firm value
    /// and left only at a distinctly lower one. Without the gap a position hovering on a threshold flips back
    /// and forth every ten seconds and the call announces a change of position over and over - which is what
    /// happened, twenty times a fight, before the margins went in.
    ///
    /// 2 = he is on the ropes, 1 = he is being walked down, 0 = neither man owns the ring, negatives mirrored.</summary>
    private static int Band(double v, int current)
    {
        const double Pin = 0.30, PinOut = 0.22, Press = 0.16, PressOut = 0.10;
        double a = Math.Abs(v);
        int now = Math.Abs(current) switch
        {
            2 => a >= PinOut ? 2 : a >= PressOut ? 1 : 0,
            1 => a >= Pin ? 2 : a >= PressOut ? 1 : 0,
            _ => a >= Pin ? 2 : a >= Press ? 1 : 0
        };
        return now == 0 ? 0 : Math.Sign(v) * now;
    }

    /// <summary>What to call a man through the fight. His surname, unless both men share one — two Daniels in
    /// the ring is exactly when a surname stops identifying anybody, so then they keep their full names.</summary>
    private static string Short(string name, string other)
    {
        string Last(string n)
        {
            int sp = n.LastIndexOf(' ');
            return sp < 0 ? n : n[(sp + 1)..];
        }
        string mine = Last(name);
        return mine.Length < 3 || mine == Last(other) ? name : mine;
    }

    public static IReadOnlyList<CallLine> Build(FightResult res, Boxer me)
    {
        bool iAmA = res.A.Id == me.Id;
        // Surnames after the first mention. Over ten lines the full names appeared seven times between them,
        // and a block of text where every line opens with the same two words cannot be scanned - the eye has
        // nothing to catch on. A commentator says "Daniels", not "Tommy Daniels", forty times a fight.
        string my = Short(me.Name, (iAmA ? res.B : res.A).Name);
        string his = Short((iAmA ? res.B : res.A).Name, me.Name);
        var caller = new Caller();
        var lines = new List<CallLine>();

        // The clock repeated down the column - four consecutive "1:50"s - which is noise the eye stops
        // reading. It is shown only when it changes.
        string lastClock = "";
        int myTotal = 0, hisTotal = 0;
        // Where the fight is being fought carries across rounds, so the call does too: -1 means I am the one
        // on the ropes, +1 means he is, 0 means neither. Only CHANGES get spoken.
        int ringState = 0;
        foreach (var rd in res.Rounds)
        {
            lines.Add(new CallLine("", $"ROUND {rd.Round}", CallKind.Round, rd.Round, myTotal, hisTotal, Event: CallEvent.RoundBell));
            caller.NewRound();
            lastClock = "";

            int prevKdMine = 0, prevKdHis = 0;
            bool cutMine = false, cutHis = false, hurtCalled = false, staggerCalled = false;
            bool hurtMine = false, hurtHis = false;
            bool endedFight = false;
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
                int rockMine = iAmA ? t.RockA : t.RockB;
                int rockHis = iAmA ? t.RockB : t.RockA;
                bool staggerMine = iAmA ? t.StaggerA : t.StaggerB;
                bool staggerHis = iAmA ? t.StaggerB : t.StaggerA;
                double cutM = iAmA ? t.CutA : t.CutB;
                double cutH = iAmA ? t.CutB : t.CutA;
                bool handMine = iAmA ? t.HandA : t.HandB;
                bool handHis = iAmA ? t.HandB : t.HandA;
                bool downBodyHis = iAmA ? t.DownBodyB : t.DownBodyA;
                bool downBodyMine = iAmA ? t.DownBodyA : t.DownBodyB;

                int liveMine = myTotal + (iAmA ? t.LandedA : t.LandedB);
                int liveHis = hisTotal + (iAmA ? t.LandedB : t.LandedA);
                double gasMine = iAmA ? t.GasA : t.GasB;
                double gasHis = iAmA ? t.GasB : t.GasA;
                CallLine Line(string text, CallKind kind, CallEvent ev = CallEvent.None, int actor = -1)
                {
                    string shown = t.Clock == lastClock ? "" : t.Clock;
                    lastClock = t.Clock;
                    return new(shown, text, kind, rd.Round, liveMine, liveHis, rockMine, rockHis, ev,
                               gasMine, gasHis, actor);
                }

                // Ring position, from my side: positive means I have him backed up. Three bands, not two.
                // The old pair only spoke when somebody's back was actually against the ropes, and the
                // threshold for that sat at the 90th percentile of where the fight ever is - so half of all
                // fights never mentioned the ring once. Pressure gets called long before a man is pinned,
                // which is what a commentator does. Hysteresis is kept so a position hovering on a boundary
                // does not chatter: bands are entered at a firm value and left only on a clear reversal.
                double ring = iAmA ? t.Ring : -t.Ring;
                int nowState = Band(ring, ringState);
                if (nowState != ringState)
                {
                    lines.Add(nowState switch
                    {
                        2 => Line(caller.Trapped(my, his), CallKind.Position, actor: 0),
                        1 => Line(caller.Pressing(my, his), CallKind.Position, actor: 0),
                        -1 => Line(caller.Pressing(his, my), CallKind.Position, actor: 1),
                        -2 => Line(caller.Trapped(his, my), CallKind.Position, actor: 1),
                        // Coming off the ropes is a moment; drifting back to even from mere pressure is not.
                        _ => Math.Abs(ringState) == 2
                             ? Line(caller.Escaped(ringState == 2 ? his : my), CallKind.Position,
                                    actor: ringState == 2 ? 1 : 0)
                             : Line(caller.Even(), CallKind.Position)
                    });
                    ringState = nowState;
                }

                if (bigMine && punchMine is not null)
                    lines.Add(Line(caller.Power(my, his, punchMine, bodyMine, comboMine, counterMine), CallKind.Big, CallEvent.HardPunch, 0));
                if (bigHis && punchHis is not null)
                    lines.Add(Line(caller.Power(his, my, punchHis, bodyHis, comboHis, counterHis), CallKind.Big, CallEvent.HardPunch, 1));

                if (rockHis >= 2) hurtHis = true;
                if (rockMine >= 2) hurtMine = true;
                if (!hurtCalled && rockHis >= 2)
                { lines.Add(Line(caller.Hurt(my, his), CallKind.Drama, CallEvent.Hurt, 1)); lines.Add(Line(caller.Crowd(true), CallKind.Crowd)); hurtCalled = true; }
                else if (!hurtCalled && rockMine >= 2)
                { lines.Add(Line(caller.Hurt(his, my), CallKind.Drama, CallEvent.Hurt, 0)); hurtCalled = true; }

                // Once a round: the engine flags a stagger on every tick a man stays wobbled, and calling it
                // each time turned the drama into a stuck record.
                if (!staggerCalled && staggerHis)
                { lines.Add(Line(caller.Stagger(my, his), CallKind.Drama)); staggerCalled = true; }
                else if (!staggerCalled && staggerMine)
                { lines.Add(Line(caller.Stagger(his, my), CallKind.Drama)); staggerCalled = true; }

                if (kdAgainstHim > prevKdHis)
                {
                    prevKdHis = kdAgainstHim;
                    lines.Add(Line(caller.Down(my, his, downBodyHis, kdAgainstHim), CallKind.Drama, CallEvent.Knockdown));
                    lines.Add(Line(caller.Crowd(true), CallKind.Crowd));
                }
                if (kdAgainstMe > prevKdMine)
                {
                    prevKdMine = kdAgainstMe;
                    lines.Add(Line(caller.Down(his, my, downBodyMine, kdAgainstMe), CallKind.Drama, CallEvent.Knockdown));
                    lines.Add(Line(caller.Crowd(false), CallKind.Crowd));
                }

                if (!cutHis && cutH >= 0.4) { cutHis = true; lines.Add(Line(caller.Cut(his), CallKind.Drama, CallEvent.Cut)); }
                if (!cutMine && cutM >= 0.4) { cutMine = true; lines.Add(Line(caller.Cut(my), CallKind.Drama, CallEvent.Cut)); }

                if (handMine) lines.Add(Line(caller.Hand(my), CallKind.Action));
                if (handHis) lines.Add(Line(caller.Hand(his), CallKind.Action));

                if (t.Foul is FoulEvent f)
                {
                    string who = (f.Who == 0) == iAmA ? my : his;
                    lines.Add(Line(
                        f.Dq ? $"{who} is DISQUALIFIED for {f.Type}."
                        : f.Deduct ? $"A point comes off {who} for {f.Type}."
                        : caller.Warned(who, f.Type),
                        f.Deduct || f.Dq ? CallKind.Drama : CallKind.Action));
                }

                if (t.Fin is StopInfo fin)
                {
                    string w = (fin.Winner == 0) == iAmA ? my : his;
                    string l = w == my ? his : my;
                    // The shape of the ending matters to how it is called: a one-punch knockout in the second
                    // is not the same story as a man being broken down over twelve, and a fighter who has
                    // already been on the floor twice is not being knocked out "out of nowhere".
                    bool loserHadBeenDown = (w == my ? kdAgainstHim : kdAgainstMe) > 1;
                    lines.Add(Line(caller.Finish(w, l, fin, rd.Round, loserHadBeenDown),
                                   CallKind.Verdict, CallEvent.Stoppage));
                    lines.Add(Line(caller.Crowd(w == my), CallKind.Crowd));
                    endedFight = true;
                }
            }

            int myLanded = iAmA ? rd.LandedA : rd.LandedB;
            int hisLanded = iAmA ? rd.LandedB : rd.LandedA;
            int myScore = iAmA ? rd.ScoreA : rd.ScoreB;
            int hisScore = iAmA ? rd.ScoreB : rd.ScoreA;

            // Nothing follows the finish. The call used to run on into the shape of the round and then the
            // card, both written as though the fight were still going — and because a round cut short by a
            // knockout has barely any punches in it, the quiet-round read was the one that fired: "a cagey
            // round, both men measuring" printed directly underneath a man being counted out. When it is over
            // the finish is the read and the verdict is the card.
            if (!endedFight)
            {
                // How the round was FOUGHT, not just who won it. This is the read a commentator adds between
                // the action and the card, and it is what makes a quiet round worth listening to.
                if (caller.Pattern(rd, iAmA, my, his) is string shape)
                    lines.Add(new CallLine("", shape, CallKind.Pattern, rd.Round, myTotal + myLanded, hisTotal + hisLanded));
            }
            myTotal += myLanded;
            hisTotal += hisLanded;
            if (!endedFight)
                lines.Add(new CallLine("", caller.Recap(rd.Round, my, his, myLanded, hisLanded, myScore, hisScore),
                                       CallKind.Score, rd.Round, myTotal, hisTotal));

            // The corner, between rounds. Advice follows what actually happened to him, so it lands as counsel
            // rather than noise — and it is the only voice in the call that is on the player's side.
            if (rd.Round < res.Rounds.Count && caller.Corner(my, his, myLanded, hisLanded, cutMine, hurtMine, hurtHis) is string corner)
                lines.Add(new CallLine("", corner, CallKind.Corner, rd.Round, myTotal, hisTotal));
        }
        return lines;
    }

    /// <summary>Holds the state a commentator would carry in his head: how he last phrased something, and how
    /// often each man has landed each punch.</summary>
    private sealed class Caller
    {
        private readonly Dictionary<string, int> _cursor = new();
        private readonly Dictionary<string, int> _landed = new();   // "fighter|punch" -> times landed this fight
        private readonly Dictionary<string, int> _roundLanded = new();

        public void NewRound() => _roundLanded.Clear();

        /// <summary>Walk through a family's variants so the same wording never lands twice in a row.</summary>
        private string Rotate(string family, params string[] variants)
        {
            int i = _cursor.GetValueOrDefault(family);
            _cursor[family] = (i + 1) % variants.Length;
            return variants[i % variants.Length];
        }

        /// <summary>A man has been walked onto the ropes or into a corner. Called on the transition only —
        /// position is a state, and saying it every ten seconds would drown the round.</summary>
        public string Trapped(string att, string tgt) => Rotate("trapped",
            $"{att} has him on the ropes.",
            $"{tgt} is being backed up — {att} is cutting the ring off.",
            $"{att} walks him into the corner.",
            $"{tgt} has nowhere to go, and {att} knows it.",
            $"{att} has him pinned, working him over against the ropes.",
            $"{tgt} finds himself trapped along the ropes again.");

        /// <summary>Pressure without a pin. Most of a fight is spent here — one man walking the other down,
        /// taking the middle, herding him — and calling only the moment somebody's back hits the ropes meant
        /// half of all fights never mentioned where they were being fought at all.</summary>
        public string Pressing(string att, string tgt) => Rotate("pressing",
            $"{att} is walking him down.",
            $"{att} takes the centre of the ring.",
            $"{att} is cutting the ring off on him.",
            $"{tgt} is giving ground now.",
            $"{att} is backing him up, step by step.",
            $"{tgt} is on his bike, {att} tracking him.");

        /// <summary>Back to even. Neither man owns the ring.</summary>
        public string Even() => Rotate("even",
            "They meet in the middle again.",
            "Back to centre ring, both circling.",
            "Neither man is giving up the middle now.",
            "It has evened out — they are both boxing on the move.");

        /// <summary>And back out. Getting off the ropes is the other half of the story.</summary>
        public string Escaped(string who) => Rotate("escaped",
            $"{who} spins off the ropes and back to centre ring.",
            $"{who} works his way out of the corner.",
            $"{who} gets his feet moving and finds space again.",
            $"Good feet from {who} — he's back in the middle of the ring.",
            $"{who} slides out and resets.");

        public string Power(string att, string tgt, string punch, bool body, int combo, bool counter)
        {
            string shot = punch.ToLowerInvariant();
            string bare = Strip(shot);
            string key = att + "|" + bare;
            int n = _landed[key] = _landed.GetValueOrDefault(key) + 1;
            int inRound = _roundLanded[key] = _roundLanded.GetValueOrDefault(key) + 1;
            string to = body && !NamesTheBody(shot) ? " to the body" : "";

            // Repetition is the story. Once a punch keeps arriving, say so rather than repeating the sentence.
            if (inRound >= 3 || n >= 5)
                return Rotate("again3",
                    $"{att} keeps going back to the {bare} and it keeps arriving.",
                    $"The {bare} again — {att} can do no wrong with it.",
                    $"Every time {att} lets the {bare} go, it finds a home.",
                    $"{tgt} still has no answer to that {bare}.");
            if (n >= 3)
                return Rotate("again2",
                    $"That's the {bare} again — {att} has found a home for it.",
                    $"{att} goes back to the {bare}, and again it lands{to}.",
                    $"The {bare} once more from {att}; {tgt} isn't reading it.");
            if (n == 2)
                return Rotate("again1",
                    $"There it is again — the {bare} from {att}.",
                    $"{att} repeats the {bare}{to}.",
                    $"Another {bare} from {att}.");

            if (counter)
                return Rotate("counter",
                    $"{att} times him beautifully with {shot}.",
                    $"{att} waits on it and counters with {shot}.",
                    $"Lovely counter from {att} — {shot}.",
                    $"{tgt} walks onto {shot}.");
            if (combo > 1)
                return Rotate("combo",
                    $"{att} lets his hands go, finishing with {shot}.",
                    $"A burst from {att}, capped by {shot}.",
                    $"{att} strings them together and ends it with {shot}.",
                    $"Combination from {att} — {shot} on the end of it.");

            // Ordinary work ends on a full stop. When every routine punch shouted, five lines in ten carried an
            // exclamation mark and the knockdown three lines later had nothing left to raise its voice with;
            // "!" is now spent on the drama and the verdict, so it means something when it arrives.
            return Rotate("power",
                $"{att} lands {shot}{to}.",
                $"{att} gets through with {shot}{to}.",
                $"{att} cracks him with {shot}{to}.",
                $"Good {bare}{to} from {att}.",   // bare: "Good right cross", never "Good a right cross"
                $"{att} finds the mark with {shot}{to}.");
        }

        public string Hurt(string att, string tgt) => Rotate("hurt",
            $"{tgt} is badly hurt — his legs have gone!",
            $"{tgt} is in real trouble now!",
            $"{tgt} is hanging on — {att} has him going!",
            $"{tgt} is hurt, and {att} knows it!");

        public string Stagger(string att, string tgt) => Rotate("stagger",
            $"{att} is all over him, teeing off!",
            $"{att} smells the finish and piles in!",
            $"{att} has him on the ropes, letting everything go!");

        public string Down(string att, string tgt, bool body, int count)
        {
            if (body) return Rotate("downbody",
                $"{tgt} IS DOWN — and it was to the body!",
                $"{tgt} goes down clutching his ribs!");
            if (count >= 2) return Rotate("downagain",
                $"{tgt} IS DOWN AGAIN!",
                $"Down again! {tgt} is being taken apart!",
                $"That's {count} times {tgt} has been on the floor!");
            return Rotate("down",
                $"{tgt} IS DOWN!",
                $"DOWN GOES {tgt.ToUpperInvariant()}!",
                $"{att} puts him on the canvas!");
        }

        public string Cut(string who) => Rotate("cut",
            $"{who} has been opened up — there's blood.",
            $"A cut on {who}, and it's leaking badly.",
            $"{who} is marked up now; the doctor will want a look.");

        /// <summary>The arena. Only on the moments that would actually lift or silence a crowd.</summary>
        public string Crowd(bool forMe) => forMe
            ? Rotate("crowdUp",
                "The crowd is on its feet!",
                "A roar goes round the arena!",
                "They can smell it — the place has erupted!",
                "You can barely hear yourself in here!")
            : Rotate("crowdDown",
                "The crowd draws breath — they saw that one.",
                "A gasp goes round the hall.",
                "The place has gone quiet.");

        /// <summary>The corner between rounds. Advice tracks what actually happened to him last round, so it
        /// reads as counsel rather than filler — and it is the one voice in the call on the player's side.</summary>
        public string? Corner(string my, string his, int mine, int theirs, bool cut, bool wasHurt, bool hurtHim)
        {
            if (wasHurt) return Rotate("cnrHurt",
                "CORNER: “Sit down. Clear your head, hold if you have to — you're not losing this in one round.”",
                "CORNER: “Breathe. Tie him up, take your time and let it pass.”");
            if (cut) return Rotate("cnrCut",
                "CORNER: “Let me work on it. Keep that side away from him and don't let him see it bother you.”",
                "CORNER: “It's under control. Guard high, don't lean into that right hand.”");
            if (hurtHim) return Rotate("cnrGo",
                "CORNER: “He's there for the taking. Go and finish it — don't let him breathe.”",
                "CORNER: “You've hurt him. Straight down the middle and don't stop punching.”");
            if (theirs > mine + 3) return Rotate("cnrBehind",
                "CORNER: “You're giving this away. Double the jab and get off first.”",
                "CORNER: “He's beating you to the punch. Move your head and come back at him.”",
                "CORNER: “You need this round. Let your hands go.”");
            if (mine > theirs + 3) return Rotate("cnrAhead",
                "CORNER: “You're on top. Keep it long, don't get greedy.”",
                "CORNER: “That's the round. Same again — behind the jab, don't stand with him.”");
            return Rotate("cnrLevel",
                "CORNER: “It's close. Whoever wants it more takes this.”",
                "CORNER: “Nothing in it. Be first, and be busier.”");
        }

        public string Hand(string who) => Rotate("hand",
            $"{who} shakes his hand out — that looked painful.",
            $"{who} is favouring that hand.");

        public string Warned(string who, string type) => Rotate("warn",
            $"{who} is warned for {type}.",
            $"The referee steps in — {type} from {who}.");

        /// <summary>How a fight ended, told the way it happened. A knockout inside two rounds, a man broken
        /// down over twelve, one who had already been on the floor twice, a body shot that folded him — these
        /// are different endings and were all reaching the reader as the same two or three sentences.
        /// The pools are separated by the shape of the finish so the right kind of line gets picked.</summary>
        public string Finish(string w, string l, StopInfo fin, int round, bool hadBeenDown)
        {
            bool early = round <= 2;
            bool late = round >= 9;

            if (fin.Method == "DQ") return $"{l} is disqualified — it's over.";

            if (fin.Method == "cut")
                return Rotate("cut",
                    $"The doctor takes one look and waves it off — {l} can't continue with that cut.",
                    $"The referee calls the doctor over, and that is that. {l} is out on the cut.",
                    $"They can't stem it. The cut has ended {l}'s night.");

            if (fin.Method == "KO")
            {
                // A man who has been down before is not caught cold; he is worn down and finally caught.
                if (hadBeenDown)
                    return Rotate("koAgain",
                        $"{l} goes down again — and this time he does not get up. {w} has knocked him out!",
                        $"He had nothing left to get up with. {w} KNOCKS OUT {l}!",
                        $"That is the end of it — {l} has been on the floor once too often. {w} wins by knockout!");
                if (early)
                    return Rotate("koEarly",
                        $"OUT OF NOWHERE! {w} has knocked {l} spark out in the {Ord(round)}!",
                        $"{l} never saw it. {w} KNOCKS HIM OUT in the {Ord(round)}!",
                        $"One punch — and it is finished. {w} flattens {l} inside {round} round{(round == 1 ? "" : "s")}!");
                if (late)
                    return Rotate("koLate",
                        $"{l} is out on his feet no longer — he is out cold. {w} KNOCKS HIM OUT in the {Ord(round)}!",
                        $"After all of that, it ends with one shot. {w} KNOCKS OUT {l}!",
                        $"{l} has been carried into the deep water and drowned there. {w} wins by knockout!");
                return Rotate("ko",
                    $"{w} KNOCKS OUT {l}!",
                    $"IT'S ALL OVER — {w} has knocked him cold!",
                    $"{l} is out! {w} has finished it!",
                    $"Flat on his back — the referee does not even begin the count. {w} KNOCKS OUT {l}!",
                    $"{l} is gone. He was unconscious before he landed. {w} has knocked him out!",
                    $"Timber! {l} goes over stiff and {w} has his knockout!");
            }

            if (fin.Body)
                return Rotate("tkobody",
                    $"{l} turns away from the body shots and the referee jumps straight in! {w} STOPS him!",
                    $"The body work has done it — the referee waves it off! {w} STOPS {l}!",
                    $"{l} sinks to a knee, hand on his ribs, and the referee has seen enough! {w} wins it!",
                    $"That is what all that body work was for. {l} cannot straighten up and it is waved off — {w} wins!");

            if (hadBeenDown)
                return Rotate("tkoAgain",
                    $"The referee will not let him take another one. {w} STOPS {l}!",
                    $"{l} has been down too often and the referee steps in to save him. {w} wins it!",
                    $"Enough. The referee has seen a man who has stopped defending himself. {w} STOPS {l}!");
            if (late)
                return Rotate("tkoLate",
                    $"He has broken him down over {round} rounds — the referee waves it off! {w} STOPS {l}!",
                    $"{l} has nothing left to hold him up and the referee saves him in the {Ord(round)}. {w} wins it!",
                    $"The corner will not send him out again. It is over — {w} beats {l}.");
            return Rotate("tko",
                $"The referee jumps in to save him! {w} STOPS {l}!",
                $"The referee has seen enough — he waves it off! {w} STOPS {l}!",
                $"The towel comes in from {l}'s corner! {w} wins it!",
                $"That is all — the referee steps between them and saves {l}. {w} STOPS him!",
                $"{l} is taking them without answer and the referee has stepped in. {w} STOPS him!");
        }

        /// <summary>"the 4th", so a round can be named in prose rather than printed as a number.</summary>
        private static string Ord(int n) => n + (n % 100 is >= 11 and <= 13 ? "th"
                                                : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        /// <summary>Read the shape of a round from its ticks: body investment, a late surge or a fade, a war in
        /// the pocket, a cagey feeling-out, a man content to counter. Returns null when the round had no story
        /// worth telling, so this never becomes filler.</summary>
        public string? Pattern(RoundResult rd, bool iAmA, string my, string his)
        {
            var ticks = rd.Ticks;
            if (ticks.Count < 3) return null;

            int myBody = 0, hisBody = 0, myCounters = 0, hisCounters = 0;
            foreach (var t in ticks)
            {
                if (iAmA ? t.BodyShotA : t.BodyShotB) myBody++;
                if (iAmA ? t.BodyShotB : t.BodyShotA) hisBody++;
                if (iAmA ? t.CounterA : t.CounterB) myCounters++;
                if (iAmA ? t.CounterB : t.CounterA) hisCounters++;
            }

            // Landed counts on a tick are cumulative for the round, so halves come from the midpoint reading.
            var mid = ticks[ticks.Count / 2];
            var last = ticks[^1];
            int myFirst = iAmA ? mid.LandedA : mid.LandedB;
            int hisFirst = iAmA ? mid.LandedB : mid.LandedA;
            int myAll = iAmA ? last.LandedA : last.LandedB;
            int hisAll = iAmA ? last.LandedB : last.LandedA;
            int mySecond = myAll - myFirst, hisSecond = hisAll - hisFirst;

            if (myBody >= 3 && myBody > hisBody + 1)
                return Rotate("bodyMe",
                    $"{my} has been banking body shots all round — that tells late.",
                    $"Note the investment downstairs from {my}; {his} is starting to wear it.");
            if (hisBody >= 3 && hisBody > myBody + 1)
                return Rotate("bodyHim",
                    $"{his} keeps digging to the body — {my} is taking a toll there.",
                    $"That body work from {his} will be felt in the later rounds.");

            // A one-sided round is a story too.
            if (myAll >= 8 && myAll >= hisAll * 3)
                return Rotate("dominateMe",
                    $"{my} is putting a beating on him now.",
                    $"{his} has no answer — {my} is landing at will.");
            if (hisAll >= 8 && hisAll >= myAll * 3)
                return Rotate("dominateHim",
                    $"{his} is taking {my} apart in there.",
                    $"{my} is shipping a lot of leather — he needs to hold on.");

            if (myAll + hisAll >= 16 && Math.Abs(myAll - hisAll) <= 4)
                return Rotate("war",
                    "Neither man will take a backward step — they're trading in the pocket.",
                    "This has turned into a war; both are standing and letting them go.");

            if (mySecond >= myFirst * 2 && mySecond >= 4)
                return Rotate("surgeMe",
                    $"{my} finished the round on top — he took over in the last minute.",
                    $"A strong close from {my}; he stole that one late.");
            if (hisSecond >= hisFirst * 2 && hisSecond >= 4)
                return Rotate("surgeHim",
                    $"{his} came on strong at the end of that round.",
                    $"{his} finished the stronger — {my} faded in the last minute.");

            if (myFirst >= 4 && mySecond * 2 <= myFirst)
                return Rotate("fadeMe", $"{my} started fast and his output dropped away.");
            if (hisFirst >= 4 && hisSecond * 2 <= hisFirst)
                return Rotate("fadeHim", $"{his} began brightly, then the work dried up.");

            if (myCounters >= 3)
                return Rotate("ctrMe", $"{my} is content to sit back and counter everything {his} offers.");
            if (hisCounters >= 3)
                return Rotate("ctrHim", $"{his} is picking his moments, countering as {my} comes in.");

            // Low punch counts usually mean a quiet round — but not when the reason the punches stopped is that
            // somebody was on the floor. A round with a knockdown or a man badly hurt in it is never a
            // feeling-out round, however few shots landed.
            //
            // The threshold used to be 8, which turns out to catch 0.0% of rounds that are actually fought to
            // the bell — so this read only ever appeared on rounds cut short by a stoppage, describing a
            // knockout as "both men measuring". With those correctly excluded it would have been dead code, so
            // it is set where genuinely quiet rounds live: 14 or fewer is about one round in forty-five.
            bool violent = rd.KnockdownsA + rd.KnockdownsB > 0
                           || ticks.Any(t => t.RockA >= 2 || t.RockB >= 2);
            if (myAll + hisAll <= 14 && !violent)
                return Rotate("cagey",
                    "A cagey round — both men measuring, little committed.",
                    "Not much doing there; a feeling-out round.");

            return null;
        }

        public string Recap(int round, string my, string his, int mine, int theirs, int myScore, int hisScore)
        {
            string card = $"{myScore}–{hisScore}, {mine}–{theirs} landed.";
            if (mine > theirs + 2)
                return Rotate("recapMe",
                    $"End of {round} — {my} takes it. {card}",
                    $"Round {round} to {my}. {card}",
                    $"That's {my}'s round. {card}");
            if (theirs > mine + 2)
                return Rotate("recapHim",
                    $"End of {round} — {his} takes it. {card}",
                    $"Round {round} to {his}. {card}",
                    $"That one belonged to {his}. {card}");
            return Rotate("recapClose",
                $"End of {round} — hard round to split. {card}",
                $"Nothing between them in {round}. {card}",
                $"You could score round {round} either way. {card}");
        }

        /// <summary>Drop the leading article so a punch can be referred to as "the right cross".</summary>
        private static string Strip(string shot) =>
            shot.StartsWith("an ") ? shot[3..] : shot.StartsWith("a ") ? shot[2..] : shot;

        private static readonly string[] BodyWords =
            { "body", "ribs", "liver", "heart", "midsection", "solar plexus", "kidney" };

        private static bool NamesTheBody(string shot) => BodyWords.Any(shot.Contains);
    }
}
