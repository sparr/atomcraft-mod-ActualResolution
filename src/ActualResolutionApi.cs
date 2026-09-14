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
            RefreshCamera();
            Log.Info(value ? "enabled" : "disabled");
        }
    }

    /// <summary>How many window pixels one render-target pixel currently becomes: 1 when the
    /// game is drawing at the window's own resolution.</summary>
    public static int Divisor => RenderTarget.Divisor;

    /// <summary>The size of the frame the game draws into, which is no longer 1600x900.</summary>
    public static Vector2 ViewportSize => RenderTarget.ViewportSize ?? Vector2.Zero;

    /// <summary>What the game's fixed 1600x900 UI layout is being drawn at.</summary>
    public static float UiScale => UiLayout.Scale;

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
