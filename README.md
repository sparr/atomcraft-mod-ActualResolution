# Actual Resolution

An Atomcraft mod that draws the game at your window's resolution instead of drawing a fixed 1600x900 frame and letting the engine rescale it.

`project.binary` sets `display/window/stretch/mode = viewport` over a viewport of 1600x900, so the game renders every frame into a target of that size whatever the window is, and the engine rescales the finished image at the end. At the shipped default window of 1280x720 that is 0.8, which drops one row in five. At 1920x1080 it is exactly 1.2, which doubles one row in five. Nothing drawn inside the frame can compensate, because the resample happens after everything in it has been drawn.

Requires [GodotMonoModLoader](https://github.com/sacroimper/GodotMonoModLoader). Drop `ActualResolution.zip` in your `Mods/` folder.

**It works on its own, and it is only half the problem.** This mod delivers the drawn frame to the screen intact. Whether a *world pixel* inside that frame is a square block is a separate question, and the answer in the shipped game is usually no: the camera's zoom rests wherever the player let go of the key, so a cell covers a fractional number of pixels and some cells come out a pixel wider than others. Its sibling **IntegerZoom** fixes that half. Either is useful alone; together they put a square world pixel on the screen.

## What it changes

| | Shipped game | With this mod |
| --- | --- | --- |
| Render target | always 1600x900, rescaled to the window | the window's size (or a whole fraction of it), blitted by a whole number |
| UI | 1600x900 layout, rescaled with the frame | same layout, scaled by the same factor the blit used to apply |
| Widest view | ~265 cells across | ~265 cells across |
| Zoom-in limit | 1.5, whatever the window | scaled with the frame, so the closest view looks the same |
| Zoom feel | smooth and continuous | unchanged |

The last three rows are where most of the code goes. **Every constant the camera was tuned with is a measurement of a 1600x900 frame**, and drawing into a different one silently moves all of them. Left alone, the camera would be allowed to zoom out until it was looking past the window of world the game actually renders (black edges), the closest view would be 17% further out than the game shipped, and the parallax background would stop covering the screen.

## The UI, and what is still imperfect

The game lays its UI out in a **fixed 1600x900 Control** with top-left anchors: everything in it is positioned absolutely inside that box, down to `UpperRightHUDElements` sitting at x=1600 because that is where the right edge was assumed to be. It relied entirely on the frame being rescaled to the window to make that fill the screen. Take the rescale away and the HUD stops three hundred pixels short of the bottom-right corner.

So the mod applies the scale the blit used to apply, to that one Control, with the same uniform factor and centering the engine's `stretch/aspect = keep` would have used. The UI therefore comes out exactly as it does in the shipped game — including being resampled by a fraction, which is the very thing this mod removes from the world. **The UI is no better than before; it is the world that gains.** Making the UI itself land on whole pixels means laying it out against the frame rather than scaling it, which is a different job and not attempted here. `uiScale: 1` is available in the meantime for anyone who would rather have a small, sharp UI than a correctly-sized soft one.

## Settings

Written to `ActualResolution.json` in the game's user data directory the first time the mod runs, with a comment beside each key. Read at startup only.

| Key | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | `false` turns the whole mod off without uninstalling it |
| `renderDivisor` | `0` | Window pixels per rendered pixel. `0` picks 1 below 1440p and 2 above. `1` is the sharpest; `2` renders a smaller frame and doubles it, which is exact and much cheaper on a 4K screen |
| `preserveMaxZoom` | `true` | Scales the zoom-in limit with the frame so the closest view keeps the size it has in the shipped game |
| `integerLimits` | `true` | Puts the widest and the closest view on a whole number of pixels per cell |
| `uiScale` | `0` | What the game's fixed 1600x900 UI layout is scaled by. `0` reproduces what the engine's blit was doing. `1` draws it at the frame's own resolution: sharp, and smaller |

`integerLimits` is worth a word, because it is a small change that does most of the work. The game comes to rest on its two zoom limits and, reliably, nowhere else: the widest view is where a session starts and where zooming out stops, the closest is where zooming in stops, and everywhere in between is wherever the player happened to release the key. Rounding just those two puts the two views a player actually sits at on exact, square world pixels, and it costs at most half a pixel per cell of the framing the shipped game would have given. It does not make every zoom whole — that is IntegerZoom's job, and the two compose, since quantizing an already-whole limit is a no-op.

`renderDivisor` is the one worth thinking about. At `1` the game renders at your screen's full resolution, which is the sharpest it can be, but it costs about six times the fragments at 4K. The automatic choice keeps the render target near the size the game was drawn for and gets exactness from a whole-number upscale instead — the figure it rounds is the one the engine was already blitting by, which keeps the UI within a quarter of its shipped size at every resolution the game offers.

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

```
src/        the mod            -> build/ActualResolution.zip
test/       its tests          -> build/ActualResolution.Test.zip
minimal/    the mechanism in one file, not built and not equivalent -- read its header
lib/        shell helpers shared by the three scripts
```

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

**There is no per-frame work.** An earlier version compared `DisplayServer.WindowGetSize()` against the last value it acted on, every frame, which was simpler and always right but meant the mod was never idle. The window changes size from three places and only three — `Game._Ready`, `ApplySettings`, and the window manager — and Godot's own `size_changed` signal covers the third, including the fullscreen switch `_Ready` defers to the next frame.

## License

MIT. See [LICENSE](LICENSE), which ships inside the mod zip as well as sitting here.
