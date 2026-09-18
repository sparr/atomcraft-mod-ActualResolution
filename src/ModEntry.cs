using System.Reflection;
using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// Entry point, as named by <c>mod.json</c>.
///
/// <para><b>Initialize runs before the game has initialized anything.</b> The mod loader
/// loads every mod during <c>SceneTree._initialize()</c>, and <c>Game._Ready</c> does not run
/// until the following frame, so there is no window, no viewport, no camera and no settings
/// to read here. The only correct thing to do is install patches and return; everything this
/// mod does is driven from them afterwards.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "ActualResolution";

    private static Harmony? _harmony;

    public static void Initialize()
    {
        Settings.Load();

        _harmony = new Harmony(ModId);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        if (!ShadowLayer.Bound)
            Log.Warn("Gameplay no longer has a Shadowmap sprite, so the shadow and fog shader " +
                     "cannot be told the size of the resized frame. Expect that layer to be " +
                     "drawn off the terrain it belongs to, with nothing in the log. This " +
                     "usually means the game updated; the mod needs one too.");

        if (!CameraFields.Complete)
            Log.Warn($"FollowCam no longer has {CameraFields.Missing}, so the camera's limits " +
                     "cannot be restated for the new render target. Expect the view to reach " +
                     "past what the game renders. This usually means the game updated; the " +
                     "mod needs one too.");

        if (!FlagOutlinePatch.Applied)
            Log.Warn($"FlagGrid.{FlagOutlinePatch.Method} no longer offsets the language " +
                     "screen's selection outline the way this mod corrects, so that outline " +
                     "will be drawn wherever the game puts it. Cosmetic, one screen, and it " +
                     "may well mean the game has fixed this itself; see src/FlagOutline.cs.");

        Log.Info($"initialized, {_harmony.GetPatchedMethods().Count()} method(s) patched");
    }
}

/// <summary>
/// Sizes the render target when it needs sizing, and at no other time.
///
/// <para>The window changes size from three places and only three: <c>Game._Ready</c> sets it
/// from the saved device settings and then defers a mode switch to the next frame,
/// <c>SaveData_Device.ApplySettings</c> re-applies both whenever the player changes anything,
/// and the window manager can resize it from outside the game entirely. The first two are
/// methods worth a postfix. The third is what the per-frame check in
/// <see cref="AfterProcess"/> is for.</para>
///
/// <para><b>That check was once a subscription to Godot's <c>size_changed</c> signal, and that
/// was a bug.</b> <c>size_changed</c> belongs to <c>Viewport</c>, not to the window: it is
/// emitted from <c>Viewport::_set_size</c>, which returns early when the viewport's size is
/// unchanged. This mod pins the viewport to <c>ContentScaleSize</c>, so a window that resizes
/// under a pinned render target changes nothing the signal watches, and the signal stays
/// silent. It fired exactly once, at startup, on the way from 1600x900 to the window's size,
/// and then never again. A window resized by the window manager, by dragging an edge, or by a
/// monitor change went unnoticed until the player next opened the settings page. Shipped that
/// way in 0.1.2, and found by
/// <c>ScreenTests.TheFrameFollowsAWindowResize</c>.</para>
///
/// <para>So the per-frame comparison of <c>DisplayServer.WindowGetSize()</c> is back, which is
/// what the mod did before the signal replaced it. It is one <c>GetClientRect</c> and a
/// <c>Vector2I</c> compare, it allocates nothing, and it calls <see cref="Sync"/> only when
/// the window has actually moved, so the mod is still idle whenever nothing is happening. The
/// honest version of "never idle" is that this costs a syscall a frame; the signal cost
/// nothing and did nothing.</para>
///
/// <para>A fourth caller joins these three and changes no window: the postfix on
/// <c>Gameplay.ResizeDisplayTextures</c> in <see cref="ShadowLayerRebindPatch"/>, which is
/// where the game rebinds the shadow material. It comes here rather than calling
/// <see cref="ShadowLayer.Sync"/> itself so that every path that touches the frame shares one
/// fault latch and one switch.</para>
/// </summary>
[HarmonyPatch]
internal static class WindowWatcher
{
    /// <summary>
    /// Set after an unhandled failure, which stops the sizing permanently.
    ///
    /// Godot logs an exception thrown from a signal handler on every emission with no
    /// backpressure, and the render target is not worth a wall of log. One report, then
    /// silence, is strictly more useful.
    /// </summary>
    internal static bool Faulted { get; private set; }

