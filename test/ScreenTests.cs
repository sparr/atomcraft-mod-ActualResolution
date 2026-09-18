using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

// The game has a Session type of its own; the harness's is the one meant here.
using Session = Atomcraft.TestHarness.Session;

namespace ActualResolution.Test;

/// <summary>
/// What the arithmetic cannot show: that the running game draws a frame the size of its
/// window, that the engine hands it to the screen untouched, and that the UI still fills it.
///
/// <para>Every test here needs a display and abstains without one, so the ordinary headless
/// suite stays green and <c>./run-tests.sh --headful</c> is where they count.</para>
///
/// <para>One thing none of them can do: look at the window. <c>GetViewport().GetTexture()</c>
/// samples the render target, <i>before</i> the engine blits it, so a frame that is about to
/// be resampled reads back perfect. That half of the claim is made by
/// <see cref="TheFrameReachesTheWindowUnresampled"/>, which measures the blit rather than
/// looking at it.</para>
/// </summary>
public static class ScreenTests
{
    /// <summary>
    /// The half of the problem that lives outside the frame: the engine's final blit is by a
    /// whole number, on both axes, so nothing in the finished image is resampled on the way
    /// to the window.
    ///
    /// <para>This is the defect the mod was written for. Shipped, the game draws into a fixed
    /// 1600x900 target whatever the window is: 0.8 at the default 1280x720, which drops one
    /// row in five, and 1.2 at 1920x1080, which doubles one row in five.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheFrameReachesTheWindowUnresampled()
    {
        yield return Session.Enter("flat");
        yield return Wait.Frames(2);

        var window = (Vector2)DisplayServer.WindowGetSize();
        var viewport = ActualResolutionApi.ViewportSize;
        if (viewport.X <= 0f || viewport.Y <= 0f)
            throw new AssertionException("the game reports no viewport");

        var scale = new Vector2(window.X / viewport.X, window.Y / viewport.Y);
        if (!Mathf.IsEqualApprox(scale.X, scale.Y)
            || scale.X < 1f
            || !Mathf.IsEqualApprox(scale.X, Mathf.Round(scale.X)))
            throw new AssertionException(
                $"the {window.X}x{window.Y} window shows a {viewport.X}x{viewport.Y} frame, " +
                $"a scale of {scale.X}x{scale.Y}. A fraction here doubles or drops whole rows " +
                "and columns of the finished image.");

        if (scale.X != ActualResolutionApi.Divisor)
            throw new AssertionException(
                $"the engine is scaling by {scale.X} but the mod sized the render target for " +
                $"{ActualResolutionApi.Divisor}; the two have to agree or the camera's own " +
                "arithmetic is against the wrong frame");
    }

    /// <summary>
    /// The UI is scaled by exactly what the engine's blit used to scale it by, so it reaches
    /// the edges of the frame it used to reach and is the size it always was.
    ///
    /// <para>The game lays its UI out in a fixed 1600x900 Control with top-left anchors, and
    /// relied on the frame being rescaled to the window to make that fill the screen. Sizing
    /// the render target to the window takes that rescale away, which would leave the HUD
    /// stopping short of the bottom-right of a larger screen.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheUiIsScaledTheWayTheEngineUsedTo()
    {
        yield return Session.Enter("flat");
        yield return Wait.Frames(3);

        var ui = Game.UI ?? throw new AssertionException("the game has no UI node");
        var viewport = ActualResolutionApi.ViewportSize;
        var wanted = Geometry.Ratio(viewport);

        if (!Mathf.IsEqualApprox(ui.Scale.X, ui.Scale.Y))
            throw new AssertionException(
                $"the UI is scaled {ui.Scale.X} across and {ui.Scale.Y} down. The blit this " +
                "replaces was uniform (stretch/aspect defaults to keep), and a UI stretched " +
                "unevenly is a UI whose circles are ellipses.");

        if (!Mathf.IsEqualApprox(ui.Scale.X, wanted))
            throw new AssertionException(
                $"the UI is scaled {ui.Scale.X} in a {viewport.X}x{viewport.Y} frame, where the " +
                $"engine's own blit would have scaled it {wanted}");

        // The scaled layout has to reach both edges of whichever axis constrains it, and be
        // centred on the other: that is what the letterbox did.
        var covered = new Vector2(Geometry.DesignWidth, Geometry.DesignHeight) * ui.Scale;
        var margin = viewport - covered - ui.Position * 2f;
        if (MathF.Abs(margin.X) > 1.5f || MathF.Abs(margin.Y) > 1.5f)
            throw new AssertionException(
                $"the UI covers {covered.X}x{covered.Y} at {ui.Position} in a " +
                $"{viewport.X}x{viewport.Y} frame, which is not centred");

        if (MathF.Min(viewport.X - covered.X, viewport.Y - covered.Y) > 1.5f)
            throw new AssertionException(
                $"the UI covers only {covered.X}x{covered.Y} of a {viewport.X}x{viewport.Y} " +
                "frame, so it reaches neither pair of edges and the HUD stops short of the screen");
    }

