using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

namespace ActualResolution.Test;

/// <summary>
/// The language screen's selection outline, which is the one piece of the game's UI that
/// measures a distance in the layout and then adds it to a position in the frame.
///
/// <para>The mod scales the UI rather than laying it out against the frame, so those two are
/// no longer the same pixels and the outline drifts off the flag by the difference. See
/// <c>src/FlagOutline.cs</c> for the mechanism and <see cref="RetirementTests"/> for the
/// question of whether the game still needs the correction at all.</para>
///
/// <para><b>No display needed.</b> Laying out a container is arithmetic, and the assertion is
/// about where two nodes are rather than what was drawn, so this belongs in the everyday
/// headless run rather than with the screen tests.</para>
/// </summary>
public static class FlagOutlineTests
{
    /// <summary>The game's own offset, which the mod restates rather than replaces.</summary>
    private static readonly Vector2 LayoutOffset = new(-60f, -48f);

    /// <summary>
    /// The UI scale this test works at. Not a figure any window in the test run produces, so a
    /// correction that read anything other than the live node would come out wrong here.
    /// </summary>
    private const float Imposed = 1.25f;

    /// <summary>
    /// The outline sits exactly its offset away from a flag, whatever the UI is scaled by.
    ///
    /// <para><b>Against any flag rather than the selected one.</b> Which flag is highlighted
    /// is the game's business and nothing this mod touches; that the outline is the right
    /// distance from the one it chose is the whole of what the correction claims, and asking
    /// it this way needs no private state and no assumption about the machine's locale.</para>
    ///
    /// <para>The page has to be open for the grid to lay its children out, so this opens it
    /// and puts the previous page back. Both are free: <c>Gameplay</c>'s own
    /// <c>OnOpen</c> and <c>OnClose</c> are empty.</para>
    ///
    /// <para><b>A scale is imposed rather than taken from the run.</b> The harness runs in a
    /// window the size of the design frame, so the mod scales the UI by exactly 1 and the
    /// defect this covers cannot appear: every reading of the offset agrees, and the test
    /// would pass with the correction deleted. Setting a scale that no window here would
    /// produce is what makes it measure something, and it also checks the correction reads the
    /// live node rather than the mod's own idea of the scale.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator TheOutlineSitsOnAFlag()
    {
        if (Game.UI is not { } ui)
        {
            Harness.Inapplicable("the game has no UI node");
            yield break;
        }

        if (!ui.Pages.TryGetValue(PageId.LocalizationMenu, out var page)
            || page is not LocalizationMenu menu
            || menu.FlagGrid is not { } grid)
        {
            Harness.Inapplicable(
                "the game has no LocalizationMenu page with a FlagGrid, which is a bigger " +
                "change than this test is asking about; see src/FlagOutline.cs");
            yield break;
        }

        // The node names FlagGrid.Init itself uses, so a rename shows up here rather than as a
        // silently skipped assertion.
        if (grid.GetNodeOrNull<TextureRect>("FlagOutline") is not { } outline
            || grid.GetNodeOrNull<GridContainer>("GridContainer") is not { } container)
        {
            Harness.Inapplicable("FlagGrid's scene no longer has a FlagOutline and a GridContainer");
            yield break;
        }

        var previous = UI.CurrentPageId;
        var previousScale = ui.Scale;

        // Before the page opens, because opening it is what places the outline.
        ui.Scale = new Vector2(Imposed, Imposed);
        ui.SetUIPage(PageId.LocalizationMenu);

        // SetHighlightOnCurrentLocale waits two process frames, queues a sort, and waits for
        // it, so the outline is placed on the frame after that at the earliest.
        yield return Wait.Frames(6);

        var wanted = Geometry.LayoutDistance(LayoutOffset, Imposed);
        var flags = container.GetChildren().OfType<LocaleButton>().ToList();
        var nearest = float.PositiveInfinity;
        foreach (var flag in flags)
            nearest = MathF.Min(nearest, (outline.GlobalPosition - flag.GlobalPosition - wanted).Length());

        // Put both back before anything can throw, so a failure here costs one test rather than
        // leaving every test after it looking at a language screen at the wrong scale.
        ui.Scale = previousScale;
        ui.SetUIPage(previous);
        yield return Wait.Frames(1);

        if (flags.Count == 0)
            throw new AssertionException("the flag grid contains no LocaleButtons to sit on");

        if (nearest > 1f)
            throw new AssertionException(
                $"the outline is {nearest} pixels from where it belongs: no flag is at its " +
                $"offset of {wanted} (the game's {LayoutOffset} at a UI scale of {Imposed}). " +
                "An error of about (scale - 1) * (60, 48) means the correction in " +
                "src/FlagOutline.cs is not installed; see RegistrationTests.");
    }
}
