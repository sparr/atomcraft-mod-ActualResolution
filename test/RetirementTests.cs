using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using HarmonyLib;

// The game has a Session type of its own; the harness's is the one meant here.
using Session = Atomcraft.TestHarness.Session;

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


    /// <summary>
    /// The game still never tells the shadow and fog shader how big the frame is.
    ///
    /// <para><c>Art/Shaders/Shadowmap.tres</c> maps its texture with
    /// <c>SCREEN_UV * viewport_size</c>, and <c>Gameplay.RefreshShaderParameters</c> sets that
    /// material's <c>lightmap_size</c> and <c>lightmap_uv_offset</c> every frame and never its
    /// <c>viewport_size</c>. The only assignment of that uniform in the whole assembly is on
    /// the heatwave material. So the shadow layer runs on whatever is baked into the material,
    /// which is 1600x900, which is right only while the game always renders at exactly that.
    /// </para>
    ///
    /// <para><b>Measured rather than read.</b> A sentinel goes into the uniform, the game is
    /// allowed to draw several frames, and the sentinel is still there afterwards. Asserting it
    /// this way means the test reports on the running game rather than on what the decompiled
    /// source looked like the day this was written.</para>
    ///
    /// <para><b>When this fails:</b> the game has started setting the uniform itself, and
    /// <c>src/ShadowLayer.cs</c> can go, along with its two calls in <c>RenderTarget</c>, its
    /// call in <c>WindowWatcher.Sync</c>, the <c>Gameplay.ResizeDisplayTextures</c> entry in
    /// <c>RegistrationTests</c>, and the startup check in <c>ModEntry.Initialize</c>. The
    /// conformance suite's <c>TheShadowLayerIsMappedWithTheRealFrameSize</c> stays: that asks
    /// whether the property holds, not who is holding it up.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator TheGameStillNeverSetsTheShadowShadersFrameSize()
    {
        yield return Session.Enter("flat");
        yield return Wait.Frames(2);

        if (AccessTools.Field(typeof(Gameplay), "Shadowmap")?.GetValue(null) is not Sprite2D sprite
            || sprite.Material is not ShaderMaterial material)
        {
            Harness.Inapplicable(
                "Gameplay no longer has a Shadowmap sprite with a shader material, which is a " +
                "bigger change than this test is asking about; see ShadowLayer.cs");
            yield break;
        }

        // Not a frame size, and not any size this mod would have written.
        var sentinel = new Vector2(4242f, 2424f);
        material.SetShaderParameter("viewport_size", sentinel);

        // Long enough for the game's own per-frame shader refresh to run several times. The
        // mod does not re-assert this on a timer -- it sets it from the events that resize the
        // frame, and none of them fires while the window sits still -- so anything that puts
        // the sentinel back is the game.
        yield return Wait.Frames(5);

        var after = material.GetShaderParameter("viewport_size");
        var survived = after.VariantType == Variant.Type.Vector2
                       && after.AsVector2().IsEqualApprox(sentinel);

        // Put the shader back before anything can throw, so a failure here costs one test
        // rather than every test after it.
        ShadowLayer.Sync();

        if (!survived)
            throw new AssertionException(
                $"the shadow shader's viewport_size was set to {sentinel} and the game changed " +
                $"it to {after}. The game now maintains this uniform itself; see this test's " +
                "doc comment for what can be deleted.");

        yield return Session.Leave();
    }
}
