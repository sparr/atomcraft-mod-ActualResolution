using System.Reflection;
using System.Reflection.Emit;
using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// Takes back the pixel of each edge that Godot's non-exclusive fullscreen spends on a window
/// border, so a fullscreen game renders at the screen's own resolution.
///
/// <para><b>The defect.</b> The game asks for <c>WINDOW_MODE_FULLSCREEN</c> (mode 3) in
/// <c>SaveData_Device.ApplySettings</c> and in <c>Game.SetWindowMode</c>. That is Godot's
/// <i>non-exclusive</i> fullscreen, and its Windows backend -- the one in play, since this is
/// a Windows build under Proton -- adds <c>WS_BORDER</c> to such a window, with the comment
/// "Allows child windows to be displayed on top of full screen". It then moves the window's
/// <i>outer</i> rect to the screen, so the 1px non-client frame comes out of the client area:
/// a 1920x1080 screen renders into 1918x1078 behind a 1px border that Godot paints itself in
/// the renderer's clear color. Measured 2026-09-17.</para>
///
/// <para>Nothing drawn can reclaim those pixels, because the rendering surface is the client
/// rect and the border is outside it. Raising <c>ContentScaleSize</c> past the client size
/// makes it worse rather than better: the engine then scales a larger frame down into the same
/// client rect. The only way to have the pixels is to have a client area that covers the
/// screen.</para>
///
/// <para><b>The fix is one constant.</b> Mode 4, <c>WINDOW_MODE_EXCLUSIVE_FULLSCREEN</c>,
/// clears Godot's <c>multiwindow_fs</c> and so fails the border's one condition,
/// <c>(fullscreen &amp;&amp; multiwindow_fs) || maximized_fs</c>. The window rect does not
/// move; only the style changes, and the client area grows into the two pixels the border had.
/// As a bonus the frame then divides the 1600x900 design layout exactly on a 16:9 screen,
/// which also retires the sub-pixel pillarbox 1918x1078 was producing.</para>
///
/// <para><b>Substituted where the game asks, rather than corrected afterwards.</b> An earlier
/// version let the game set mode 3 and then set mode 4 behind it, which meant reading the live
/// window back to find out whether the correction had survived, re-asserting it from three
/// places, and doing that three times per boot. Replacing the argument at the two call sites
/// instead leaves the game's own window logic untouched and running exactly once, with one
/// constant different. What that deletes is the interesting part: the readback had to compare
/// the window's <i>size</i> as well as its mode, because Godot infers fullscreen from geometry
/// (<c>WM_WINDOWPOSCHANGED</c>) and a mod asking whether its own state survived could be lied
/// to. Substitution never has to ask.</para>
///
/// <para><b>"Exclusive" is a misnomer on this backend and costs less than it sounds.</b> Godot
/// never calls <c>ChangeDisplaySettings</c>; the only <c>EnumDisplaySettings</c> in the Windows
/// display server is a read inside <c>screen_get_size</c>. So there is no display mode change,
/// no resolution switch, and no alt-tab penalty to buy here. Between modes 3 and 4 there is
/// exactly one difference on this renderer: the <c>WS_BORDER</c> bit, and therefore the client
/// size. What is genuinely given up is what that comment says, other windows drawing on top of
/// the game, which is the trade the <c>exactFullscreen</c> setting exists to let a player make,
/// and the reason it is off by default.</para>
///
/// <para>Under Proton even that is not given up: Wine decides fullscreen geometrically
/// (<c>is_window_rect_full_screen</c>, covers-at-least, style never consulted), and modes 3 and
/// 4 produce the identical window rect, so both get the same <c>_NET_WM_STATE_FULLSCREEN</c>.
/// <c>WS_BORDER</c> is a DWM workaround with no analogue on X11.</para>
/// </summary>
internal static class WindowFrame
{
    /// <summary>
    /// The private method <c>Game._Ready</c> defers the fullscreen switch to. Spelled rather
    /// than <c>nameof</c>'d, so <see cref="Installed"/> checks it at startup.
    /// </summary>
    internal const string Method = "SetWindowMode";

    /// <summary>
    /// Whether the substitution was woven into both of the game's calls.
    ///
    /// <para>Counted rather than flagged, because one of two is the dangerous outcome: the
    /// setting would work from the settings page and not at startup, or the other way round,
    /// which reads as an intermittent fault rather than as a game update.</para>
    /// </summary>
    internal static bool Installed => _woven >= 2;

    /// <summary>Whether the window is currently fullscreen without the border.</summary>
    internal static bool Applied { get; private set; }

    private static int _woven;

    /// <summary>Counts a call site the transpiler replaced. See <see cref="Installed"/>.</summary>
    internal static void Weave() => _woven++;

