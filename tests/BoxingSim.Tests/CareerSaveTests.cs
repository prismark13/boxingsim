using System.Text.Json;
using System.Text.Json.Serialization;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

public class CareerSaveTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void CareerRoundTripsThroughSaveAndStaysPlayable()
    {
        var rng = new Random(5);
        var player = CareerGame.CreatePlayer(rng, "Test Fighter", "USA", WeightClass.Heavyweight, 84);
        var game = new CareerGame(1970, player, Array.Empty<Boxer>(), rng);

        for (int i = 0; i < 8 && game.Offer is not null; i++) game.TakeOffer();

        // Serialize with the exact options the app uses, then reload.
        var json = JsonSerializer.Serialize(game.ToSave(), Opts);
        var save = JsonSerializer.Deserialize<CareerSave>(json, Opts)!;
        var loaded = CareerGame.Load(save, new Random(9));

        Assert.Equal(game.Year, loaded.Year);
        Assert.Equal(game.Active.Count(), loaded.Active.Count());
        Assert.Equal(game.Player.Overall, loaded.Player.Overall);
        Assert.Equal(game.Player.Record.Wins, loaded.Player.Record.Wins);
        Assert.Equal(game.Player.Record.Losses, loaded.Player.Record.Losses);
        Assert.Equal(game.Champion?.Id, loaded.Champion?.Id);
        Assert.Equal(game.Offer?.Opponent.Id, loaded.Offer?.Opponent.Id);

        // The player's fights go through the full engine, so their ledger must carry per-round detail
        // (scorecards + round-by-round stats) across the save round-trip.
        var pre = game.Player.History;
        var post = loaded.Player.History;
        Assert.Equal(pre.Count, post.Count);
        Assert.True(pre.Any(h => h.Rounds is { Count: > 0 }), "expected at least one bout with round-by-round data");
        for (int i = 0; i < pre.Count; i++)
        {
            Assert.Equal(pre[i].Cards, post[i].Cards);
            Assert.Equal(pre[i].Note, post[i].Note);
            Assert.Equal(pre[i].Rounds?.Count ?? 0, post[i].Rounds?.Count ?? 0);
        }

        // The loaded career must keep playing — the offer opponent must be the same instance in the roster.
        if (loaded.Offer is not null)
        {
            Assert.Contains(loaded.Active, b => b.Id == loaded.Offer.Opponent.Id);
            int before = loaded.Player.Record.Wins + loaded.Player.Record.Losses + loaded.Player.Record.Draws;
            loaded.TakeOffer();
            int after = loaded.Player.Record.Wins + loaded.Player.Record.Losses + loaded.Player.Record.Draws;
            Assert.Equal(before + 1, after);
        }
    }
}
