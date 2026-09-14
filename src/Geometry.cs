using Godot;

namespace ActualResolution;

/// <summary>
/// The arithmetic this mod is built on, with nothing in it that needs a running game.
///
/// <para>The game draws every frame into a render target of a fixed 1600x900 and lets the
/// engine rescale that finished image to the window. At the shipped default window of
/// 1280x720 that rescale is 0.8, which drops one row in five; at 1920x1080 it is exactly 1.2,
/// which doubles one row in five. Nothing drawn inside the frame can compensate, because the
/// resample happens after everything in it has been drawn.</para>
///
/// <para>This mod sizes the render target to the window instead. That is the easy half. The
/// hard half is that <b>every constant the game was tuned with is a measurement of a 1600x900
/// frame</b>: the camera's zoom floor, the cell counts it clamps its own position with, the
/// parallax background's scale, the zoom ceiling, and the UI's whole layout. Left alone on a
/// larger frame they variously show the player past the edge of the world, shrink the view,
/// and strand the HUD in the top-left corner. Everything here is one of those constants,
/// expressed as a ratio to the frame it was measured against so it can be restated for the
/// frame actually in use.</para>
/// </summary>
public static class Geometry
{
    /// <summary>
    /// The render target the game is built around: <c>display/window/size/viewport_width</c>
    /// and <c>_height</c> in <c>project.binary</c>, with <c>stretch/mode = viewport</c>.
    /// Every constant the camera uses was chosen against this size, which is why the ones
    /// below are expressed as ratios to it rather than copied.
    /// </summary>
    public const float DesignWidth = 1600f;

    /// <inheritdoc cref="DesignWidth"/>
    public const float DesignHeight = 900f;

    /// <summary>World units per simulation cell: <c>Game.CELL_SIZE</c>, and FollowCam's own
    /// <c>WORLD_UNITS_PER_CELL</c>. A cell is <c>8 * zoom</c> render-target pixels.</summary>
    public const float CellWorldUnits = 8f;

    /// <summary>The zoom <c>FollowCam.IncreaseZoom</c> stops at, which at the design size is
    /// 12 pixels per cell.</summary>
    public const float GameMaxZoom = 1.5f;

    /// <summary>
    /// FollowCam's horizontal budget in cells, its literal 204.
    ///
    /// 1600/8 is 200 cells across the design viewport, so this is the visible width plus
    /// four cells of slack. <c>RecalculateMinZoom</c> divides it by the simulated width to
    /// find the zoom below which the camera would see past what the game renders.
    /// </summary>
    public const float CameraCellsWide = 204f;

    /// <summary>
    /// FollowCam's vertical budget in cells, its literal 104. Smaller than the 112.5 cells
    /// that fit, because the HUD covers the bottom of the screen
    /// (<c>CELL_HEIGHT_OF_HUD = 11</c>).
    /// </summary>
    public const float CameraCellsTall = 104f;

    /// <summary>FollowCam's <c>SAFEZONE_PADDING</c>: cells of the simulated area the camera
    /// may never reach the edge of.</summary>
    public const int SafezonePadding = 50;

    /// <summary>The background sprite's scale factors, FollowCam's 2.62 and 2.82. Divided by
    /// the minimum zoom so the parallax art still covers the screen at the widest view.</summary>
    public const float BackgroundScaleX = 2.62f;

    /// <inheritdoc cref="BackgroundScaleX"/>
    public const float BackgroundScaleY = 2.82f;

    // ------------------------------------------------- render target sizing

    /// <summary>
    /// How many window pixels one render-target pixel should become when nobody has said.
    ///
    /// <para>Rendering at the window's own resolution (a divisor of 1) is the sharpest
    /// option and gives the finest choice of zoom steps, but it also shrinks every piece of
    /// UI, which is laid out in render-target pixels: on a 4K screen the HUD would arrive at
    /// 42% of the size it has today. Dividing instead keeps the render target near the size
    /// the game was drawn for, and the whole-number upscale that follows is exact, so the
    /// pixels stay square either way.</para>
    ///
    /// <para>The figure rounded is the one the engine was already blitting by, which is
    /// <see cref="Ratio"/> of the window against the design size. So the choice is "the whole
    /// number nearest to what the game was doing anyway", and across every resolution in
    /// <c>Consts.SUPPORTED_RESOLUTIONS</c> that keeps the UI within a quarter of its shipped
    /// apparent size: 1 up to 1080p, 2 from 1440p. The one exception is the 800x1280 portrait
    /// entry, where the window is narrower than the design frame and no whole divisor can
    /// avoid a UI twice its shipped size.</para>
    /// </summary>
    public static int AutoDivisor(Vector2I window) =>
        Math.Max(1, (int)MathF.Round(Ratio(window)));

    /// <summary>The configured divisor, or <see cref="AutoDivisor"/> when it is zero.</summary>
    public static int DivisorFor(Vector2I window, int configured) =>
        configured > 0 ? configured : AutoDivisor(window);

