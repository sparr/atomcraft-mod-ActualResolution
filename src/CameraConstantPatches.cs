using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// The private state of <c>FollowCam</c> this mod has to reach, resolved once.
///
/// <para>Each is null if the game renames it, and every use is guarded, so a game update
/// costs this mod a feature and a log line rather than throwing from a per-frame path. Godot
/// logs an exception out of <c>_Process</c> on every frame with no backpressure at all, so a
/// patch that can fail has to fail quietly.</para>
/// </summary>
internal static class CameraFields
{
    /// <summary>
    /// <c>FollowCam.CameraMinZoom</c>: the widest view the camera may take, recomputed from
    /// the simulated area whenever it changes. Assigned here so it accounts for the resized
    /// render target rather than the 1600x900 one its constants were measured against.
    /// </summary>
    internal static readonly AccessTools.FieldRef<float>? MinZoom =
        AccessTools.Field(typeof(FollowCam), "CameraMinZoom") is { } min
            ? AccessTools.StaticFieldRefAccess<float>(min)
            : null;

    /// <summary>
    /// <c>FollowCam.TargetZoom</c>: where the zoom keys are steering, which the camera eases
    /// toward. Needed only to move the zoom-in limit; see <see cref="ZoomCeilingPatch"/>.
    /// </summary>
    internal static readonly AccessTools.FieldRef<float>? TargetZoom =
        AccessTools.Field(typeof(FollowCam), "TargetZoom") is { } target
            ? AccessTools.StaticFieldRefAccess<float>(target)
            : null;

    /// <summary>Whether everything this mod reaches for is still there.</summary>
    internal static bool Complete => MinZoom != null && TargetZoom != null;

    /// <summary>Names what is missing, for one log line at startup.</summary>
    internal static string Missing =>
        string.Join(", ",
            new[]
            {
                MinZoom == null ? "CameraMinZoom" : null,
                TargetZoom == null ? "TargetZoom" : null,
            }.Where(name => name != null)!);
}

/// <summary>
/// Recomputes the camera's widest view, and the parallax background's scale, for a render
/// target that is no longer 1600x900.
///
/// <para><c>RecalculateMinZoom</c> divides two cell counts measured against the design
/// viewport by the size of the simulated area. On a larger render target those counts are
/// short, so the camera is allowed to zoom out until it is looking past the window of world
/// the game actually renders, and the edges of the screen go black. Scaling each count by
/// its own axis ratio is the whole correction, and it has the property that matters most
/// here: the widest view still shows the same number of cells it did before this mod.</para>
///
/// <para>A postfix rather than a replacement, so the game keeps ownership of when this
/// happens and of everything else it does.</para>
/// </summary>
[HarmonyPatch(typeof(FollowCam), nameof(FollowCam.RecalculateMinZoom))]
internal static class RecalculateMinZoomPatch
{
    [HarmonyPostfix]
    internal static void Postfix(SimulationArea newArea)
    {
        if (!ActualResolutionApi.Enabled || CameraFields.MinZoom == null)
            return;
        if (RenderTarget.ViewportSize is not { } viewport)
            return;

        var camera = Client.FollowCam;
        if (camera == null)
            return;

        var minZoom = Geometry.MinZoom(viewport, newArea.GetWidth(), newArea.GetHeight(),
                                      Settings.IntegerLimits);
        if (!float.IsFinite(minZoom) || minZoom <= 0f)
            return;

        CameraFields.MinZoom() = minZoom;

        // The parallax background is a child of the camera, so it is drawn through the same
        // zoom, and the game sizes it to cover the screen at the widest view. Its constants
        // were measured against the design viewport too.
        if (Client.Background != null)
            Client.Background.Scale = new Vector2(
                Geometry.BackgroundScaleX * Geometry.WidthRatio(viewport) / minZoom,
                Geometry.BackgroundScaleY * Geometry.HeightRatio(viewport) / minZoom);

        camera.Zoom = new Vector2(minZoom, minZoom);
        if (CameraFields.TargetZoom != null)
            CameraFields.TargetZoom() = minZoom;
    }
}

