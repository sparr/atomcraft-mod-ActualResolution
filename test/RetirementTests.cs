using Atomcraft.TestHarness;
using Godot;

namespace ActualResolution.Test;

/// <summary>
/// The part of this mod that exists only because the game renders into a frame that is not the
/// window, and the question of whether it still does.
///
/// <para><b>A failure in this file is good news.</b> Each test asserts the game still has the
/// property this mod works around, so red means the game changed and something here can be
/// deleted. That is a different question from "is the mod correct", and mixing the two makes a
/// red suite unreadable, so these are excluded from the default run and asked for
/// deliberately:</para>
/// <code>
/// ./run-tests.sh --retirement
/// </code>
/// </summary>
public static class RetirementTests
{
    /// <summary>
    /// The game is still configured to draw into a fixed frame and let the engine rescale it.
    ///
    /// <para><b>Read from the project settings, not from the live viewport.</b> This mod resizes
    /// the render target at runtime through <c>Window.ContentScaleSize</c> and never touches the
    /// project setting, so the setting still answers for the shipped game while the mod is
    /// installed. Asking the viewport instead would just report the mod back to itself.</para>
    ///
    /// <para><b>When this fails:</b> if <c>stretch/mode</c> is no longer <c>viewport</c>, or the
    /// configured size is no longer a fixed design size, the game has taken this over and the
    /// whole of RenderTarget and UiLayout can go, along with the camera-constant patches that
    /// exist only to restate figures measured against that fixed frame.</para>
    /// </summary>
    [GameTest]
    public static void TheGameStillRendersIntoAFixedFrame()
    {
        var mode = ProjectSettings.GetSetting("display/window/stretch/mode").AsString();
        if (mode != "viewport")
            throw new AssertionException(
                $"display/window/stretch/mode is now '{mode}', not 'viewport'. The game no " +
                "longer draws into a fixed frame for the engine to rescale; see this test's " +
                "doc comment for what can be deleted.");

        var width = ProjectSettings.GetSetting("display/window/size/viewport_width").AsInt32();
        var height = ProjectSettings.GetSetting("display/window/size/viewport_height").AsInt32();
        if (width != 1600 || height != 900)
            throw new AssertionException(
                $"the configured frame is now {width}x{height}, not 1600x900. Every constant " +
                "this mod restates was measured against 1600x900, so they are all wrong by the " +
                "ratio between the two sizes.");
    }
}
