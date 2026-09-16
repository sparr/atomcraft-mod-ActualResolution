// The whole of ActualResolution, in one file and one idea.
//
// NOT part of the build: it lives outside src/ so the real project's **/*.cs glob does not
// sweep it in. Two [HarmonyPatch] classes both resizing the render target would fight over it.
// It is here to show how little the mod's *mechanism* is once the camera constants, the UI
// rescale, the settings, the degradation paths, and the commentary come off, and to be pasted
// into a fresh project by anyone who wants to see the idea working.
//
// What it is: project.binary sets display/window/stretch/mode = viewport over a viewport of
// 1600x900, so the game draws every frame into a render target of exactly that size whatever
// the window is, and the engine rescales the finished image at the end. At the shipped default
// window of 1280x720 that rescale is 0.8 and drops one row in five; at 1920x1080 it is 1.2 and
// doubles one row in five. Nothing drawn inside the frame can compensate, because the resample
// happens after everything in it has been drawn. Setting ContentScaleSize to the window's own
// size makes the final blit 1:1.
//
// READ THIS BEFORE SHIPPING IT. Unlike the other minimal/ files in this family, this one is
// NOT equivalent to the mod. Every constant the game was tuned with is a measurement of a
// 1600x900 frame, and on a larger frame, left alone, they variously:
//
//   - show the player past the edge of what the world renders (FollowCam.RecalculateMinZoom,
//     and the cell counts the camera clamps its own position with);
//   - stop the zoom further out than the shipped game did, because the engine's blit used to
//     supply the last 1.2x of magnification for free (FollowCam's flat 1.5 ceiling);
//   - strand the HUD in the top-left corner, because Game.UI is a fixed 1600x900 Control with
//     every anchor at 0 and only the blit was ever making it reach the edges;
//   - draw the shadow and fog layer off the terrain it belongs to, because Shadowmap.tres maps
//     its texture with SCREEN_UV * viewport_size and the game never assigns that uniform: it
//     carries the 1600x900 saved in the material, which was right only while the frame was.
//
// The real mod exists to restate all of that for the frame actually in use. This file gives a
// sharp world, a broken HUD, and a misplaced shadow layer. It is a demonstration of the
// mechanism, not a smaller mod.
//
// If you do want to ship something from here, ship the real one:
//
//   { "id": "ActualResolution", "name": "Actual Resolution", "version": "0.1.0", "author": "",
//     "modules": [ { "moduleId": "ActualResolution/Main", "dll": "ActualResolution.dll",
//                    "initClass": "ActualResolution.ModEntry" } ] }

using System.Reflection;
using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

[HarmonyPatch]
public static class ActualResolutionMinimal
{
    /// <summary>Called by the mod loader, a frame before Game._Ready.</summary>
    public static void Initialize() =>
        new Harmony("ActualResolution").PatchAll(Assembly.GetExecutingAssembly());

    /// <summary>The window size the render target was last computed from.</summary>
    private static Vector2I _applied;

    /// <summary>
    /// Size the render target to the window once the game is up, and again whenever the
    /// player changes a display setting.
    ///
    /// The real mod also subscribes to the root window's size_changed signal, which is what
    /// catches a resize from the window manager and the fullscreen switch _Ready defers.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), "_Ready")]
    public static void AfterReady() => Sync();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveData_Device), nameof(SaveData_Device.ApplySettings))]
    public static void AfterApplySettings() => Sync();

    private static void Sync()
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root })
            return;

        var window = DisplayServer.WindowGetSize();
        if (window == _applied || window.X <= 0 || window.Y <= 0)
            return;
        _applied = window;

        // Integer stretch as well as the size, so the engine cannot pick a fraction of its own
        // if the division ever leaves a remainder.
        root.ContentScaleSize = window;
        root.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;

        GD.Print($"[ActualResolution] render target now {window.X}x{window.Y}");
    }
}
