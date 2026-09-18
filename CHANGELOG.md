# Changelog

What changed in each release. The commits carry the reasoning.

## 0.2.0 - 2026-09-17

Fixed: a window resized by anything other than the game itself was not noticed, so the frame kept its old size and the engine resampled it until the player next opened the settings page. Dragging a window edge, a tiling window manager, and a monitor change were all affected. The resize was watched through a signal that belongs to the viewport rather than to the window, and this mod pins the viewport, so the signal could not fire.

Added: `exactFullscreen`, off by default. Fullscreen renders at the screen's exact resolution rather than two pixels short of it behind a 1px border. The border is Godot's, not the game's, and it is what lets other windows draw over the game, which is why this is a setting and why it is off. Already fixed in Godot 4.5; the game ships on 4.4.

Built against Steam buildid 25333425.

## 0.1.2 - 2026-09-17

Fixed: the language screen's selection outline sat off the flag, by about 12 pixels at 1080p. The game measures that offset in its 1600x900 layout and adds it to a position in the frame, which are the same pixels only while the UI is unscaled, and this mod scales it. Reported upstream.

Built against Steam buildid 25333425.

## 0.1.1 - 2026-09-16

Fixed: the shadow and fog layer, which hides unexplored rock and the dark of space, was drawn off the terrain it belongs to. The game maps that layer with a frame size baked into its shader and never updates it.

Built against Steam buildid 25333425.

## 0.1.0 - 2026-09-15

First release. Draws the game at your window's resolution instead of a fixed 1600x900 frame, and restates the camera and UI constants that were measured against that frame.

Built against Steam buildid 25276035.
