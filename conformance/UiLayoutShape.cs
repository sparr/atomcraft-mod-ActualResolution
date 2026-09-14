using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

namespace ActualResolutionConformance;

/// <summary>
/// Is the game's UI still a fixed 1600x900 box that something else has to scale?
///
/// <para><b>This suite names no mod.</b> It states a property the game either has or does not,
/// and can be installed alongside any mod, alongside several, or alongside none. Nothing here
/// references ActualResolution, or names it, or depends on its assembly.</para>
///
/// <para><b>Why this property, and why it is conformance rather than retirement.</b> A mod that
/// renders at the window's resolution does not <i>fix</i> this; it <i>depends</i> on it. The
/// game lays its UI out in one Control of exactly the design size with every anchor at zero,
/// and relies on the whole frame being rescaled to the window to make that fill the screen. A
/// mod that takes the rescale away has to apply it to that Control instead.</para>
///
/// <para>So if the game ever anchored its UI to the viewport properly, this test fails and the
/// news is bad rather than good: the compensating scale would then be applied on top of a
/// layout that no longer needs it, and the HUD would be wrong in a way no arithmetic in the mod
/// could detect. That is the opposite of a retirement test, which is why it lives here.</para>
///
/// <para>Measured as the Control's <i>size</i> and anchors, not its scale: a mod compensating
/// for this scales the Control and leaves both of those alone, so the property reads the same
/// whether one is installed or not.</para>
/// </summary>
public static class UiLayoutShape
{
    [GameTest]
    public static void TheUiIsOneFixedSizeControlWithTopLeftAnchors()
    {
        var ui = Game.UI
            ?? throw new AssertionException("the game has no UI node; has the scene changed?");

        var design = new Vector2(1600f, 900f);
        if (!ui.Size.IsEqualApprox(design))
            throw new AssertionException(
                $"the UI Control is {ui.Size.X}x{ui.Size.Y}, not {design.X}x{design.Y}. Either " +
                "the design size moved, in which case anything scaling this layout is scaling " +
                "it by the wrong factor, or the UI is now sized to the frame and needs no " +
                "scaling at all.");

        foreach (var (name, anchor) in new[]
                 {
                     ("left", ui.AnchorLeft), ("top", ui.AnchorTop),
                     ("right", ui.AnchorRight), ("bottom", ui.AnchorBottom),
                 })
            if (!Mathf.IsZeroApprox(anchor))
                throw new AssertionException(
                    $"the UI Control's {name} anchor is {anchor}, not 0. The layout now follows " +
                    "the viewport, so a mod scaling it to compensate for a rescaled frame would " +
                    "be scaling a layout that has already adapted.");
    }
}
