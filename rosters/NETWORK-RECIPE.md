# Building a fighter network generically

We built Larry Holmes' network by hand. Here's the repeatable pipeline to do it for **any**
fighter, with almost no manual work. Three steps: **research → build → (optionally) polish.**

The trick: every fighter is **auto-rated from their record + tier**, and a reusable
**overrides library** (`_library.json`) re-applies hand-tuned ratings to the famous names.
So research fills the roster; the library polishes the greats. The library grows once and is
reused for every future network.

---

## Step 1 — Research (one agent call per headliner)

Run a research agent with this prompt, replacing `{HEADLINER}` and the peak window:

> Research the boxer **{HEADLINER}** during their peak era using BoxRec.com and Wikipedia.
> Return ONLY a JSON array. The first entry is {HEADLINER} (their **peak** record — at their
> individual prime, e.g. just before their first defining loss). The rest are the ~20-30
> opponents they fought during that window. Every entry uses this exact shape:
>
> ```json
> { "Name": "", "Nickname": null, "WeightClass": "Heavyweight", "Age": 0,
>   "AutoRate": true, "Tier": "Champion|TopContender|Gatekeeper|Journeyman",
>   "Notable": true,
>   "Wins": 0, "Losses": 0, "Draws": 0, "KnockoutWins": 0 }
> ```
>
> `Tier` = level of competition. `Notable` = true if a famous champion/contender.
> `Age` = their age when they fought {HEADLINER}. Records are facts — verify against BoxRec.

Save the result as `rosters/{headliner}-net.json`. That's it — no hand-rating needed.

## Step 2 — Build the deck + viewer

```powershell
dotnet run --project src/BoxingSim.Cli -- `
  --roster   rosters/{headliner}-net.json `
  --overrides rosters/_library.json `
  --html     rosters/{headliner}-net.html
```

Everyone is auto-rated from their record; anyone in `_library.json` is upgraded to their
curated card (keeping the research record). Open the HTML — done.

## Step 3 (optional) — Polish a new great

If the headliner (or a notable opponent) isn't in the library yet, hand-rate them **once**
by adding an entry to `rosters/_library.json` (ratings + nickname only — no record needed):

```json
{ "Name": "Evander Holyfield", "Nickname": "The Real Deal",
  "Power": 86, "Chin": 88, "Speed": 84, "Defense": 84, "Stamina": 90,
  "Accuracy": 86, "Conditioning": 92, "CutResistance": 78, "Aggression": 86, "Heart": 96 }
```

Re-run Step 2 and they'll be upgraded everywhere, in this network and every future one.

---

## Why this is generic

| Concern | How it's handled |
| --- | --- |
| Filling 20-30 opponents | Auto-rated from record + tier (`RatingsEstimator`) |
| Famous names needing real cards | `_library.json` overrides, matched by name |
| KO% varies by era/division | Tier + division-relative KO baseline in the estimator |
| Peak vs career records | Research supplies the record; overrides never touch it |
| Name spelling / nicknames | Matched on first+last word, ignoring quotes/middle nicknames |

To do a whole era, run Steps 1-2 for each headliner and merge the JSON files (dedupe by name).
