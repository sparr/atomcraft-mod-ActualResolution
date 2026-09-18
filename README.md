# Actual Resolution

An Atomcraft mod that draws the game at your window's resolution instead of drawing a fixed 1600x900 frame and letting the engine rescale it.

Requires [GodotMonoModLoader](https://github.com/sacroimper/GodotMonoModLoader). Drop `ActualResolution.zip` in your `Mods/` folder.

What changed in each release is in [CHANGELOG.md](CHANGELOG.md).

## What it changes

| | Shipped game | With this mod |
| --- | --- | --- |
| Render target | always 1600x900, rescaled to the window | the window's size (or a whole fraction of it), blitted by a whole number |
| UI | 1600x900 layout, rescaled with the frame | same layout, scaled by the same factor the blit used to apply |
| Shadow and fog | mapped with a 1600x900 baked into its shader | mapped with the frame the game is actually drawing |
| Language screen | the selection outline drifts off the flag once the UI is scaled | the outline stays on the flag |
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
./run-tests.sh --no-build              # test what is installed; how a release is verified
./play.sh                              # a real game with only this mod loaded
./play.sh --verify                     # boot it headless and confirm it loaded
```

To exercise the interesting case, run the display tests at a resolution the shipped game cannot hit exactly:

```sh
XVFB_GEOMETRY=1920x1080x24 ./run-tests.sh --headful
```

**`XVFB_GEOMETRY` alone does not change the render target.** It sizes the virtual screen; the game's window comes from `DisplayResolution` in the test prefix's own `DeviceSettings.json`, which defaults to 1280x720 and stays there however big the screen is. To reach a divisor above 1, raise that too — it lives at `$TEST_ROOT/prefix/pfx/drive_c/users/steamuser/AppData/Roaming/Godot/app_userdata/Atomcraft/DeviceSettings.json` — and then run at a matching screen size:

```sh
XVFB_GEOMETRY=2560x1440x24 ./run-tests.sh --headful
```

With `DisplayResolution` at 2560x1440 that gives a 2560x1420 window and a 1280x710 render target at 2x, which is the case where the target is neither the window nor the design frame: the one that catches a fix assuming those are the same thing.

Tests run inside the real game through the [Atomcraft TestHarness](https://github.com/sparr/atomcraft-mod-TestHarness), against a patched *copy* of the install in a throwaway prefix, so your saves and your real game are never touched. `harness.conf` names the pinned harness release; there are no path defaults, because a path guessed from a sibling directory goes stale silently the first time that directory is renamed. Everything stays under a test root private to this project.

**Build releases with `--release`.** Debug is the default because that is what you want while working, but a Debug assembly carries `DebuggableAttribute` with `DisableOptimizations`, which turns the JIT off for it entirely.

## Layout

| | |
| --- | --- |
| [`src/`](src/) | the mod &rarr; `build/ActualResolution.zip` |
| [`test/`](test/) | its tests &rarr; `build/ActualResolution.Test.zip` |
| [`conformance/`](conformance/) | a property of the game, naming no mod &rarr; `build/ActualResolutionConformance.zip` |
| [`minimal/`](minimal/) | the mechanism in one file, not built and not equivalent — read its header |
| [`lib/`](lib/) | shell helpers shared by the three scripts |

The tests are a peer mod, not a module of this one: the loader treats a missing dependency as an error, so a test module shipped inside this zip would show a red entry in the loader report for every player who had not also installed `TestHarness`.

### The retirement suite

[`test/RetirementTests.cs`](test/RetirementTests.cs) asserts the **game** is still what this mod works around, rather than that the mod is correct. A failure there is good news: it means a game update made this unnecessary, and the test's doc comment says what to delete. Kept out of the default run, because mixing the two questions makes a red suite unreadable:

```sh
./run-tests.sh --retirement
```

`TheGameStillRendersIntoAFixedFrame` reads the project settings rather than the live viewport, because this mod resizes the render target at runtime and never touches the setting — asking the viewport would just report the mod back to itself. `TheGameStillNeverSetsTheShadowShadersFrameSize` writes a sentinel into the shader uniform the game leaves alone and checks that several drawn frames later it is still there, which reports on the running game rather than on what the decompiled source said the day it was written.

`TheGameStillOffsetsTheFlagOutlineInFrameSpace` reads the original IL of the state machine this mod transpiles, because the claim is about the game's own code and the mod has a patch inside that very method. Harmony leaves a patched method's body in metadata, so that reading answers for the game rather than for the mod.

### The conformance suite

[`conformance/`](conformance/) is a peer mod that **names no mod** and depends only on the harness, so it can be installed alongside this mod, alongside a rival, or alongside none. It asks two things.

`TheUiIsOneFixedSizeControlWithTopLeftAnchors` is a property this mod *depends on* rather than fixes: if the game ever anchored its UI to the viewport, the compensating scale here would be applied to a layout that had already adapted, so a failure is bad news.

`TheShadowLayerIsMappedWithTheRealFrameSize` is a property any mod that resizes the render target has to maintain, because the game does not maintain it. It measures the live viewport rather than anything the mod reports, which makes it an independent check rather than the mod reporting itself back. It came from the [Zoooom](../Zoooom/) project, which depends on the property and does not affect it; it lives here too because a suite that never runs beside the fix tests nothing about the fix.

## Tests

Seventeen run headless, so a game update that moves the camera's constants fails the ordinary suite. Five need a display. Three more are the retirement suite, which the default run leaves out.

- `TheFrameReachesTheWindowUnresampled` measures the blit rather than looking at it, because a viewport readback samples the render target *before* the engine scales it to the window: a frame about to be resampled reads back perfect. Nothing inside the game can photograph this defect, which is why it went unnoticed.
- `TheUiIsScaledTheWayTheEngineUsedTo` and `TheGameFillsTheFrameItIsGiven` cover the half of the job that is not the world. The second also writes the frame out as an artifact, because "the UI is laid out sensibly" is not a thing a test can assert and is a thing somebody should look at after a game update.
- `TheWidestViewShowsTheSameWorldAtEveryResolution` is the invariant that makes resizing the frame safe at all.
- `TheFrameFollowsAWindowResize` resizes the window directly, which is the point: every other route into the mod goes through a method it patches, so any of them would pass with the per-frame window check deleted. This is the only route that does not, and it is the test that found the mod was not watching the window at all up to 0.1.2.
- `TheOutlineSitsOnAFlag` imposes a UI scale of its own rather than using the run's. The harness runs in a window the size of the design frame, where the mod scales the UI by exactly 1 and the defect it covers cannot appear, so without that the test would pass with the correction deleted.
- `TheShadowLayerIsMappedWithTheRealFrameSize`, in the conformance suite, is the one defect here that *is* visible to the naked eye and invisible to everything else: the game never tells the shadow shader how big the frame is, and the layer that hides unexplored terrain lands off the terrain with nothing in the log.

## Compatibility

Nothing here touches the simulation, so this mod cannot change what the world does or desync a multiplayer session.

**The TestHarness's own `OverlayTests.RaisingTheLimitRaisesTheGamesOwnClamp` fails while this mod is installed.** It asserts that an unpatched camera stops zooming in at exactly 1.5, and `preserveMaxZoom` moves that limit on purpose. `./run-tests.sh` runs only this mod's own tests, so it does not show up there; it appears under `--all`, where `--atomtest-exclude=RaisingTheLimit` or `preserveMaxZoom: false` gets it green.

Built against Steam buildid **25333425** (`Atomcraft.dll` md5 `24bae9b4d904deb16b573d3279a5d3e1`), and the test suite is run against a patched copy of that same build.

## What it patches

| Target | Why |
| --- | --- |
| `Game._Ready` (postfix) | size the frame at startup |
| `Game._Process` (postfix) | notice a window resized by the window manager, which the game is never told about. One `WindowGetSize` and a compare; it calls nothing else unless the window actually moved |
| `SaveData_Device.ApplySettings` (postfix) | the game's own "the player changed a setting" event |
| `Gameplay.ResizeDisplayTextures` (postfix) | where the game rebinds the shadow material, so a frame size set on it cannot be left behind |
| `FollowCam.RecalculateMinZoom` (postfix) | recompute the widest view, and the background's scale, for a frame that is not 1600x900 |
| `FollowCam.CameraCellWidth` / `CameraCellHeight` (getters) | the cell counts the camera clamps its own position with, likewise |
| `FollowCam.IncreaseZoom` (prefix and postfix) | move the zoom-in limit by biasing the target around the game's own clamp, rather than reimplementing it |
| `FlagGrid.SetHighlightOnCurrentLocale` (transpiler) | the language screen measures its selection outline's offset in the 1600x900 layout and adds it to a position in the frame, which are the same pixels only while the UI is not scaled |

Nothing is replaced. Every patch but one is a prefix or postfix around the game's own arithmetic, so a retune of the zoom speed or the camera constants is inherited rather than overwritten. The exception is one instruction inserted into `FlagGrid`, between the game building that offset and the game adding it; see [`src/FlagOutline.cs`](src/FlagOutline.cs) for why there is no boundary to postfix there.

## License

MIT. See [LICENSE](LICENSE)
