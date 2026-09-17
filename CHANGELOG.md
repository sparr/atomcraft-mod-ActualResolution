# Changelog

What changed in each release. The commits carry the reasoning.

## 0.1.2 - 2026-09-17

Fixed: the language screen's selection outline sat off the flag, by about 12 pixels at 1080p. The game measures that offset in its 1600x900 layout and adds it to a position in the frame, which are the same pixels only while the UI is unscaled, and this mod scales it. Reported upstream.

Built against Steam buildid 25333425.

## 0.1.1 - 2026-09-16

Fixed: the shadow and fog layer, which hides unexplored rock and the dark of space, was drawn off the terrain it belongs to. The game maps that layer with a frame size baked into its shader and never updates it.

Built against Steam buildid 25333425.

## 0.1.0 - 2026-09-15

First release. Draws the game at your window's resolution instead of a fixed 1600x900 frame, and restates the camera and UI constants that were measured against that frame.

Built against Steam buildid 25276035.
