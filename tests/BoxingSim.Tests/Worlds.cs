using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
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
    /// <summary>One warm-up per profile, and — the part that matters — profiles warm CONCURRENTLY.
    ///
    /// This was a dictionary behind one lock, which makes every profile queue behind every other. That was
    /// nearly free while two classes used it; with the suite on it there are eight profiles, and eight
    /// warm-ups taken one at a time on a six-way parallel runner is most of them waiting. A Lazy per key
    /// keeps the "warm once" guarantee — ExecutionAndPublication means one racer builds and the rest get his
    /// copy — while letting a different profile be built on another thread at the same time.</summary>
    private static readonly ConcurrentDictionary<(int Potential, WeightClass Div), Lazy<CareerSave>> Warmed = new();

    /// <summary>A fresh, independent world at the player's first day. Callers may fight, decline and mutate
    /// it freely — each gets its own copy.</summary>
    public static CareerGame Fresh(int potential = 88, WeightClass div = WeightClass.Middleweight, int seed = 1)
    {
        var save = Warmed.GetOrAdd((potential, div), key => new Lazy<CareerSave>(() =>
        {
            var rng = new Random(key.Potential * 31 + (int)key.Div);
            var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", key.Div, key.Potential);
            var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, key.Div, warmupYears: 12);
            return g.ToSave();
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        return CareerGame.Load(save, new Random(seed));
    }
}