    /// <summary>
    /// The render target for a window and a divisor.
    ///
    /// Integer division can leave a remainder on an odd window dimension, which is why the
    /// window is also put into <c>ContentScaleStretchEnum.Integer</c>: the engine then scales
    /// by a whole number and letterboxes the leftover row or column, rather than stretching
    /// by a fraction to cover it.
    /// </summary>
    public static Vector2I ContentSizeFor(Vector2I window, int divisor)
    {
        divisor = Math.Max(1, divisor);
        return new Vector2I(Math.Max(1, window.X / divisor), Math.Max(1, window.Y / divisor));
    }

    // ------------------------------------------------- camera bounds

    /// <summary>The viewport's width against the design width.</summary>
    public static float WidthRatio(Vector2 viewport) => viewport.X / DesignWidth;

    /// <summary>The viewport's height against the design height.</summary>
    public static float HeightRatio(Vector2 viewport) => viewport.Y / DesignHeight;

    /// <summary>
    /// The single figure the zoom ceiling scales by.
    ///
    /// The smaller of the two axis ratios, which is what the engine's own
    /// <c>stretch/aspect = keep</c> would have used to blit the design-sized frame into this
    /// window. Taking the minimum is what keeps an ultrawide window from magnifying the
    /// world past what a 16:9 one shows.
    /// </summary>
    public static float Ratio(Vector2 viewport) =>
        MathF.Min(WidthRatio(viewport), HeightRatio(viewport));

    /// <summary>
    /// <c>FollowCam.RecalculateMinZoom</c>'s answer, for a viewport that is not the design
    /// size.
    ///
    /// <para>Its two constants are cell counts measured against a 1600x900 viewport, so on a
    /// larger one they under-count the cells actually on screen and the camera is allowed to
    /// zoom out past the edge of the window of world the game renders. Scaling each by its
    /// own axis ratio restores the intent exactly: the minimum zoom comes out proportional
    /// to the viewport, so <b>the player sees the same number of cells at the widest view as
    /// they did before this mod</b>, whatever the resolution.</para>
    /// </summary>
    public static float MinZoom(Vector2 viewport, int simWidthCells, int simHeightCells,
                               bool wholePixels = false)
    {
        var horizontal = CameraCellsWide * WidthRatio(viewport) / (simWidthCells - SafezonePadding);
        var vertical = CameraCellsTall * HeightRatio(viewport) / (simHeightCells - SafezonePadding);
        return wholePixels
            ? WholePixels(MathF.Max(horizontal, vertical))
            : MathF.Max(horizontal, vertical);
    }

    /// <summary>
    /// The nearest zoom that puts a whole number of pixels on a simulation cell.
    ///
    /// <para>This is what makes the two limits worth landing on. The game comes to rest on
    /// them and nowhere else reliably — everywhere between is wherever the player let go of
    /// the key — so a whole number here is the difference between a view that is exact by
    /// default and one that is exact only by accident.</para>
    ///
    /// <para>Rounded to nearest rather than inward. Half a level is well under a percent of
    /// either limit, the floor keeps a fifty-cell margin from the edge of what the game
    /// renders, and the ceiling is a chosen limit rather than a correctness boundary, so
    /// neither direction can do harm; nearest is what keeps both closest to the view the
    /// shipped game gives.</para>
    /// </summary>
    public static float WholePixels(float zoom) =>
        MathF.Max(1f, MathF.Round(zoom * CellWorldUnits)) / CellWorldUnits;

    /// <summary>
    /// The zoom ceiling, scaled so the closest view keeps the apparent size it has in the
    /// shipped game.
    ///
    /// <para>The game stops at 1.5, which is 12 pixels per cell in a 1600x900 target; the
    /// engine then blew that up to the window, so a 1080p player is really looking at 14.4
    /// screen pixels per cell. Rendering at the window's resolution and stopping at 1.5
    /// anyway would quietly zoom every high-resolution player out. Multiplying the ceiling
    /// by <see cref="Ratio"/> hands back the magnification the blit used to supply.</para>
    /// </summary>
    public static float MaxZoom(Vector2 viewport, bool wholePixels = false)
    {
        var ceiling = GameMaxZoom * Ratio(viewport);
        return wholePixels ? WholePixels(ceiling) : ceiling;
    }

    /// <summary>
    /// What <c>FollowCam.IncreaseZoom</c>'s own clamp has to be shifted by to land on
    /// <see cref="MaxZoom"/> instead of on the game's 1.5.
    ///
    /// Biasing the easing target down by this before the original runs and back up after
    /// leaves the game's zoom speed, its arithmetic, and its clamp in charge, so a retune of
    /// any of them is inherited rather than overwritten. The harness raises the same ceiling
    /// the same way, and two biases compose because they are addition.
    /// </summary>
    public static float MaxZoomBias(Vector2 viewport, bool wholePixels = false) =>
        MaxZoom(viewport, wholePixels) - GameMaxZoom;
}
