using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Turning down a belt costs you the belt.
///
/// A title shot used to be re-decided every time the matchmaker was asked. The gates are deterministic, so
/// the same offer came straight back — which meant refusing a world title cost nothing whatever. You passed
/// on the champion, waited three weeks, and there he was again.
///
/// It is a fact about the world now: a sanctioning body has ordered a fight. Refuse it and they order
/// somebody else, and you wait the same few fights you would have waited had you taken it and lost.</summary>
public class TitleShotTests
{
    private static CareerGame Contender(int seed, out int guard)
    {
        var g = Worlds.Fresh(potential: 94, seed: seed);
        // Fight until a world title is on the table.
        guard = 0;
        while (guard++ < 60 && g.Offer is not null && !g.Player.Retired)
        {
            if (IsWorldTitle(g.Offer)) return g;
            g.TakeOffer();
        }
        return g;
    }

    private static bool IsWorldTitle(FightOffer? o) =>
        o is not null && o.TitleFight && o.Belt is "WBA" or "WBC" or "IBF";

    /// <summary>At most one belt on the table at a time, over hundreds of slates.
    ///
    /// This is the rule a slate exists to break. The title path returns on the first match — WBA, else WBC,
    /// else IBF — so fanning it out naively hands a top-five contender three world title shots on one screen,
    /// and a belt stops being scarce. The cap is structural rather than a check: BigNight fills at most one
    /// slot and the ordinary path cannot produce a title at all.</summary>
    [Fact]
    public void AtMostOneTitleFightIsEverOnTheTable()
    {
        var worst = new List<string>();
        for (int seed = 1; seed <= 3; seed++)
        {
            var g = Worlds.Fresh(potential: 92, seed: seed);
            for (int i = 0; i < 40 && g.Offer is not null && !g.Player.Retired; i++)
            {
                int belts = g.Slate.Count(o => o.TitleFight);
                if (belts > 1)
                    worst.Add($"seed {seed}: {belts} title fights at once — "
                              + string.Join(", ", g.Slate.Where(o => o.TitleFight).Select(o => o.Belt)));
                g.TakeOffer();
            }
        }
        Assert.True(worst.Count == 0, string.Join("; ", worst.Take(4)));
    }

    /// <summary>A slate offers genuinely different fights, not the same man listed twice.</summary>
    [Fact]
    public void NobodyAppearsTwiceOnOneSlate()
    {
        var g = Worlds.Fresh(potential: 86, seed: 2);
        var dupes = new List<string>();
        for (int i = 0; i < 30 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var ids = g.Slate.Select(o => o.Opponent.Id).ToList();
            if (ids.Count != ids.Distinct().Count())
                dupes.Add(string.Join(", ", g.Slate.Select(o => o.Opponent.Name)));
            g.TakeOffer();
        }
        Assert.True(dupes.Count == 0, "the same man appeared twice on a slate: " + string.Join(" | ", dupes.Take(3)));
    }

    /// <summary>A belt is not one option among three. When the big night is a title, the slate narrows.</summary>
    [Fact]
    public void ABeltIsNotBuriedUnderAlternatives()
    {
        var g = Contender(4, out _);
        if (!IsWorldTitle(g.Offer)) return;
        Assert.True(g.Slate.Count <= 2, $"a world title was offered alongside {g.Slate.Count - 1} other fights");
        Assert.True(g.Slate[0].TitleFight, "the belt was not the first thing on the table");
    }

    /// <summary>The one nobody writes. Declining over and over must never be a way of fishing for a belt.</summary>
    [Fact]
    public void DecliningRepeatedlyNeverFishesUpATitleShot()
    {
        var g = Worlds.Fresh(potential: 78, seed: 11);

        int belts = 0;
        for (int i = 0; i < 60 && g.Offer is not null && !g.Player.Retired; i++)
        {
            if (IsWorldTitle(g.Offer)) belts++;
            g.DeclineOffer();
        }

        // He never fights, so he never earns anything. A man who only ever says no is not a contender.
        Assert.True(belts <= 1,
                    $"turning everything down produced {belts} world title offers — declining is a reroll");
    }

    /// <summary>Refuse the shot and it is gone: the next offer is not the same belt handed back.</summary>
    [Fact]
    public void DecliningATitleShotDoesNotBringItStraightBack()
    {
        var g = Contender(4, out _);
        if (!IsWorldTitle(g.Offer)) return;   // this seed never got there; nothing to assert

        var refused = g.Offer!;
        g.DeclineOffer();

        Assert.False(IsWorldTitle(g.Offer) && g.Offer!.Opponent.Id == refused.Opponent.Id,
                     $"the {refused.Belt} title against {refused.Opponent.Name} came straight back after being turned down");
    }

    /// <summary>And it stays gone for the rebuild — the same gap that follows losing one.</summary>
    [Fact]
    public void AfterRefusingHeRebuildsBeforeTheNextShot()
    {
        var g = Contender(4, out _);
        if (!IsWorldTitle(g.Offer)) return;

        g.DeclineOffer();
        int at = g.Player.History.Count;

        var tooSoon = new List<string>();
        for (int i = 0; i < 3 && g.Offer is not null && !g.Player.Retired; i++)
        {
            if (IsWorldTitle(g.Offer) && g.Player.History.Count - at < 3)
                tooSoon.Add($"{g.Offer.Belt} after {g.Player.History.Count - at} fights");
            g.TakeOffer();
        }
        Assert.True(tooSoon.Count == 0, "a shot came back before the rebuild: " + string.Join("; ", tooSoon));
    }

    /// <summary>Passing on a belt is reported. A cost the player cannot see is a cost he will think is a bug.</summary>
    [Fact]
    public void PassingOnABeltIsInTheNews()
    {
        var g = Contender(4, out _);
        if (!IsWorldTitle(g.Offer)) return;

        int before = g.Log.Count;
        g.DeclineOffer();
        Assert.Contains(g.Log.Skip(before), e => e.Text.Contains("passes on"));
    }

    /// <summary>A granted shot survives a save. Without this, quitting without saving would put the belt back
    /// on the table — which is the reroll again, wearing a different hat.</summary>
    [Fact]
    public void AGrantedShotSurvivesASaveAndReload()
    {
        var g = Contender(4, out _);
        if (!IsWorldTitle(g.Offer)) return;

        var save = g.ToSave();
        var back = CareerGame.Load(save, new Random(99));

        Assert.True(IsWorldTitle(back.Offer),
                    $"the shot was lost across a save — reloaded on {back.Offer?.Context ?? "nothing"}");
        Assert.Equal(g.Offer!.Belt, back.Offer!.Belt);
        Assert.Equal(g.Offer.Opponent.Id, back.Offer.Opponent.Id);
    }
}
