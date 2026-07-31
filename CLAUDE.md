# The Final Bell

A boxing career and world simulator in C# (.NET 10). You create a fighter and steer a career — take or turn
down the fights offered, climb the rankings, win a belt — inside a world that runs on its own: every division
active at once, real fighters debuting at their historical years, generated prospects arriving every year,
everyone ageing, retiring, and being ranked, honoured and forgotten around you.

The shipped product is the **WPF desktop app**. Everything else is either a tool or dead weight kept for a
reason.

## Layout

| Project | What it is |
|---|---|
| `src/BoxingSim.Core` | The whole simulation. No UI, no package dependencies. Keep it that way. |
| `src/BoxingSim.Desktop` | The product: WPF, .NET 10 Windows. Ships as `BoxingSim.exe`, titled "The Final Bell". |
| `src/BoxingSim.Cli` | A harness for exporting reports and running bulk sims. Not shipped. |
| `tests/BoxingSim.Tests` | xUnit. The entire safety net. |
| `data/fighters.json` | The fighter roster, owned by no project. Desktop embeds it *and* copies it beside the exe; the tests copy it too. |
| `rosters/` | Authoring data and exported reports. Not compiled. |

`Core` depends on nothing — no `PackageReference`, no UI. Its layering runs `Model` ←
`Analysis`/`Generation` ← `Engine` ← `Career`/`League`. The one known cycle is `Career ↔ League`, which
references itself both ways; don't add more, and untangling that one is welcome. `Observable` lives in Core
deliberately (see its docstring) — that is not licence to move anything else UI-shaped in there.

## Build and test

```bash
dotnet build BoxingSim.slnx -c Debug --nologo
dotnet test tests/BoxingSim.Tests/BoxingSim.Tests.csproj --nologo
```

The suite is ~78 tests and takes **about two minutes**. Run it before claiming anything works. CI
(`.github/workflows/ci.yml`) runs build + test on Windows for every push and PR, and uploads the golden-master
dumps as an artifact when they move.

Two practical notes:

- **Don't build while a test run is live.** `testhost` locks `BoxingSim.Core.dll` and the copy fails with
  MSB3026. If you want parallel work, use a `git worktree` so each has its own `bin`/`obj`.
- `out/`, `out-msix/`, `out-release/` are release assets and gitignored. Never commit build output.

## The golden master — read this before changing simulation code

`tests/BoxingSim.Tests/GoldenMasterTests.cs` runs a fixed seed through a career and a universe, flattens
everything observable about the finished worlds into one string, and asserts a SHA fingerprint of it. It is
the reason this codebase can be refactored at all.

The rule for every change to `Core`:

1. **Decide up front whether the hash is allowed to move**, and say so in the commit message.
2. A **pure refactor must not move it.** If it does, you changed behaviour without meaning to — find out what
   before you re-baseline. This property is what proved the clock refactor correct.
3. A **deliberate behaviour change moves it**, and that is fine. Read the dumped fingerprint
   (`golden-*.txt`, written beside the test binary), satisfy yourself the diff is the change you meant, then
   update the expected hash and record in the commit message *why* the world is different.
4. **Never mix the two in one commit.** If a refactor and a behaviour change land together, the hash moved and
   nobody can say which one moved it.
5. The hash also moves if the **order random numbers are drawn** changes, even when behaviour is conceptually
   identical. Drawing a card nobody sees, or an extra `_rng.Next()` in a loop, shifts every result after it.

## Invariants — each of these cost a real bug to learn

- **Determinism.** `Random` is injected; simulation code never calls `new Random()`. The one exception is the
  venue pick, which is deliberately seeded from the fight date so the same night is always in the same
  building. Don't add others.
- **The world clock moves in one place.** `Date` is written only through `AdvanceClockTo`, which ignores a
  backwards move rather than throwing — a clock anomaly should not end a career somebody has played for hours,
  so the tests are where that is loud, not the runtime. A bout carries its own night as an `on:` argument; it
  must never set the clock to stage itself. That conflation once meant nobody in the world aged, for the life
  of the project, and made a "week" of waiting advance about a month.
- **No ambient "current division".** There used to be a `_cursor` that every caller had to remember to set and
  restore. It is gone. Weight class is a parameter.
- **`_log.Count` is not a position.** The event log is a capped FIFO (1,500), so once full its length stops
  moving. Marks are taken against `_logWrites`. Using the length as a position silently killed the news feed
  for every career past about fifteen years. The same applies to any other capped list — fighter ledgers are
  capped at 60 bouts.
- **Saves are DTOs, not domain objects.** `CareerSave` and its `*Save` types are a deliberate boundary so the
  model can be refactored without breaking existing careers. Don't serialise `Boxer` directly. The save is
  written to a temp file and moved into place; a corrupt one is renamed `.broken`, never deleted.
- **One scorecard format.** Cards are written and read through `ScoreCards`. There were once two hand-rolled
  parsers and one writer, and they disagreed.

## Conventions

**Commits.** Subject lines are a sentence describing what changed, in plain English — "The world ages again.
Nobody in it had aged since the sim was written." The body explains the cause, not just the fix, and says
explicitly what was tried and did NOT work. Behaviour changes and pure refactors go in separate commits. End
with:

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

**Comments.** This codebase explains *why*, and records the bug that motivated the code. That is why a 10k-line
simulation is navigable by one person. Match it — a comment that restates the code is worse than none, and a
comment that says what used to be wrong is worth five that say what is right.

**Tests.** Two kinds, and both matter: property tests that assert a rule (nobody climbs past his frame, a
champion is never offered without his belt on the line) and the golden master that pins behaviour. When you fix
a bug, pin the invariant, not the symptom.

Use `Worlds.Fresh(...)` rather than standing a world up by hand — it warms one world per player profile and
hands out copies, which is the difference between a two-minute suite and a nine-minute one. The golden master
must **not** use it; building from scratch is the thing it measures.

## Gotchas

- **File encoding.** Source is UTF-8 and the commentary is full of em dashes. A PowerShell 5.1
  `Get-Content`/`Set-Content` round trip reads as the system codepage and turns every one of them into
  mojibake — this shipped in a release once. `EncodingTests` guards it now, but prefer the editing tools over
  shell round trips for any file containing prose.
- **`Boxer.WithRatings` is a shallow clone.** The copy shares `Record` and `History` with the real fighter. It
  is for handing a what-if fighter to the engine, and must never enter the world.
- **Fighter identity is doubled.** Live objects key on `int Id`; the persisted ledger and `BoutRef` key on
  name. Generated names are reserved against collisions to make that safe — don't undermine the reservation.