    /// <summary>
    /// The game still fills the frame it is given, now that the frame is not the 1600x900 one
    /// every piece of its UI was laid out against.
    ///
    /// <para>This is the risk that comes with resizing the render target rather than with
    /// quantizing the zoom: a Control anchored to a fixed size, or an art asset sized to the
    /// design viewport, would leave an unpainted margin down one edge of a larger frame.
    /// Sampling the border is a coarse check and it is the one that matters, because the
    /// failure it looks for is coarse.</para>
    ///
    /// <para>The frame is also written out as an artifact, because "the UI is laid out
    /// sensibly" is not a thing a test can assert and is a thing somebody should look at after
    /// a game update.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheGameFillsTheFrameItIsGiven()
    {
        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);
        yield return Wait.Frames(3);

        var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
        if (image == null || image.GetWidth() == 0)
            Harness.Inapplicable("the viewport cannot be read back on this renderer");

        var width = image!.GetWidth();
        var height = image.GetHeight();
        Artifacts.WriteBytes($"frame-{width}x{height}.png", image.SavePngToBuffer());

        // Four inset scanlines rather than the outermost row: the outermost is where a
        // legitimate one-pixel border or vignette would live, and this is looking for a
        // margin, not for a line.
        var unpainted = new List<string>();
        Check(2, "left");
        Check(width - 3, "right");
        CheckRow(2, "top");
        CheckRow(height - 3, "bottom");

        if (unpainted.Count > 0)
            throw new AssertionException(
                $"the {width}x{height} frame has nothing drawn along its " +
                $"{string.Join(", ", unpainted)} edge, so something in the game is still sized " +
                "for a 1600x900 one. The frame is in this test's artifacts.");

        void Check(int x, string edge)
        {
            for (var y = 0; y < height; y++)
                if (image.GetPixel(x, y).A > 0.01f)
                    return;
            unpainted.Add(edge);
        }