    /// <summary>What went wrong, kept so a test can assert on it rather than grep the log.</summary>
    internal static Exception? Fault { get; private set; }

    /// <summary>The window size the last per-frame check saw, so the check is a comparison.</summary>
    private static Vector2I _lastWindow;

    /// <summary>
    /// Unlatches the fault, so a session that recovers is not disabled for the rest of the run.
    ///
    /// <para><b>Seeds the last window size rather than clearing it.</b> Clearing it makes the
    /// next frame's check see a change that has not happened and call <see cref="Sync"/> once
    /// per test, which is not merely wasteful: it re-applies the mod's own UI scale, so a test
    /// that imposes a scale of its own and then waits has it taken away underneath.
    /// <c>FlagOutlineTests.TheOutlineSitsOnAFlag</c> is that test, and it caught this. Nothing
    /// is lost by seeding, because <c>ActualResolutionApi.ResetState</c> calls <c>Sync</c>
    /// itself immediately afterwards.</para>
    /// </summary>
    internal static void Reset()
    {
        Faulted = false;
        Fault = null;
        _lastWindow = DisplayServer.WindowGetSize();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), "_Ready")]
    internal static void AfterReady()
    {
        Sync();
    }

    /// <summary>
    /// The window manager's half of the job: notice a window that changed size without the
    /// game being told, and bring the frame with it.
    ///
    /// <para>Deliberately cheap enough to run unconditionally. The comparison is what keeps
    /// the mod idle, and it comes before <see cref="Sync"/> rather than inside it, because
    /// <c>Sync</c> is three comparisons across three subsystems and this is one.</para>
    ///
    /// <para><b>The fault latch is what makes a per-frame hook safe here.</b> Godot logs an
    /// exception out of <c>_Process</c> on every frame with no backpressure, and one throwing
    /// hook is worth a million lines of <c>godot.log</c> in ninety seconds. <c>Sync</c> catches,
    /// latches, and reports once; this returns early forever after.</para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), nameof(Game._Process))]
    internal static void AfterProcess()
    {
        if (Faulted)
            return;

        var window = DisplayServer.WindowGetSize();
        if (window == _lastWindow)
            return;

        _lastWindow = window;
        Sync();
    }

    /// <summary>
    /// Every settings change, because this is where the game applies the window size and mode
    /// together. The signal would catch a change of size on its own; this also catches the
    /// case where the size is unchanged and something else about the frame is not.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveData_Device), nameof(SaveData_Device.ApplySettings))]
    internal static void AfterApplySettings() => Sync();

    /// <summary>
    /// Brings the render target, the camera's limits and the UI's scale into line with the
    /// window. Cheap enough to call on any of the three events, and it does nothing at all
    /// when nothing has changed.
    /// </summary>
    internal static void Sync()
    {
        if (Faulted || !ActualResolutionApi.Enabled)
            return;

        try
        {
            if (RenderTarget.SyncIfChanged())
                ActualResolutionApi.RefreshCamera();
            UiLayout.Sync();

            // Unconditionally, not only when the render target changed, because the two can
            // go out of step without the window moving: the shadow sprite does not exist for
            // the whole of Game._Ready, so an event early enough to find nothing to tell would
            // otherwise never be followed up. Today's order puts Gameplay.Init before the
            // ApplySettings that first sizes the frame, so this is insurance rather than a
            // case that fires -- and it costs a comparison, because it does nothing at all
            // when the shader already agrees.
            ShadowLayer.Sync();
        }
        catch (Exception e)
        {
            Faulted = true;
            Fault = e;
            Log.Error("sizing the render target threw and has been disabled for this session. " +
                      "The game is unaffected apart from drawing its fixed 1600x900 frame " +
                      "again, as it does unmodded.");
            Log.Error(e.ToString());
        }
    }
}
