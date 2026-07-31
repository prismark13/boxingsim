using System;
using System.Linq;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>The judges' cards survive the round trip into a fight ledger and back out.
///
/// They did not. The cards were written joined with " · " and one of the two readers split on a comma, which
/// nothing has ever written — so every stored decision read back empty and watching an old fight again reached
/// the verdict with no scoring at all. It was invisible because the OTHER reader was right, so the record page
/// showed the cards while the playback of the very same bout did not.</summary>
public class ScoreCardsTests
{
    [Fact]
    public void WhatIsWrittenIsWhatIsRead()
    {
        var cards = new (int A, int B)[] { (116, 112), (115, 113), (114, 114) };
        var back = ScoreCards.Read(ScoreCards.Write(cards));
        Assert.Equal(cards, back);
    }

    /// <summary>A card read from the other man's corner is the same fight with the numbers swapped.</summary>
    [Fact]
    public void BothCornersAgreeOnTheFight()
    {
        var mine = new (int A, int B)[] { (117, 111), (115, 113) };
        var his = mine.Select(c => (c.B, c.A));
        var back = ScoreCards.Read(ScoreCards.Write(his));
        Assert.Equal(new[] { (111, 117), (113, 115) }, back);
    }

    [Fact]
    public void NothingScoredReadsAsNoCardsRatherThanThrowing()
    {
        Assert.Empty(ScoreCards.Read(null));
        Assert.Empty(ScoreCards.Read(""));
        Assert.Empty(ScoreCards.Read("   "));
        Assert.Empty(ScoreCards.Read("KO rd4"));
    }

    /// <summary>An older save, or a roster written by hand, may separate them with a comma. That is not the
    /// format any more, but a reader losing a fight's scoring over punctuation is the bug being fixed.</summary>
    [Fact]
    public void AnOlderSeparatorStillReads()
    {
        Assert.Equal(new[] { (116, 112), (115, 113) }, ScoreCards.Read("116-112, 115-113"));
        Assert.Equal(new[] { (116, 112) }, ScoreCards.Read("116–112"));   // en dash
    }
}
