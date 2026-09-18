using System.Collections;
using System.Reflection.Emit;
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
    /// The game still adds the language screen's outline offset in the frame's coordinates
    /// rather than the UI layout's.
    ///
    /// <para><c>FlagGrid.SetHighlightOnCurrentLocale</c> does</para>
    /// <code>
    /// FlagOutline.GlobalPosition = value.GlobalPosition + new Vector2(-60f, -48f);
    /// </code>
    /// <para>where the constant is a measurement of the 1600x900 layout and the position is in
    /// the frame. Godot converts an assigned <c>GlobalPosition</c> back through the parent
    /// chain, so a scale on an ancestor divides that constant and the outline sits
    /// <c>(scale - 1) * (60, 48)</c> away from the flag. It is right in the shipped game only
    /// because the UI is never scaled there.</para>
    ///
    /// <para><b>Read from the original IL,</b> which is where the claim lives: this is about
    /// what the game's own code does, and the mod has a transpiler inside that very method.
    /// Harmony redirects a patched method at runtime and leaves its body in metadata, so
    /// <c>GetOriginalInstructions</c> answers for the game rather than for the mod. Measuring
    /// the outline's position instead would just report the correction back to itself; that
    /// assertion belongs in <see cref="FlagOutlineTests"/>, and it is the one that stays.</para>
    ///
    /// <para><b>When this fails:</b> the game has changed how it places that outline. If it
    /// now scales the offset — through <c>GetGlobalTransform().BasisXform</c>, by working in
    /// the outline's own parent space, or by anchoring the outline to the flag — then
    /// <c>src/FlagOutline.cs</c> can go, along with <c>Geometry.LayoutDistance</c> and its test
    /// in <see cref="GeometryTests"/>, <c>FlagOutlineCorrected</c> on the API, the startup
    /// check in <c>ModEntry.Initialize</c>, <c>TheFlagOutlineCorrectionIsInstalled</c> in
    /// <see cref="RegistrationTests"/>, and this test. Keep
    /// <c>FlagOutlineTests.TheOutlineSitsOnAFlag</c>: it asks whether the outline is on the
    /// flag, not who put it there. If the code merely moved, the mod needs an update rather
    /// than a deletion, and the two are told apart by reading the method.</para>
    /// </summary>
    [GameTest]
    public static void TheGameStillOffsetsTheFlagOutlineInFrameSpace()
    {
        var method = AccessTools.Method(typeof(FlagGrid), "SetHighlightOnCurrentLocale")
                     ?? throw new AssertionException(
                         "the game no longer has FlagGrid.SetHighlightOnCurrentLocale at all; " +
                         "see this test's doc comment for what that decides");

        var moveNext = AccessTools.AsyncMoveNext(method)
                       ?? throw new AssertionException(
                           "FlagGrid.SetHighlightOnCurrentLocale is no longer async, so the " +
                           "assignment this asks about has been rewritten; see this test's doc " +
                           "comment");

        var ctor = AccessTools.Constructor(typeof(Vector2), new[] { typeof(float), typeof(float) });
        var plus = AccessTools.Method(typeof(Vector2), "op_Addition",
                                      new[] { typeof(Vector2), typeof(Vector2) });
        if (ctor == null || plus == null)
        {
            Harness.Inapplicable("Godot's Vector2 no longer has the members this reads for");
            return;
        }

        var il = PatchProcessor.GetOriginalInstructions(moveNext);
        var found = false;
        for (var i = 0; i + 3 < il.Count && !found; i++)
            found = il[i].LoadsConstant(-60d)
                    && il[i + 1].LoadsConstant(-48d)
                    && il[i + 2].opcode == OpCodes.Newobj
                    && Equals(il[i + 2].operand, ctor)
                    && il[i + 3].Calls(plus);

        if (!found)
            throw new AssertionException(
                "FlagGrid no longer builds an unscaled (-60, -48) and adds it to the selected " +
                "flag's global position. Something about how the game places that outline has " +
                "changed; see this test's doc comment for what to delete and what to update.");
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

    /// <summary>
    /// The engine still takes a pixel of each edge of a fullscreen window's client area for a
    /// border, so a fullscreen game renders at less than the screen's resolution.
    ///
    /// <para><b>The subject of this test is Godot, not Atomcraft.</b> Every other test in this
    /// file asks whether the game's own code or configuration has changed. This one asks about
    /// <c>DisplayServerWindows</c>: <c>WINDOW_MODE_FULLSCREEN</c> adds <c>WS_BORDER</c> to the
    /// window and then moves its <i>outer</i> rect to the screen, so the border comes out of
    /// the client area and a 1920x1080 screen renders into 1918x1078. So this one retires on a
    /// Godot version bump rather than on anything the Atomcraft developer writes, and whoever
    /// reads it red should look in the engine's changelog rather than in the game.</para>
    ///
    /// <para>It is already fixed upstream. Godot 4.5 dropped <c>WS_BORDER</c> and made the
    /// window two pixels wider than the screen instead, clipping the spare strip out of the
    /// mouse region; 4.6 chooses the axis by which edge has no adjacent monitor. Atomcraft
    /// ships on 4.4, where the defect is live. So the assertion below accepts only a client
    /// area <i>smaller</i> than the screen: equal is the 4.4 defect repaired, and larger is
    /// the 4.5 fix, and either means this mod has nothing left to do.</para>
    ///
    /// <para><b>Measured rather than read from <c>Engine.GetVersionInfo</c>.</b> A version
    /// comparison would go red at roughly the right moment and prove nothing about the
    /// property, and it would miss a backport into a 4.4 point release.</para>
    ///
    /// <para><b>Switches the mod's own correction off while measuring.</b> With
    /// <c>exactFullscreen</c> on and the game's device settings asking for fullscreen, the mod
    /// would convert the window to mode 4 the moment this test entered fullscreen, and the
    /// measurement would report the mod back to itself as an engine fix. Same hazard, and the
    /// same answer, as <c>FlagOutlineTests.TheOutlineSitsOnAFlag</c> imposing a UI scale of its
    /// own.</para>
    ///
    /// <para><b>When this fails:</b> the engine hands a fullscreen window the whole screen, so
    /// <c>src/WindowFrame.cs</c> can go in its entirety, along with the <c>exactFullscreen</c>
    /// key in <c>src/Settings.cs</c> (field, <c>Load</c>, <c>Describe</c>, <c>Reset</c> and the
    /// comment in <c>Save</c>), the <c>WindowFrame.Bound</c> check in
    /// <c>ModEntry.Initialize</c>, the <c>WindowFrame.Sync</c> call in <c>WindowWatcher.Sync</c>
    /// and in the <c>ActualResolutionApi.Enabled</c> setter, and this test. Check
    /// <c>Geometry</c> before deleting anything else: the 4.5 fix makes the fullscreen frame
    /// two columns <i>wider</i> than the display, which is not 16:9, so the aspect handling and
    /// the <c>integerLimits</c> rounding want re-reading against a frame that is deliberately
    /// bigger than the screen it is shown on.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheEngineStillTakesTheFullscreenWindowsBorderFromItsClientArea()
    {
        var mode = DisplayServer.WindowGetMode();
        var size = DisplayServer.WindowGetSize();
        var correcting = Settings.ExactFullscreen;

        Settings.ExactFullscreen = false;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        yield return Wait.Frames(2);

        var reached = DisplayServer.WindowGetMode();
        var client = DisplayServer.WindowGetSize();
        var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());

        // Restore before anything can throw, so a failure here costs one test rather than
        // every test after it, and so the window the rest of the run measures is the one it
        // started with. The mod's own resize signal brings the render target back with it.
        DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed)
            DisplayServer.WindowSetSize(size);
        Settings.ExactFullscreen = correcting;
        yield return Wait.Frames(2);

        // Not the same question as the one being asked. Without this a window that never went
        // fullscreen would report its smaller windowed size and pass for the wrong reason,
        // which is the one way this test could go quietly green while saying nothing.
        if (reached != DisplayServer.WindowMode.Fullscreen)
            Harness.Inapplicable(
                $"asked for fullscreen and got {reached}, so there is no fullscreen client " +
                "area to measure on this display");

        if (client.X >= screen.X && client.Y >= screen.Y)
            throw new AssertionException(
                $"a fullscreen window on a {screen.X}x{screen.Y} screen now has a " +
                $"{client.X}x{client.Y} client area, so the engine no longer spends the " +
                "border on it. See this test's doc comment for what can be deleted.");
    }
}
