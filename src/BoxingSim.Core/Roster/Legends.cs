using BoxingSim.Core.Model;

namespace BoxingSim.Core.Roster;

/// <summary>
/// A small demo deck of all-time greats. The NAMES are historical fact, but every rating
/// here is an original, subjective estimate by this project — they are not taken from any
/// published boxing game or product. Treat them as a starting point and tune to taste.
/// </summary>
public static class Legends
{
    public static List<FighterDefinition> Deck() => new()
    {
        new FighterDefinition
        {
            Name = "Sugar Ray Robinson", Nickname = "Sugar", WeightClass = WeightClass.Middleweight, Age = 30,
            Power = 88, Chin = 86, Speed = 95, Defense = 90, Stamina = 88,
            Accuracy = 94, Conditioning = 88, CutResistance = 80, Aggression = 78, Heart = 92,
            Wins = 173, Losses = 19, Draws = 6, KnockoutWins = 108
        },
        new FighterDefinition
        {
            Name = "Muhammad Ali", Nickname = "The Greatest", WeightClass = WeightClass.Heavyweight, Age = 28,
            Power = 82, Chin = 92, Speed = 94, Defense = 90, Stamina = 90,
            Accuracy = 88, Conditioning = 89, CutResistance = 78, Aggression = 72, Heart = 96,
            Wins = 56, Losses = 5, Draws = 0, KnockoutWins = 37
        },
        new FighterDefinition
        {
            Name = "Mike Tyson", Nickname = "Iron", WeightClass = WeightClass.Heavyweight, Age = 22,
            Power = 97, Chin = 84, Speed = 90, Defense = 82, Stamina = 80,
            Accuracy = 86, Conditioning = 82, CutResistance = 75, Aggression = 96, Heart = 80,
            Wins = 50, Losses = 6, Draws = 0, KnockoutWins = 44
        },
        new FighterDefinition
        {
            Name = "Floyd Mayweather", Nickname = "Money", WeightClass = WeightClass.Welterweight, Age = 30,
            Power = 68, Chin = 86, Speed = 93, Defense = 98, Stamina = 90,
            Accuracy = 92, Conditioning = 92, CutResistance = 88, Aggression = 55, Heart = 86,
            Wins = 50, Losses = 0, Draws = 0, KnockoutWins = 27
        },
        new FighterDefinition
        {
            Name = "Manny Pacquiao", Nickname = "Pac-Man", WeightClass = WeightClass.Welterweight, Age = 30,
            Power = 88, Chin = 82, Speed = 94, Defense = 80, Stamina = 90,
            Accuracy = 88, Conditioning = 88, CutResistance = 80, Aggression = 92, Heart = 92,
            Wins = 62, Losses = 8, Draws = 2, KnockoutWins = 39
        },
        new FighterDefinition
        {
            Name = "Roberto Duran", Nickname = "Hands of Stone", WeightClass = WeightClass.Lightweight, Age = 27,
            Power = 92, Chin = 88, Speed = 86, Defense = 84, Stamina = 86,
            Accuracy = 88, Conditioning = 84, CutResistance = 82, Aggression = 94, Heart = 90,
            Wins = 103, Losses = 16, Draws = 0, KnockoutWins = 70
        },
        new FighterDefinition
        {
            Name = "Joe Louis", Nickname = "The Brown Bomber", WeightClass = WeightClass.Heavyweight, Age = 28,
            Power = 93, Chin = 85, Speed = 84, Defense = 82, Stamina = 84,
            Accuracy = 92, Conditioning = 84, CutResistance = 82, Aggression = 86, Heart = 88,
            Wins = 66, Losses = 3, Draws = 0, KnockoutWins = 52
        },
        new FighterDefinition
        {
            Name = "Marvin Hagler", Nickname = "Marvelous", WeightClass = WeightClass.Middleweight, Age = 30,
            Power = 90, Chin = 94, Speed = 84, Defense = 86, Stamina = 90,
            Accuracy = 86, Conditioning = 90, CutResistance = 80, Aggression = 90, Heart = 94,
            Wins = 62, Losses = 3, Draws = 2, KnockoutWins = 52
        },
    };
}
