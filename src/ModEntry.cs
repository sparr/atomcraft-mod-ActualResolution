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

        if (!CameraFields.Complete)
            Log.Warn($"FollowCam no longer has {CameraFields.Missing}, so the camera's limits " +
                     "cannot be restated for the new render target. Expect the view to reach " +
                     "past what the game renders. This usually means the game updated; the " +
                     "mod needs one too.");

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
/// methods worth a postfix. The third is what Godot's own <c>size_changed</c> signal is for,
/// and subscribing to it costs nothing until it fires.</para>
///
/// <para>An earlier version compared <c>DisplayServer.WindowGetSize()</c> against the last
/// value on every frame, which was simpler and always correct but meant the mod was never
/// idle. The signal covers the same ground: the deferred fullscreen switch arrives as a resize
/// like any other.</para>
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

    /// <summary>Whether the resize signal has been subscribed to, so it is not subscribed twice.</summary>
    private static bool _subscribed;

    /// <summary>
    /// Unlatches the fault, so a session that recovers is not disabled for the rest of the run.
    ///
    /// <para>Deliberately does not touch <see cref="_subscribed"/>: the signal connection
    /// outlives any test and re-subscribing would attach a second handler to the same
    /// signal.</para>
    /// </summary>
    internal static void Reset()
    {
        Faulted = false;
        Fault = null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), "_Ready")]
    internal static void AfterReady(Game __instance)
    {
        Subscribe(__instance);
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

    private static void Subscribe(Node game)
    {
        if (_subscribed)
            return;
        try
        {
            var root = game.GetTree()?.Root;
            if (root == null)
                return;
            root.SizeChanged += Sync;
            _subscribed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not subscribe to the window's resize signal: {e.Message}. The " +
                     "render target will still be sized at startup and on a settings change.");
        }
    }

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