        void CheckRow(int y, string edge)
        {
            for (var x = 0; x < width; x++)
                if (image.GetPixel(x, y).A > 0.01f)
                    return;
            unpainted.Add(edge);
        }
    }

    /// <summary>
    /// Turning the mod off puts the game back as it shipped: the render target returns to the
    /// fixed 1600x900 the engine had, and the UI to its drawn size.
    ///
    /// <para>Worth its own test because the switch is what a player uses to compare. A switch
    /// that only half worked would leave the camera's bounds computed for a frame that is no
    /// longer that size.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TurningItOffPutsTheGameBack()
    {
        yield return Session.Enter("flat");
        yield return Wait.Frames(2);

        var modded = ActualResolutionApi.ViewportSize;

        ActualResolutionApi.Enabled = false;
        try
        {
            yield return Wait.Frames(3);

            var vanilla = ActualResolutionApi.ViewportSize;
            if (!Mathf.IsEqualApprox(vanilla.X, Geometry.DesignWidth)
                || !Mathf.IsEqualApprox(vanilla.Y, Geometry.DesignHeight))
                throw new AssertionException(
                    $"with the mod off the game should draw into its own {Geometry.DesignWidth}" +
                    $"x{Geometry.DesignHeight} frame, but the viewport is {vanilla.X}x{vanilla.Y}");

            var ui = Game.UI ?? throw new AssertionException("the game has no UI node");
            if (!Mathf.IsEqualApprox(ui.Scale.X, 1f))
                throw new AssertionException(
                    $"with the mod off the UI should be drawn at its own size, not {ui.Scale.X}");
        }
        finally
        {
            ActualResolutionApi.Enabled = true;
        }

        yield return Wait.Frames(3);
        var restored = ActualResolutionApi.ViewportSize;
        if (!Mathf.IsEqualApprox(restored.X, modded.X) || !Mathf.IsEqualApprox(restored.Y, modded.Y))
            throw new AssertionException(
                $"turning the mod back on gave a {restored.X}x{restored.Y} frame, where it had " +
                $"been {modded.X}x{modded.Y}");
    }

    /// <summary>
    /// With <c>exactFullscreen</c> on, a fullscreen window renders at the screen's own
    /// resolution rather than two pixels short of it.
    ///
    /// <para><b>This is the one test that proves the setting is wired to anything.</b> It does
    /// not call the correction. It asks for fullscreen the way the display settings page does,
    /// by setting <c>DeviceSettings.Fullscreen</c> and calling <c>ApplySettings</c>, and then
    /// checks the mod noticed, so what it exercises is the postfix on <c>ApplySettings</c>,
    /// <c>WindowWatcher</c> and <c>WindowFrame</c> together. Driving <c>WindowFrame</c>
    /// directly would pass with every one of those deleted.</para>
    ///
    /// <para><b>Not <c>DisplayServer.WindowSetMode</c>,</b> which was the first thing tried and
    /// which this mod does not notice at all. <c>size_changed</c> is a <i>viewport</i> signal,
    /// and the mod pins the viewport with <c>ContentScaleSize</c>, so a window that resizes
    /// under a pinned render target changes nothing the signal watches and it stays silent.
    /// That is a real hole and it is not this test's subject; see the note on
    /// <c>WindowWatcher</c>.</para>
    ///
    /// <para><b>Two assertions, and the order matters.</b> The measurement is the claim: the
    /// client area covers the screen. <see cref="ActualResolutionApi.ExactFullscreenApplied"/>
    /// is checked as well, and only afterwards, so that a screen where fullscreen happens to
    /// be exact for some other reason cannot pass this off as the mod working. Without it the
    /// test would be measuring the display rather than the code.</para>
    ///
    /// <para><b>Needs the game's own setting, not just the mod's.</b>
    /// <c>WindowFrame.Wanted</c> is the conjunction of <c>exactFullscreen</c>, the mod being
    /// enabled, and <c>DeviceSettings.Fullscreen</c>, because a mod has no business overriding
    /// a window the player asked to be a window. So the test has to say both.</para>
    ///
    /// <para><b>Restores before it asserts,</b> and deliberately does not lean on the state
    /// registry to do it. <c>ActualResolutionApi.ResetState</c> pulls the window out of the
    /// exclusive mode, because the reset re-syncs and the correction is then unwanted, but
    /// nothing in it remembers the size the harness was running at. A throw between the switch
    /// and the restore would leave every later test measuring a fullscreen window.</para>
    ///
    /// <para>The fullscreen round trip here is shaped like the one in
    /// <c>RetirementTests.TheEngineStillTakesTheFullscreenWindowsBorderFromItsClientArea</c>,
    /// and is kept separate rather than shared: that one switches the correction <i>off</i> to
    /// measure the engine, this one switches it on to measure the mod, and a helper serving
    /// both would join the two questions the retirement split exists to keep apart.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheFullscreenFrameIsTheWholeScreen()
    {
        // Not a pattern binding: Harness.Inapplicable throws but is not annotated as doing
        // so, so the compiler cannot see a pattern-bound local as assigned afterwards.
        var device = Game.DeviceSettings;
        if (device == null)
            Harness.Inapplicable("the game has no device settings to ask for fullscreen with");

        var mode = DisplayServer.WindowGetMode();
        var size = DisplayServer.WindowGetSize();
        var wasFullscreen = device!.Fullscreen;
        var wasExact = Settings.ExactFullscreen;

        Settings.ExactFullscreen = true;
        device.Fullscreen = true;
        device.ApplySettings();
        yield return Wait.Frames(3);

        var reached = DisplayServer.WindowGetMode();
        var client = DisplayServer.WindowGetSize();
        var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        var applied = ActualResolutionApi.ExactFullscreenApplied;
        var unresampled = ActualResolutionApi.IsUnresampled;

        // Before anything can throw, and in this order: the correction has to become unwanted
        // before the window is asked to go back, or the mod would put it straight into the
        // exclusive mode again on the resize this causes.
        Settings.ExactFullscreen = false;
        device.Fullscreen = wasFullscreen;
        DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed)
            DisplayServer.WindowSetSize(size);
        // Explicitly, not by waiting: the mod does not see a window resize on its own, which
        // is the hole noted above. Without this the render target would stay the size of the
        // fullscreen frame for every test after this one.
        ActualResolutionApi.ResetState();
        yield return Wait.Frames(3);
        Settings.ExactFullscreen = wasExact;

        if (reached == DisplayServer.WindowMode.Windowed)
            Harness.Inapplicable(
                "the window would not go fullscreen on this display, so there is no " +
                "fullscreen frame to measure");

        if (client != screen)
            throw new AssertionException(
                $"a fullscreen window on a {screen.X}x{screen.Y} screen renders into " +
                $"{client.X}x{client.Y}, so it is still short of the screen. " +
                ActualResolutionApi.DescribeState());

        if (!applied)
            throw new AssertionException(
                $"the frame is the screen's {screen.X}x{screen.Y}, but the mod does not claim " +
                "to have done it, so this passed for a reason other than the correction. " +
                ActualResolutionApi.DescribeState());

        if (!unresampled)
            throw new AssertionException(
                "the fullscreen frame covers the screen but is still resampled on its way " +
                $"there. {ActualResolutionApi.DescribeState()}");
    }

    /// <summary>
    /// A window resized by something other than the game keeps the frame with it.
    ///
    /// <para>The case is a player dragging a window edge, a tiling window manager, or a
    /// monitor change: the window moves and nothing in the game is told, so no method this mod
    /// postfixes ever runs. If the frame does not follow, the game draws the old size into the
    /// new window and the engine resamples it, which is precisely the defect the whole mod
    /// exists to remove.</para>
    ///
    /// <para><b>This test was written because the mod failed it.</b> Up to 0.1.2 the resize was
    /// watched through Godot's <c>size_changed</c> signal, which is a <c>Viewport</c> signal and
    /// not a window one: it is emitted from <c>Viewport::_set_size</c>, which returns early when
    /// the viewport size is unchanged, and this mod pins the viewport with
    /// <c>ContentScaleSize</c>. So a resized window left the pinned viewport alone, the signal
    /// stayed silent, and the frame kept its old size until the player next opened the settings
    /// page. See <c>WindowWatcher</c> for the replacement.</para>
    ///
    /// <para><b>Resizes the window directly, which is the point.</b> Every other route into the
    /// mod goes through a method it postfixes, so any of them would pass with the per-frame
    /// check deleted. This one is the only route that does not, which is why it is the only one
    /// that catches this.</para>
    ///
    /// <para>Shrinks rather than grows, so the request cannot be clamped by the screen and come
    /// back as no change at all, which would make the test vacuous.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheFrameFollowsAWindowResize()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
            Harness.Inapplicable("the window is not windowed, so it cannot be resized");

        var original = DisplayServer.WindowGetSize();
        var resized = original - new Vector2I(64, 32);
        if (resized.X <= 0 || resized.Y <= 0)
            Harness.Inapplicable($"a {original.X}x{original.Y} window is too small to shrink");

        DisplayServer.WindowSetSize(resized);
        yield return Wait.Frames(3);

        var window = DisplayServer.WindowGetSize();
        var viewport = ActualResolutionApi.ViewportSize;
        var divisor = ActualResolutionApi.Divisor;
        var unresampled = ActualResolutionApi.IsUnresampled;

        // Before anything can throw. Three frames again, so the check that follows the window
        // back has run before the next test measures anything.
        DisplayServer.WindowSetSize(original);
        yield return Wait.Frames(3);

        if (window == original)
            Harness.Inapplicable(
                $"the window would not resize on this display; it is still " +
                $"{original.X}x{original.Y}");

        var expected = Geometry.ContentSizeFor(window, divisor);
        if (!Mathf.IsEqualApprox(viewport.X, expected.X) || !Mathf.IsEqualApprox(viewport.Y, expected.Y))
            throw new AssertionException(
                $"the window was resized to {window.X}x{window.Y} and the frame should have " +
                $"followed it to {expected.X}x{expected.Y}, but it is {viewport.X}x{viewport.Y}. " +
                "Nothing told the mod the window moved. " + ActualResolutionApi.DescribeState());

        if (!unresampled)
            throw new AssertionException(
                $"the frame followed the window to {viewport.X}x{viewport.Y} but is still " +
                $"resampled on its way to a {window.X}x{window.Y} window. " +
                ActualResolutionApi.DescribeState());

        var restored = DisplayServer.WindowGetSize();
        if (restored != original)
            throw new AssertionException(
                $"the window did not go back to {original.X}x{original.Y}; it is now " +
                $"{restored.X}x{restored.Y}, which every test after this one will measure");
    }

    /// <summary>
    /// A world position that is explored, stationary and away from the spaceship's own
    /// clutter. The same anchor the harness's own screen tests use.
    /// </summary>
    private static Vector2I Anchor()
    {
        if (Game.World?.Spaceship == null)
            Harness.Inapplicable("no spaceship to anchor a world position on");
        return Game.World.Spaceship.GlobalPosition.GlobalToTileposI() + new Vector2I(200, 0);
    }
}
