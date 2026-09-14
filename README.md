# Actual Resolution

An Atomcraft mod that draws the game at your window's resolution instead of drawing a fixed 1600x900 frame and letting the engine rescale it.

Requires [GodotMonoModLoader](https://github.com/sacroimper/GodotMonoModLoader). Drop `ActualResolution.zip` in your `Mods/` folder.

## What it changes

| | Shipped game | With this mod |
| --- | --- | --- |
| Render target | always 1600x900, rescaled to the window | the window's size (or a whole fraction of it), blitted by a whole number |
| UI | 1600x900 layout, rescaled with the frame | same layout, scaled by the same factor the blit used to apply |
| Widest view | ~265 cells across | ~265 cells across |
| Zoom-in limit | 1.5, whatever the window | scaled with the frame, so the closest view looks the same |
| Zoom feel | smooth and continuous | unchanged |

## Settings

Written to `ActualResolution.json` in the game's user data directory the first time the mod runs, with a comment beside each key. Read at startup only.

| Key | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | `false` turns the whole mod off without uninstalling it |
| `renderDivisor` | `0` | Window pixels per rendered pixel. `0` picks 1 below 1440p and 2 above. `1` is the sharpest; `2` renders a smaller frame and doubles it, which is exact and much cheaper on a 4K screen |
| `preserveMaxZoom` | `true` | Scales the zoom-in limit with the frame so the closest view keeps the size it has in the shipped game |
| `integerLimits` | `true` | Puts the widest and the closest view on a whole number of pixels per cell |
| `uiScale` | `0` | What the game's fixed 1600x900 UI layout is scaled by. `0` reproduces what the engine's blit was doing. `1` draws it at the frame's own resolution: sharp, and smaller |

## Building and running

```sh
cp harness.conf.example harness.conf   # then edit it
./build.sh                             # Debug, the everyday loop
./build.sh --release --install         # what a release is cut from
./run-tests.sh                         # the arithmetic, headless
./run-tests.sh --headful               # and the tests that judge what is on screen
./play.sh                              # a real game with only this mod loaded
./play.sh --verify                     # boot it headless and confirm it loaded
```

To exercise the interesting case, run the display tests at a resolution the shipped game cannot hit exactly:

```sh
XVFB_GEOMETRY=1920x1080x24 ./run-tests.sh --headful
```

Tests run inside the real game through the [Atomcraft TestHarness](https://github.com/sparr/atomcraft-mod-TestHarness), against a patched *copy* of the install in a throwaway prefix, so your saves and your real game are never touched. `harness.conf` names the pinned harness release; there are no path defaults, because a path guessed from a sibling directory goes stale silently the first time that directory is renamed. Everything stays under a test root private to this project.

**Build releases with `--release`.** Debug is the default because that is what you want while working, but a Debug assembly carries `DebuggableAttribute` with `DisableOptimizations`, which turns the JIT off for it entirely.

## Layout

| | |
| --- | --- |
| [`src/`](src/) | the mod &rarr; `build/ActualResolution.zip` |
| [`test/`](test/) | its tests &rarr; `build/ActualResolution.Test.zip` |
| [`minimal/`](minimal/) | the mechanism in one file, not built and not equivalent — read its header |
| [`lib/`](lib/) | shell helpers shared by the three scripts |

The tests are a peer mod, not a module of this one: the loader treats a missing dependency as an error, so a test module shipped inside this zip would show a red entry in the loader report for every player who had not also installed `TestHarness`.

## Tests

Nine are arithmetic and run headless, so a game update that moves the camera's constants fails the ordinary suite. Four need a display.

- `TheFrameReachesTheWindowUnresampled` measures the blit rather than looking at it, because a viewport readback samples the render target *before* the engine scales it to the window: a frame about to be resampled reads back perfect. Nothing inside the game can photograph this defect, which is why it went unnoticed.
- `TheUiIsScaledTheWayTheEngineUsedTo` and `TheGameFillsTheFrameItIsGiven` cover the half of the job that is not the world. The second also writes the frame out as an artifact, because "the UI is laid out sensibly" is not a thing a test can assert and is a thing somebody should look at after a game update.
- `TheWidestViewShowsTheSameWorldAtEveryResolution` is the invariant that makes resizing the frame safe at all.

## Compatibility

Nothing here touches the simulation, so this mod cannot change what the world does or desync a multiplayer session.

**The TestHarness's own `OverlayTests.RaisingTheLimitRaisesTheGamesOwnClamp` fails while this mod is installed.** It asserts that an unpatched camera stops zooming in at exactly 1.5, and `preserveMaxZoom` moves that limit on purpose. `./run-tests.sh` runs only this mod's own tests, so it does not show up there; it appears under `--all`, where `--atomtest-exclude=RaisingTheLimit` or `preserveMaxZoom: false` gets it green.

Built against Steam buildid **25276035** (`Atomcraft.dll` md5 `63e690c552b9252677ae5faa92549e1d`), and the test suite is run against a patched copy of that same build.

## What it patches

| Target | Why |
| --- | --- |
| `Game._Ready` (postfix) | size the frame at startup, and subscribe to the window's `size_changed` signal |
| `SaveData_Device.ApplySettings` (postfix) | the game's own "the player changed a setting" event |
| `FollowCam.RecalculateMinZoom` (postfix) | recompute the widest view, and the background's scale, for a frame that is not 1600x900 |
| `FollowCam.CameraCellWidth` / `CameraCellHeight` (getters) | the cell counts the camera clamps its own position with, likewise |
| `FollowCam.IncreaseZoom` (prefix and postfix) | move the zoom-in limit by biasing the target around the game's own clamp, rather than reimplementing it |

Nothing is replaced: every patch is a prefix or postfix around the game's own arithmetic, so a retune of the zoom speed or the camera constants is inherited rather than overwritten.

## License

MIT. See [LICENSE](LICENSE)
