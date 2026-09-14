using Atomcraft;
using Godot;

namespace ActualResolution;

/// <summary>
/// Puts the UI back where the engine's blit used to put it.
///
/// <para><b>The game's UI is not laid out against the viewport.</b> Under
/// <c>Game.CanvasLayer</c> there is exactly one Control, <c>UI</c>, and it is a fixed
/// 1600x900 box with top-left anchors; everything in it is positioned absolutely inside that
/// box, down to <c>UpperRightHUDElements</c> sitting at x=1600 because that is where the
/// right edge is. The engine's rescale of the finished frame is what used to stretch all of
/// that to the window.</para>
///
/// <para>Take the rescale away, as <see cref="RenderTarget"/> does, and the UI stays at its
/// drawn size in the top-left corner of a larger frame, with the HUD ending three hundred
/// pixels short of the screen. So the scale the blit was applying is applied here instead, to
/// that one Control: the same factor, the same aspect handling, the same result on screen.
/// The world is untouched by it, because the terrain, foreground, shadowmap and heatwave
/// sprites are siblings of the canvas layer rather than children of it.</para>
///
/// <para><b>What this does not fix.</b> Scaling a 1600x900 layout by 1.2 is the very
/// resampling this mod removes from the world, now confined to the UI. The UI comes out
/// exactly as it does in the shipped game, no better; it is the world that gains. Making the
/// UI itself land on whole pixels means laying it out against the frame rather than scaling
/// it, which is a different job. <see cref="Settings.UiScale"/> is the way to ask for
/// something other than the shipped behaviour in the meantime: 1.0 draws the UI at the
/// frame's own resolution, which is perfectly sharp and smaller than the game intends.</para>
/// </summary>
public static class UiLayout
{
    /// <summary>The scale currently applied, or 0 before anything has been.</summary>
    public static float Scale { get; private set; }

    /// <summary>
    /// The scale the UI should be drawn at: the one the engine's blit was applying.
    ///
    /// <c>display/window/stretch/aspect</c> is unset in <c>project.binary</c>, so the engine
    /// default of <c>keep</c> was in force and the blit was uniform on both axes, at the
    /// smaller of the two ratios, with the remainder as a letterbox. Matching that means a
    /// single factor rather than one per axis, which is also the only way a circle in the UI
    /// stays a circle.
    /// </summary>
    public static float WantedScale(Vector2 viewport) =>
        Settings.UiScale > 0f ? Settings.UiScale : Geometry.Ratio(viewport);

    /// <summary>
    /// Applies the scale and centres the result, if either has changed. Cheap enough to call
    /// every frame, which is what makes it robust to the UI being rebuilt or the window
    /// changing size without this mod hearing about it.
    /// </summary>
    public static void Sync()
    {
        var ui = Game.UI;
        if (ui == null)
            return;
        if (RenderTarget.ViewportSize is not { } viewport)
            return;

        var scale = WantedScale(viewport);
        if (!float.IsFinite(scale) || scale <= 0f)
            return;

        // Centred, as the letterbox was. On a window with the design aspect this is (0, 0)
        // and the UI fills the frame; on any other it is the margin the black bars used to
        // occupy, which now shows world instead.
        var covered = new Vector2(Geometry.DesignWidth, Geometry.DesignHeight) * scale;
        var position = ((viewport - covered) * 0.5f).Round();

        if (Mathf.IsEqualApprox(ui.Scale.X, scale) && Mathf.IsEqualApprox(ui.Scale.Y, scale)
            && ui.Position.IsEqualApprox(position))
            return;

        ui.Scale = new Vector2(scale, scale);
        ui.Position = position;
        Scale = scale;
        Log.Info($"UI scaled {scale:0.####} at ({position.X}, {position.Y})");
    }

    /// <summary>Puts the UI back at its drawn size and origin, for the mod being switched off.</summary>
    public static void Restore()
    {
        var ui = Game.UI;
        if (ui == null)
            return;
        ui.Scale = Vector2.One;
        ui.Position = Vector2.Zero;
        Scale = 0f;
    }
}
