# Fight-night audio

Anything in here **overrides the synthesised layer of the same name** at runtime. No code change and no rebuild
of the audio path — the app checks this folder first and falls back to synthesis for whatever is missing. You
can supply one file, all of them, or none. `Sfx.RealSamples` reports which layers came from disk.

All of them are currently real recordings; the synthesis in `Sfx.cs` stays as the fallback if this folder is
emptied.

## Names

| File | What it is |
|---|---|
| `bed-small-calm` | a club hall between exchanges — murmur, the odd clap |
| `bed-small-hot`  | the same room roused |
| `bed-big-calm`   | an arena ticking over |
| `bed-big-hot`    | an arena going up |
| `thud`, `thud2`, `thud3`, `thud4` | a hard punch landing — cycled, so no two in a row are the same |
| `thud-big`       | a knockdown shot |
| `ooh`            | the crowd's intake of breath when a man is hurt |
| `roar`           | a knockdown or a stoppage |
| `bell`           | between rounds |
| `bell3`          | the final bell |

`.wav`, `.mp3` and `.ogg` are all accepted. The four beds are **looped**, so they must be seamless. Everything
else plays once per event.

The two beds run simultaneously and are crossfaded on an excitement axis, so `-calm` and `-hot` should be the
same room at different temperatures rather than unrelated recordings.

Loudness is the runtime's job, not the file's: `Sfx` scales the bed by the occasion (a club night sits at 0.16,
a unification at 0.72) and crossfades calm against hot. So the four beds are **matched to each other in level**
and differ only in character. Replacing one with a much louder file will break that balance.

## How these were made

The sources are longer recordings, not loops, so each bed is a nine-second window chosen by measurement rather
than by ear: the window with the least internal level variation, sitting nearest the recording's typical level,
and — the part that matters — beginning at the same level it ends at. The head is then crossfaded (2.2 s, equal
power) with the material that *follows* the tail, so the last sample runs into the first as a genuine
continuation of the recording rather than a cut.

Seamlessness was verified by playing each loop twice and asking whether the stretch straddling the join stands
out from the loop's own natural variation. All four sit within 1.7 standard deviations of an ordinary stretch,
i.e. there is nothing at the join to notice.

`ooh` is one recorded voice multiplied into a room full of them — nine copies, fanned in pitch and staggered in
time so no two onsets align, then high-passed. `bell` layers a gong for body under a small bell for shimmer.

**The punches are the same recording reshaped, not a new one.** Measured, the original was not a punch: sixteen
milliseconds of near-silence before the hit — so every shot in the game landed a frame and a half after it was
thrown — and then a ragged ring sustaining at a third of its peak all the way to 160 ms, which is a hall rather
than an impact. The hall is the crowd bed's job, and having it in the punch as well is what made every shot
sound boxy. The lead-in is trimmed and everything past the first 22 ms is pulled onto a decay that is finished
by 130 ms. The attack is untouched and so is the timbre: 90% of the energy now lands by 30 ms instead of 113,
and the glove snap above 2 kHz is left where it was, because brightness was never the problem. The four
variants are the same shape resampled, which pitches and shortens together the way a harder punch actually
differs from a softer one; `thud-big` is the same at 0.84 speed.

**The beds are stereo.** They were mono, and a centred mono crowd is the single biggest reason a room sounds
small. Each is now the original in one ear and the original through three co-prime Schroeder all-passes in the
other — all-passes rather than a delay, because a delay would widen it just as well and then comb-filter the
moment anyone listens in mono. Channel correlation lands between −0.15 and −0.20, where 1.0 is mono.

For reference, real crowd recordings sit at 98–99% of their energy in the 300–3000 Hz voice band with no
measurable sub-bass. The synthesised bed these replaced reached 42–49% voice with 12–20% sub-bass rumble, which
is the whole reason it never passed for a crowd.

## Licensing

These files ship inside the published app and live in a public repository, so only audio that may be
redistributed belongs here. Anything "free for personal use only" is not usable.

**The crowd recordings below are CC-BY 4.0, which obliges this project to carry the credit.** It is reproduced
in the app's About page as well as here — if these files are removed, remove the credit with them; if they are
replaced, update both.

| File | Source | Author | Licence |
|---|---|---|---|
| `bed-small-calm`, `bed-small-hot`, `bed-big-calm`, `bed-big-hot`, `roar` | [Free Crowd Cheering Sounds](https://opengameart.org/content/free-crowd-cheering-sounds) | Gregor Quendel | [CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| `ooh` | [oooooooooooooo](https://opengameart.org/content/oooooooooooooo) | rubberduck | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| `thud`, `bell`, `bell3` | [100 CC0 SFX](https://opengameart.org/content/100-cc0-sfx) | rubberduck | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |

All of the above were modified: excerpted, layered, level-matched, reshaped, stereo-widened and loop-treated as
described above.
