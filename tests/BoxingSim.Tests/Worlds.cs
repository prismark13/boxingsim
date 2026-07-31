using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Tests;

/// <summary>A warmed world, built once and handed out as copies.
///
/// Nearly all the time in this suite went on standing worlds up. Every test that wanted a career simulated a
/// decade of boxing across eleven divisions before it could make its first assertion, and a class with nine
/// assertions did it nine times — which is how the opponent net came to take nine minutes, and a net that
/// takes nine minutes is one nobody runs.
///
/// So the decade is simulated once per player profile and kept as a SAVE. Rehydrating a roster is cheap where
/// simulating one is not, and the save round-trip is already covered by its own test, so a loaded world is a
/// world the game itself considers legitimate.
///
/// NOT for the golden master. Its whole job is to build a world from scratch under a fixed seed and write
/// down what came out; handing it a pre-built one would destroy the thing it measures.</summary>
public static class Worlds
{
    private static readonly Dictionary<(int Potential, WeightClass Div), CareerSave> Warmed = new();
    private static readonly object Gate = new();

    /// <summary>A fresh, independent world at the player's first day. Callers may fight, decline and mutate
    /// it freely — each gets its own copy.</summary>
    public static CareerGame Fresh(int potential = 88, WeightClass div = WeightClass.Middleweight, int seed = 1)
    {
        CareerSave save;
        lock (Gate)   // xUnit runs classes in parallel; the warm-up must happen once, not once per racer
        {
            if (!Warmed.TryGetValue((potential, div), out save!))
            {
                var rng = new Random(potential * 31 + (int)div);
                var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", div, potential);
                var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, div, warmupYears: 12);
                Warmed[(potential, div)] = save = g.ToSave();
            }
        }
        return CareerGame.Load(save, new Random(seed));
    }
}
