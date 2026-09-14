using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

namespace ActualResolution.Test;

/// <summary>
/// What this mod claims, in the parts that are arithmetic.
///
/// <para>None of these need a screen or even a world, so they run in the ordinary headless
/// suite and catch a regression the moment it lands. What they cannot do is prove the frame
/// the player actually sees; that is <see cref="ScreenTests"/>.</para>
/// </summary>
public static class GeometryTests
{
    /// <summary>Viewports the mod has to behave the same way on. All 16:9, because the
    /// invariants about the visible world are about a shape, not a size.</summary>
    private static readonly Vector2[] Widescreen =
    {
        new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
    };

    /// <summary>The shipped default simulated area, 5x3 chunks of 64.</summary>
    private const int SimWidth = 5 * 64;
    private const int SimHeight = 3 * 64;

    /// <summary>
    /// The camera's widest view still shows the same world it always did.
    ///
    /// <para>This is the invariant that makes resizing the render target safe. FollowCam's
    /// zoom floor is derived from two cell counts measured against a 1600x900 frame; scaled
    /// by the viewport's own axis ratios, the floor comes out proportional to the viewport,
    /// and the number of cells on screen at that floor does not move. Get it wrong in either
    /// direction and the player either loses world they used to see or is shown past the edge
    /// of what the game renders.</para>
    /// </summary>
    [GameTest]
    public static void TheWidestViewShowsTheSameWorldAtEveryResolution()
    {
        var reference = VisibleCells(new Vector2(Geometry.DesignWidth, Geometry.DesignHeight));

        foreach (var viewport in Widescreen)
        {
            var cells = VisibleCells(viewport);
            if (MathF.Abs(cells.X - reference.X) > 0.01f || MathF.Abs(cells.Y - reference.Y) > 0.01f)
                throw new AssertionException(
                    $"at {viewport.X}x{viewport.Y} the widest view shows {cells.X}x{cells.Y} " +
                    $"cells, but at the design size it shows {reference.X}x{reference.Y}");
        }
    }

    /// <summary>
    /// The zoom ceiling is scaled the same way, so the closest view is as close as it was
    /// before the mod: the magnification the engine's blit used to supply is handed back
    /// exactly.
    /// </summary>
    [GameTest]
    public static void TheClosestViewKeepsItsApparentSize()
    {
        foreach (var viewport in Widescreen)
        {
            // What a player saw before this mod: the game's own ceiling, blown up to the
            // window by the engine. The window and the viewport are the same size here
            // because the divisor for a 16:9 window at these sizes is 1.
            var before = Geometry.CellWorldUnits * Geometry.GameMaxZoom * Geometry.Ratio(viewport);
            var after = Geometry.CellWorldUnits * Geometry.MaxZoom(viewport, wholePixels: false);
            if (MathF.Abs(before - after) > 1e-3f)
                throw new AssertionException(
                    $"at {viewport.X}x{viewport.Y} the closest view is {after} screen pixels " +
                    $"per cell, where the shipped game gives {before}");
        }
    }

    /// <summary>
    /// With whole-pixel limits asked for, both of the zooms the game comes to rest on put a
    /// whole number of pixels on a cell — and neither has moved far enough to matter.
    ///
    /// <para>Those two are the ones worth rounding because they are the only ones the game
    /// reliably returns to: the widest view is where a session starts and where zooming out
    /// stops, the closest is where zooming in stops, and everywhere between is wherever the
    /// player let go of the key. Half a pixel per cell is the most either can move, which is
    /// under a percent of the framing at any zoom the game offers.</para>
    /// </summary>
    [GameTest]
    public static void BothZoomLimitsLandOnWholePixels()
    {
        foreach (var viewport in Widescreen)
        {
            var floor = Geometry.MinZoom(viewport, SimWidth, SimHeight, wholePixels: true);
            var ceiling = Geometry.MaxZoom(viewport, wholePixels: true);

            foreach (var (name, zoom, unrounded) in new[]
                     {
                         ("widest", floor, Geometry.MinZoom(viewport, SimWidth, SimHeight)),
                         ("closest", ceiling, Geometry.MaxZoom(viewport)),
                     })
            {
                var cell = Geometry.CellWorldUnits * zoom;
                if (MathF.Abs(cell - MathF.Round(cell)) > 1e-4f)
                    throw new AssertionException(
                        $"at {viewport.X}x{viewport.Y} the {name} view is {cell} pixels per " +
                        "cell, which is not a whole number");

                var moved = MathF.Abs(zoom - unrounded) * Geometry.CellWorldUnits;
                if (moved > 0.5f + 1e-4f)
                    throw new AssertionException(
                        $"at {viewport.X}x{viewport.Y} rounding the {name} view moved it " +
                        $"{moved} pixels per cell; half a pixel is the most it can ever need");

                if (cell < 1f)
                    throw new AssertionException(
                        $"at {viewport.X}x{viewport.Y} the {name} view rounded to less than " +
                        "one pixel per cell");
            }
        }
    }