    /// <summary>
    /// Whether the player is asking for this mod to deliver fullscreen without the border.
    ///
    /// <para>Deliberately does not ask whether the player wants fullscreen: the game calling
    /// <c>WindowSetMode</c> with mode 3 <i>is</i> that question already answered, which is one
    /// more thing substitution does not have to look up.</para>
    /// </summary>
    internal static bool Wanted => Settings.ExactFullscreen && ActualResolutionApi.Enabled;

    /// <summary>
    /// Stands in for <c>DisplayServer.WindowSetMode</c> at the game's two call sites, with the
    /// same signature so the swap is one operand.
    ///
    /// <para>Every mode the game asks for is passed through untouched except the one being
    /// corrected, so a player who turns fullscreen off gets exactly what the game intended.</para>
    /// </summary>
    internal static void SetWindowMode(DisplayServer.WindowMode mode, int windowId)
    {
        var substitute = mode == DisplayServer.WindowMode.Fullscreen && Wanted;
        if (substitute)
            mode = DisplayServer.WindowMode.ExclusiveFullscreen;

        DisplayServer.WindowSetMode(mode, windowId);
        Applied = substitute;
    }

    /// <summary>
    /// Brings the live window into line when the answer changed without the game asking again:
    /// the player toggled the mod, or a test toggled the setting.
    ///
    /// <para>The substitution covers every case where the game sets the mode itself, which is
    /// every case a player will meet. This covers the rest, and it is why
    /// <see cref="ActualResolutionApi.Enabled"/> can be switched off mid-session and leave the
    /// window as the unmodded game would have had it.</para>
    ///
    /// <para>Does nothing unless the window is already fullscreen. A mod has no business
    /// putting a window the player asked to be a window into fullscreen, and nothing here ever
    /// does: it only ever chooses <i>which</i> fullscreen.</para>
    /// </summary>
    internal static bool Sync()
    {
        var mode = DisplayServer.WindowGetMode();
        if (mode != DisplayServer.WindowMode.Fullscreen
            && mode != DisplayServer.WindowMode.ExclusiveFullscreen)
        {
            Applied = false;
            return false;
        }

        var wanted = Wanted;
        var want = wanted
            ? DisplayServer.WindowMode.ExclusiveFullscreen
            : DisplayServer.WindowMode.Fullscreen;

        Applied = wanted;
        if (mode == want)
            return false;

        DisplayServer.WindowSetMode(want);
        Log.Info(wanted
            ? $"fullscreen without the window border: {DisplayServer.WindowGetSize()}"
            : "fullscreen border handed back to the game");
        return true;
    }
}

/// <summary>
/// The two places the game sets the window mode, and the one operand that decides whether a
/// fullscreen window keeps its last two pixels.
///
/// <para>Both are <c>DisplayServer.WindowSetMode(mode, windowId)</c>. The second argument is
/// defaulted at one of the call sites and spelled at the other, which makes no difference to
/// the IL: C# emits both, so one <c>MethodInfo</c> matches both and one substitute with the
/// same signature replaces both.</para>
///
/// <para>A miss returns the method untouched and is reported at startup rather than thrown. A
/// transpiler that throws takes the game's method with it, and one of these two is
/// <c>ApplySettings</c>, which the game cannot finish <c>_Ready</c> without.</para>
/// </summary>
[HarmonyPatch]
internal static class WindowModePatch
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets()
    {
        var applySettings = AccessTools.Method(typeof(SaveData_Device),
                                               nameof(SaveData_Device.ApplySettings));
        if (applySettings != null)
            yield return applySettings;

        var setWindowMode = AccessTools.Method(typeof(Game), WindowFrame.Method);
        if (setWindowMode != null)
            yield return setWindowMode;
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(DisplayServer),
                                          nameof(DisplayServer.WindowSetMode),
                                          new[] { typeof(DisplayServer.WindowMode), typeof(int) });
        var substitute = AccessTools.Method(typeof(WindowFrame), nameof(WindowFrame.SetWindowMode));
        if (original == null || substitute == null)
            return instructions;

        var matcher = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Call, original));
        if (matcher.IsInvalid)
            return instructions;

        WindowFrame.Weave();
        return matcher.SetInstruction(new CodeInstruction(OpCodes.Call, substitute))
                      .InstructionEnumeration();
    }

}

/// <summary>
/// The frame is sized from the window, and the deferred mode switch is the one place the
/// window changes without the game having been asked for anything else. A postfix here spares
/// it the frame of wrong-sized rendering that waiting for the per-frame check in
/// <c>WindowWatcher</c> would cost.
///
/// <para>Its own class because Harmony refuses to combine <c>[HarmonyTargetMethods]</c> with a
/// per-method target in one patch class, and <see cref="WindowModePatch"/> needs the former to
/// reach two methods with one transpiler. The refusal is loud: the mod fails to load
/// outright.</para>
/// </summary>
[HarmonyPatch(typeof(Game), WindowFrame.Method)]
internal static class WindowModeSyncPatch
{
    [HarmonyPostfix]
    internal static void AfterSetWindowMode() => WindowWatcher.Sync();
}
