using Atomcraft;
using Godot;

namespace ActualResolution;

/// <summary>
/// What this mod is doing right now, and the one switch that stops it. Everything here is
/// safe to read with no game loaded; the answers are zero or false until there is one.
///
/// <para>This is the surface a test or another mod uses. The patches themselves are internal
/// on purpose: what they hook is this mod's business and changes with the game.</para>
/// </summary>
public static class ActualResolutionApi
{
    private static bool _enabled = true;

    /// <summary>
    /// Whether the mod is doing anything. Setting it false hands the render target back to
    /// the engine, puts the UI back at its drawn size, and makes every patch fall through,
    /// which returns the game to exactly how it shipped without unloading anything.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled && Settings.Enabled;
        set
        {
            // Against the effective value, not the backing field: with the setting file
            // saying off, the field alone would report no change and quietly refuse to
            // switch the mod on.
            if (Enabled == value)
                return;
            _enabled = value;
            Settings.Enabled = value;

            if (value)
            {
                RenderTarget.SyncIfChanged();
                UiLayout.Sync();
            }
            else
            {
                RenderTarget.Restore();
                UiLayout.Restore();
            }
            // After either branch: the window it asks for depends on the value just set, and
            // switching the mod off has to give the window back as well as the render target.
            WindowFrame.Sync();
            RefreshCamera();
            Log.Info(value ? "enabled" : "disabled");
        }
    }

    /// <summary>
    /// Whether sizing the frame threw and was disabled for the session. The game then draws
    /// its shipped 1600x900 frame as it does unmodded, which is a working game and a mod that
    /// is no longer doing anything; see the log for what went wrong.
    /// </summary>
    public static bool Faulted => WindowWatcher.Faulted;

    /// <summary>How many window pixels one render-target pixel currently becomes: 1 when the
    /// game is drawing at the window's own resolution.</summary>
    public static int Divisor => RenderTarget.Divisor;

    /// <summary>The size of the frame the game draws into, which is no longer 1600x900.</summary>
    public static Vector2 ViewportSize => RenderTarget.ViewportSize ?? Vector2.Zero;

    /// <summary>What the game's fixed 1600x900 UI layout is being drawn at.</summary>
    public static float UiScale => UiLayout.Scale;

    /// <summary>
    /// Whether the language screen's selection outline is being placed against the frame the
    /// UI is actually drawn at.
    ///
    /// <para>False means the game has moved the offset this corrects, so the outline is drawn
    /// wherever <c>FlagGrid</c> puts it: still on the flag at a UI scale of 1, and off it by
    /// <c>(scale - 1) * (60, 48)</c> otherwise. See <see cref="FlagOutlinePatch"/>.</para>
    /// </summary>
    public static bool FlagOutlineCorrected => FlagOutlinePatch.Applied;

    /// <summary>
    /// Whether the fullscreen correction is in effect right now: the window is fullscreen
    /// without the border Godot otherwise takes out of its client area, so the frame is the
    /// screen's own resolution. See <see cref="WindowFrame"/>.
    ///
    /// <para>False whenever the player is not asking for fullscreen, or <c>exactFullscreen</c>
    /// is off, both of which are the ordinary case: the default is off, because the border is
    /// what lets other windows draw over the game.</para>
    /// </summary>
    public static bool ExactFullscreenApplied => WindowFrame.Applied;

    /// <summary>
    /// Whether the fullscreen mode the game asks for is being substituted at both of its call
    /// sites, rather than corrected after the fact.
    ///
    /// <para>False is not a broken mod: <see cref="WindowFrame.Sync"/> still brings a
    /// fullscreen window to the mode the setting asks for, so the behavior survives a game
    /// update that moves those calls. It costs an extra window-mode change, and it means the
    /// substitution wants re-reading. Asserted by
    /// <c>RegistrationTests.TheFullscreenModeSubstitutionIsInstalled</c>, because a behavior
    /// test cannot see the difference.</para>
    /// </summary>
    public static bool FullscreenModeSubstituted => WindowFrame.Installed;

    /// <summary>
    /// Whether the finished frame reaches the window without being resampled: the engine's
    /// blit is by a whole number, the same on both axes, and not a reduction.
    ///
    /// <para>This is the claim the mod exists to make, in the form a test can assert. Note
    /// that it cannot be checked by looking at a screenshot: reading the viewport back samples
    /// the render target, <i>before</i> the blit, so a frame about to be mangled reads back
    /// perfect.</para>
    /// </summary>
    public static bool IsUnresampled
    {
        get
        {
            if (RenderTarget.ViewportSize is not { } viewport)
                return false;
            var window = (Vector2)DisplayServer.WindowGetSize();
            var scale = new Vector2(window.X / viewport.X, window.Y / viewport.Y);
            return Mathf.IsEqualApprox(scale.X, scale.Y)
                   && scale.X >= 1f
                   && Mathf.IsEqualApprox(scale.X, Mathf.Round(scale.X));
        }
    }

    /// <summary>
    /// Returns the mod to the state it starts a session in: settings at their defaults, no
    /// fault latched, and the frame actually sized again.
    ///
    /// <para><b>Why this exists and <c>Settings.Reset</c> alone does not do.</b> Settings are
    /// the state a test changes on purpose; this is the state a test leaves behind by
    /// failing. The fault latch is set once and never cleared, so a single throw leaves the
    /// game drawing its shipped 1600x900 frame for every test after it — and a test that
    /// asserts something true of the unmodded game passes anyway, which is a green run that
    /// measured nothing.</para>
    ///
    /// <para><c>_enabled</c> cannot be reached from <c>Settings.Reset</c> at all:
    /// <see cref="Enabled"/> is the conjunction of the two. That matters more here than it
    /// would have before this mod became event-driven — switching it off hands the render
    /// target back to the engine, and with no per-frame sync nothing re-applies it until the
    /// window next changes size, which in a fixed-window test run never happens. So the reset
    /// re-syncs rather than only clearing flags.</para>
    ///
    /// <para>A mod built on the simulation clears its latch on <c>Simulation.Init</c> and
    /// <c>Simulation.Reset</c>, which is where the mod template puts it. This one never
    /// touches the simulation and has no such hook, so the reset belongs to the state
    /// registry instead. Registered by the test mod; see its <c>ModEntry</c>.</para>
    /// </summary>
    public static void ResetState()
    {
        Settings.Reset();
        _enabled = true;
        WindowWatcher.Reset();
        WindowWatcher.Sync();          // enabled again, so put the frame and the UI back
    }

    /// <summary>
    /// Settings and runtime state in one line, for a failure message. Names the fault latch
    /// explicitly, because "the mod is switched off" is the explanation a confusing failure
    /// most often has.
    /// </summary>
    public static string DescribeState()
    {
        var viewport = ViewportSize;
        return $"{Settings.Describe()} faulted={WindowWatcher.Faulted} " +
               $"viewport={viewport.X}x{viewport.Y} divisor={Divisor} uiScale={UiScale}";
    }

    /// <summary>
    /// Recomputes the camera's bounds for the render target as it is now.
    ///
    /// Called after anything that changes the size of the frame. Skipped when there is no
    /// camera or no simulated area yet, because the game's own method dereferences both.
    /// </summary>
    internal static void RefreshCamera()
    {
        var area = Simulation.AvatarSimulationArea;
        if (Client.FollowCam == null || area.ChunksWide <= 0 || area.ChunksTall <= 0)
            return;
        FollowCam.RecalculateMinZoom(area);
    }
}