    /// <summary>
    /// The automatic divisor keeps the UI within a quarter of the size it has in the shipped
    /// game, at every resolution the game itself offers.
    ///
    /// <para>UI is laid out in render-target pixels, so its apparent size is the divisor,
    /// against the factor the engine used to blit the design-sized frame by. The portrait
    /// entry in the list is excluded and named: its window is narrower than the design frame,
    /// so the old factor is below a half and no whole divisor can come near it.</para>
    /// </summary>
    [GameTest]
    public static void TheAutoDivisorKeepsTheUiNearItsShippedSize()
    {
        foreach (var resolution in Consts.SUPPORTED_RESOLUTIONS)
        {
            if (resolution.X < resolution.Y)
                continue;                       // portrait; see the summary

            var before = Geometry.Ratio(resolution);
            var after = Geometry.AutoDivisor(resolution);
            var ratio = after / before;
            if (ratio is < 0.8f or > 1.26f)
                throw new AssertionException(
                    $"at {resolution.X}x{resolution.Y} the divisor is {after} against an " +
                    $"engine blit of {before}, so the UI would arrive {ratio}x its shipped " +
                    "size; the automatic choice is meant to stay within a quarter");
        }
    }

    /// <summary>
    /// Every window the game offers divides into a render target that scales back up by a
    /// whole number. The remainder of an odd division is the reason the window is also put
    /// into integer stretch, and this bounds it: at most one row or column short of the
    /// window, never more.
    /// </summary>
    [GameTest]
    public static void TheRenderTargetScalesBackUpToCoverTheWindow()
    {
        foreach (var resolution in Consts.SUPPORTED_RESOLUTIONS)
        {
            var divisor = Geometry.AutoDivisor(resolution);
            var content = Geometry.ContentSizeFor(resolution, divisor);
            var covered = content * divisor;
            var lost = resolution - covered;
            if (lost.X < 0 || lost.Y < 0 || lost.X >= divisor || lost.Y >= divisor)
                throw new AssertionException(
                    $"{resolution.X}x{resolution.Y} at {divisor}x renders {content.X}x{content.Y}, " +
                    $"covering {covered.X}x{covered.Y}: {lost.X}x{lost.Y} of the window is " +
                    "unaccounted for, which is more than integer stretch would letterbox");
        }
    }

    /// <summary>
    /// The private camera state this mod reaches for is still there.
    ///
    /// <para>A game update that renames it leaves the mod drawing a larger frame while the
    /// camera still clamps itself as though the frame were 1600x900, which shows the player
    /// past the edge of what the game renders. That is a worse failure than a red test,
    /// because it looks like a game bug. This fails in the ordinary headless suite the day it
    /// happens.</para>
    /// </summary>
    [GameTest]
    public static void TheCameraStateThisModDependsOnStillExists()
    {
        if (!CameraFieldsAreComplete())
            throw new AssertionException(
                "FollowCam no longer exposes the private state this mod patches, so the " +
                "camera's limits cannot be restated for a resized render target. The mod " +
                "needs updating for this version of the game.");
    }

    /// <summary>
    /// Asked of the mod rather than reimplemented here, so this test tracks whatever the mod
    /// actually depends on rather than a copy of the list that can go stale.
    /// </summary>
    private static bool CameraFieldsAreComplete() =>
        typeof(ActualResolutionApi).Assembly
            .GetType("ActualResolution.CameraFields")?
            .GetProperty("Complete", System.Reflection.BindingFlags.NonPublic
                                     | System.Reflection.BindingFlags.Static)?
            .GetValue(null) as bool? ?? false;

    /// <summary>
    /// The state registration puts the mod back on, not just its settings.
    ///
    /// <para>The registered reset runs between tests, and the state a test is most likely to
    /// leave behind is the state it did not change on purpose: a latched fault, or the off
    /// switch flipped by a test that threw before its <c>finally</c>. Neither is reachable
    /// from <c>Settings.Reset</c> — <c>Enabled</c> is the conjunction of a setting and a
    /// field, so restoring the setting leaves a false field false — and a mod left switched
    /// off does not fail the tests after it. It passes the ones that assert the game behaves
    /// as it does unmodded, which is worse.</para>
    ///
    /// <para>The first assertion is a premise check: if <c>Settings.Reset</c> ever does
    /// restore the mod on its own, this test is measuring nothing and says so rather than
    /// passing quietly.</para>
    /// </summary>
    [GameTest]
    public static void ResettingTheStateRestoresTheModAndNotOnlyItsSettings()
    {
        ActualResolutionApi.Enabled = false;

        Settings.Reset();
        if (ActualResolutionApi.Enabled)
            throw new AssertionException(
                "Settings.Reset restored the mod by itself, so this test no longer covers the " +
                "gap it was written for. Check what ResetState is still needed for.");

        ActualResolutionApi.ResetState();
        if (!ActualResolutionApi.Enabled)
            throw new AssertionException(
                "ResetState left the mod switched off. Every test after this one would run " +
                "against an unmodded game, and the ones asserting unmodded behaviour would pass.");
        if (ActualResolutionApi.Faulted)
            throw new AssertionException("ResetState left a fault latched");
    }

    /// <summary>How many cells fit on screen at the widest view a viewport allows.</summary>
    private static Vector2 VisibleCells(Vector2 viewport)
    {
        var zoom = Geometry.MinZoom(viewport, SimWidth, SimHeight);
        return viewport / (Geometry.CellWorldUnits * zoom);
    }
}
