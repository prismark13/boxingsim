# Fight-night audio

Anything dropped in here **overrides the synthesised layer of the same name** at runtime. No code change and
no rebuild of the audio path — the app checks this folder first and falls back to synthesis for whatever is
missing. You can supply one file, all of them, or none.

## Names

| File | What it is |
|---|---|
| `bed-small-calm` | a club hall between exchanges — murmur, the odd clap |
| `bed-small-hot`  | the same room roused |
| `bed-big-calm`   | an arena ticking over |
| `bed-big-hot`    | an arena going up |
| `thud`           | a hard punch landing |
| `ooh`            | the crowd's intake of breath when a man is hurt |
| `roar`           | a knockdown or a stoppage |
| `bell`           | between rounds |
| `bell3`          | the final bell |

`.wav`, `.mp3` and `.ogg` are all accepted. The four beds are **looped**, so they must be seamless — trim on a
zero crossing and fade the last few milliseconds into the first. Everything else plays once per event.

The two beds run simultaneously and are crossfaded on an excitement axis, so `-calm` and `-hot` should be the
same room at different temperatures rather than unrelated recordings. Different lengths are good: they drift
against each other and the repeat stops being audible.

## Licensing

These files ship inside the published app and live in a public repository, so only use audio you are free to
redistribute — CC0 or an equivalent public-domain dedication. Attribution-required licences (CC-BY) need the
credit carried somewhere visible in the app. Anything "free for personal use only" is not usable here.

If you add third-party audio, record what it is and where it came from below.

| File | Source | Licence |
|---|---|---|
| _(none yet — all layers are synthesised)_ | | |
