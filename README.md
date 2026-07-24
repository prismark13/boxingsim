# Boxing Simulator

A Title-Bout–inspired boxing **career/league** simulator in C# (.NET 10). It generates a
world of ~1000 rated fighters across eight weight classes and simulates seasons of bouts,
with evolving rankings, champions, aging, retirements and a fresh stream of prospects.

## Quick start

```powershell
# from the solution root
dotnet run --project src/BoxingSim.Cli -- --boxers 1000 --seasons 10 --seed 42
```

### Options

| Option | Default | Description |
| --- | --- | --- |
| `-b, --boxers <n>` | 1000 | Number of fighters in the world |
| `-s, --seasons <n>` | 10 | Seasons to simulate |
| `--seed <n>` | 12345 | RNG seed (same seed → identical world) |
| `--no-feature` | off | Skip the round-by-round featured bout |
| `--calibrate` | off | Print finish-rate calibration by division and exit |
| `-h, --help` | | Show usage |

## How it works

### Ratings (1–100, higher is always better)
`Power, Chin, Speed, Defense, Stamina, Accuracy, Conditioning, CutResistance, Aggression, Heart`
— combined into a single weighted `Overall`. Inspired by Title Bout's fighter categories but
kept clean enough to balance.

### Fight engine (`BoxingSim.Core/Engine/FightEngine.cs`)
Resolves a bout round by round:
- each fighter throws a volume of punches based on aggression/stamina (and fades with fatigue);
- each punch's connect chance is a logistic of attacker accuracy+speed vs defender defense+speed;
- power shots accumulate **damage**, which drives knockdowns and stoppages;
- knockdowns trigger a get-up check (conditioning + heart vs damage); three in a round = TKO;
- cuts can force a doctor's stoppage, more likely late;
- bouts going the distance are scored by **three judges** on the 10-point-must system →
  KO / TKO / UD / SD / D.

Finish rates are tuned to be believable per division (heavyweight ≈ 56% inside the distance
down to flyweight ≈ 10%) — verify with `--calibrate`.

### League & careers (`BoxingSim.Core/League/`)
- 8 divisions, each with a champion and an **Elo-style ranking** updated after every bout.
- Each season: a title fight (champion vs #1 contender) plus an undercard of adjacent-ranked
  matchups, then aging, retirements, and new prospects to keep the roster near target.
- Prospects improve toward their **potential**; veterans decline past their **peak age**
  (athleticism first, ring craft last); hard KO losses permanently shave the chin.

## Project layout

```
src/BoxingSim.Core    domain model, fight engine, league, career progression
src/BoxingSim.Cli     console front-end (season summaries, standings, featured bout)
tests/BoxingSim.Tests xUnit tests for the engine and league
```

## Tests

```powershell
dotnet test
```

## Ideas for next steps
- Fighter styles (boxer / brawler / counter-puncher) that interact (styles make fights).
- Persistence: save/load a world to JSON so careers continue across runs.
- Hall of Fame / all-time records, win streaks, title-defense counts.
- Multiple sanctioning bodies and unification bouts.
- Injuries, weight-class jumps, and trainer/camp effects.