/// <summary>
/// The two cell counts the camera clamps its own position with,
/// <c>FollowCam.CameraCellWidth</c> and <c>CameraCellHeight</c>.
///
/// They feed <c>Min_X</c>, <c>Min_Y</c>, <c>Max_X</c> and <c>Max_Y</c>, which stop the camera
/// before it can see past the edge of the world. Left at their design-viewport values they
/// under-count the cells on screen on a larger render target, and the camera walks far enough
/// into the corner to show what is not there.
/// </summary>
[HarmonyPatch]
internal static class CameraExtentPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FollowCam), "CameraCellWidth", MethodType.Getter)]
    internal static void Width(ref int __result) =>
        __result = Scale(__result, Geometry.CameraCellsWide, horizontal: true);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FollowCam), "CameraCellHeight", MethodType.Getter)]
    internal static void Height(ref int __result) =>
        __result = Scale(__result, Geometry.CameraCellsTall, horizontal: false);

    /// <summary>
    /// Recomputes the game's own expression with the axis ratio folded in, rather than
    /// scaling its result, so the truncation happens where the game puts it.
    /// </summary>
    private static int Scale(int original, float cells, bool horizontal)
    {
        if (!ActualResolutionApi.Enabled || CameraFields.MinZoom == null)
            return original;
        if (RenderTarget.ViewportSize is not { } viewport)
            return original;

        var minZoom = CameraFields.MinZoom();
        if (!float.IsFinite(minZoom) || minZoom <= 0f)
            return original;

        var ratio = horizontal ? Geometry.WidthRatio(viewport) : Geometry.HeightRatio(viewport);
        return (int)(cells * ratio / minZoom);
    }
}

/// <summary>
/// Moves the zoom-in limit with the render target, so the closest view keeps the apparent
/// size it has in the shipped game.
///
/// <para>The game clamps its easing target at 1.5, a figure chosen when the engine was
/// always going to blow the frame up to the window afterwards. Now that the frame is the
/// window's size, stopping at 1.5 would leave every player above 900 lines zoomed further
/// out than the game shipped: at 1080p the closest view would be 12 screen pixels per cell
/// where it used to be 14.4.</para>
///
/// <para>The limit is moved by biasing the target down before the original runs and back up
/// after, instead of reimplementing the clamp. The game's zoom speed, its arithmetic and its
/// clamp all stay in charge, so a retune of any of them is inherited. Two mods biasing the
/// same clamp compose without either knowing about the other, because both are addition; the
/// TestHarness raises the same ceiling the same way for its screenshots.</para>
///
/// <para>Zooming out is deliberately not biased: its floor is <c>CameraMinZoom</c>, which
/// <see cref="RecalculateMinZoomPatch"/> has already restated for this render target.</para>
/// </summary>
[HarmonyPatch(typeof(FollowCam), nameof(FollowCam.IncreaseZoom))]
internal static class ZoomCeilingPatch
{
    /// <summary>
    /// Recomputed per call rather than cached, so a resolution change between two frames
    /// cannot leave the prefix and the postfix disagreeing about the bias and permanently
    /// displace the target. Both halves read this, and nothing between them can change it.
    /// </summary>
    private static float Bias()
    {
        if (!ActualResolutionApi.Enabled || !Settings.PreserveMaxZoom || CameraFields.TargetZoom == null)
            return 0f;
        if (RenderTarget.ViewportSize is not { } viewport)
            return 0f;
        var bias = Geometry.MaxZoomBias(viewport, Settings.IntegerLimits);
        return float.IsFinite(bias) ? bias : 0f;
    }

    /// <summary>The bias the prefix applied, so the postfix undoes exactly that.</summary>
    private static float _applied;

    [HarmonyPrefix]
    internal static void Prefix()
    {
        _applied = Bias();
        if (_applied != 0f && CameraFields.TargetZoom != null)
            CameraFields.TargetZoom() -= _applied;
    }

    [HarmonyPostfix]
    internal static void Postfix()
    {
        if (_applied != 0f && CameraFields.TargetZoom != null)
            CameraFields.TargetZoom() += _applied;
        _applied = 0f;
    }
}
