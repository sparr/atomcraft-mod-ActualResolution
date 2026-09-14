using Atomcraft;
using Godot;

namespace ActualResolution;

/// <summary>
/// Sizes the render target so the engine's final blit to the window is a whole number.
///
/// <para><b>The problem.</b> <c>project.binary</c> sets <c>display/window/stretch/mode =
/// viewport</c> over a viewport of 1600x900, so the game draws every frame into a render
/// target of exactly that size whatever the window is, and the engine rescales the finished
/// image at the end. At the shipped default window of 1280x720 that rescale is 0.8 and drops
/// one row in five; at 1920x1080 it is 1.2 and doubles one row in five. Nothing drawn inside
/// the frame can compensate, because the resample happens after everything in it is
/// drawn.</para>
///
/// <para><b>The fix.</b> Set the window's <c>ContentScaleSize</c> to the window's own size
/// divided by a whole number, so the blit that follows is by that whole number, and put the
/// window into <c>ContentScaleStretchEnum.Integer</c> so the engine is not free to pick a
/// fraction if the division leaves a remainder. The camera's own constants then have to
/// follow the new viewport, which is <see cref="CameraConstantPatches"/>.</para>
///
/// <para><b>Why it is checked every frame.</b> The window changes size from the settings
/// page, from <c>SaveData_Device.ApplySettings</c>, from the deferred fullscreen switch in
/// <c>Game._Ready</c>, and from the window manager. Rather than chase each of those, this
/// compares the window size it last acted on against the current one, which is a single
/// <c>DisplayServer.WindowGetSize</c> per frame.</para>
/// </summary>
public static class RenderTarget
{
    /// <summary>The window size the current render target was computed from.</summary>
    private static Vector2I _appliedWindow;

    /// <summary>The divisor the current render target was computed with.</summary>
    private static int _appliedDivisor;

    /// <summary>What the engine had before this mod touched it, so it can be handed back.</summary>
    private static Vector2I _originalSize;
    private static Window.ContentScaleModeEnum _originalMode;
    private static Window.ContentScaleAspectEnum _originalAspect;
    private static Window.ContentScaleStretchEnum _originalStretch;
    private static bool _captured;

    /// <summary>How many window pixels one render-target pixel currently becomes.</summary>
    public static int Divisor => _appliedDivisor;

    /// <summary>The render target's size, or the viewport's size when the mod is off.</summary>
    public static Vector2I ContentSize => Root?.ContentScaleSize ?? Vector2I.Zero;

    /// <summary>
    /// The root window, or null before the scene tree has one.
    ///
    /// The mod loader runs mods from <c>SceneTree._initialize()</c>, before the game's scene
    /// has been instanced, so everything here has to tolerate not having a window yet.
    /// </summary>
    private static Window? Root => (Engine.GetMainLoop() as SceneTree)?.Root;

    /// <summary>
    /// Brings the render target into line with the window if either the window or the chosen
    /// divisor has changed since the last call, and reports whether it did anything.
    ///
    /// A true return means the viewport size has changed under the camera, and the caller is
    /// expected to recompute the camera's bounds for it.
    /// </summary>
    public static bool SyncIfChanged()
    {
        var root = Root;
        if (root == null)
            return false;

        var window = DisplayServer.WindowGetSize();
        if (window.X <= 0 || window.Y <= 0)
            return false;

        var divisor = Geometry.DivisorFor(window, Settings.RenderDivisor);
        if (window == _appliedWindow && divisor == _appliedDivisor)
            return false;

        Capture(root);

        var content = Geometry.ContentSizeFor(window, divisor);
        root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        root.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;
        root.ContentScaleSize = content;

        _appliedWindow = window;
        _appliedDivisor = divisor;

        Log.Info($"window {window.X}x{window.Y} -> render target {content.X}x{content.Y} " +
                 $"at {divisor}x");
        return true;
    }

    /// <summary>
    /// Puts back what the engine had, for <see cref="ActualResolutionApi.Enabled"/> being set
    /// false. Returns whether anything changed, on the same contract as
    /// <see cref="SyncIfChanged"/>.
    /// </summary>
    public static bool Restore()
    {
        var root = Root;
        if (root == null || !_captured)
            return false;

        root.ContentScaleMode = _originalMode;
        root.ContentScaleAspect = _originalAspect;
        root.ContentScaleStretch = _originalStretch;
        root.ContentScaleSize = _originalSize;

        _appliedWindow = Vector2I.Zero;
        _appliedDivisor = 0;
        Log.Info($"render target handed back to the engine at {_originalSize.X}x{_originalSize.Y}");
        return true;
    }

    /// <summary>
    /// Remembers the engine's own settings, once, before they are first overwritten.
    ///
    /// Read from the live window rather than from <c>project.binary</c>, so restoring puts
    /// back what this install actually had rather than what this mod assumes it had.
    /// </summary>
    private static void Capture(Window root)
    {
        if (_captured)
            return;
        _originalSize = root.ContentScaleSize;
        _originalMode = root.ContentScaleMode;
        _originalAspect = root.ContentScaleAspect;
        _originalStretch = root.ContentScaleStretch;
        _captured = true;
    }

    /// <summary>
    /// The viewport the game is currently drawing into, or null when there is no game yet.
    ///
    /// Read through <c>Game.CanvasLayer</c>, which is the same viewport the game's own
    /// <c>Utils.ScreenPositionToWorldPosition</c> and <c>InputManager.GetMouseScreenPosition</c>
    /// measure against, so this mod and the game never disagree about how big the frame is.
    /// </summary>
    public static Vector2? ViewportSize
    {
        get
        {
            var size = Game.CanvasLayer?.GetViewport()?.GetVisibleRect().Size;
            if (size is not { X: > 0f, Y: > 0f })
                return null;
            return size;
        }
    }
}
