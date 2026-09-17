using System.Reflection.Emit;
using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// Puts the language screen's selection outline back on the flag it belongs to.
///
/// <para><b>The problem.</b> <c>FlagGrid.SetHighlightOnCurrentLocale</c> places the outline
/// with</para>
/// <code>
/// Vector2 globalPosition = value.GlobalPosition + new Vector2(-60f, -48f);
/// FlagOutline.GlobalPosition = globalPosition;
/// </code>
/// <para>where the constant is a measurement of the 1600x900 layout and the position it is
/// added to is in the frame's coordinates. Godot converts an assigned <c>GlobalPosition</c>
/// back through the parent chain, so with a scale of <i>s</i> on an ancestor the offset
/// arrives as <c>(-60, -48) / s</c> in the layout, and the outline sits
/// <c>(s - 1) * (60, 48)</c> away from the flag: about 12 across and 10 down at 1080p, and
/// the other way at a window whose divisor is 2.</para>
///
/// <para><b>Whose defect this is.</b> The game's, but it cannot be observed in the shipped
/// game: the UI is drawn 1:1 into the fixed 1600x900 render target and the engine rescales the
/// finished frame, so layout pixels and frame pixels are the same pixels and the two readings
/// of that constant agree. <see cref="UiLayout"/> is what separates them, by taking the
/// rescale away from the frame and applying it to the UI Control instead. So this mod does not
/// introduce the bug and does not inherit a working feature either: it makes a latent one
/// visible, and this file is what it owes for that. The one-line fix belongs upstream; see
/// <c>test/RetirementTests.cs</c> for how we will know it has landed.</para>
///
/// <para><b>Why a transpiler.</b> The assignment sits after three awaits inside an
/// <c>async void</c>, so there is no boundary to postfix: by the time any hook on the visible
/// method returns, nothing has been placed yet. A postfix on the public <c>FlagGrid.Process</c>
/// could re-place the outline every frame instead, but it would have to rediscover which flag
/// is selected through private state, and it would be doing it sixty times a second to correct
/// something that changes when the player clicks a flag. One instruction at the point the game
/// builds the offset is both smaller and better behaved.</para>
/// </summary>
[HarmonyPatch(typeof(FlagGrid), Method, MethodType.Async)]
internal static class FlagOutlinePatch
{
    /// <summary>The game method whose state machine carries the assignment. Private, so it is
    /// spelled rather than <c>nameof</c>'d, and checked at startup by <see cref="Applied"/>.</summary>
    internal const string Method = "SetHighlightOnCurrentLocale";

    /// <summary>The offset the game adds, in the layout's pixels.</summary>
    private const float OffsetX = -60f;

    /// <inheritdoc cref="OffsetX"/>
    private const float OffsetY = -48f;

    /// <summary>
    /// Whether the transpiler found what it was looking for and inserted the correction.
    ///
    /// <para>Set while the patch is applied, so it answers for the whole session from
    /// <c>Initialize</c> onward. False means the game moved that constant and the outline is
    /// being drawn wherever the game puts it, which is a cosmetic defect on one screen rather
    /// than anything worth failing over — hence a log line at startup and a test, not a
    /// throw.</para>
    /// </summary>
    internal static bool Applied { get; private set; }

    /// <summary>
    /// Inserts one call between the game building its offset and the game adding it.
    ///
    /// <para>The site is unmistakable and the edit is the smallest one available: a
    /// <c>Vector2</c> is on the stack alone, and a <c>Vector2</c> is what the next instruction
    /// wants, so there are no locals, no labels and no branches involved. Inserting after the
    /// constructor rather than replacing the two constants is what keeps it that way — the
    /// instruction before the match is a branch target, and the one we insert at is not.</para>
    ///
    /// <para>A miss returns the method untouched. A transpiler that throws takes the game's
    /// method with it, and this one is correcting a highlight.</para>
    /// </summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var ctor = AccessTools.Constructor(typeof(Vector2), new[] { typeof(float), typeof(float) });
        var correction = AccessTools.Method(typeof(FlagOutlinePatch), nameof(ToFrame));
        if (ctor == null || correction == null)
            return instructions;

        var matcher = new CodeMatcher(instructions).MatchStartForward(
            new CodeMatch(code => code.LoadsConstant(OffsetX)),
            new CodeMatch(code => code.LoadsConstant(OffsetY)),
            new CodeMatch(OpCodes.Newobj, ctor));

        if (matcher.IsInvalid)
            return instructions;

        Applied = true;
        return matcher.Advance(3)
                      .Insert(new CodeInstruction(OpCodes.Call, correction))
                      .InstructionEnumeration();
    }

    /// <summary>
    /// The game's layout-pixel offset, in the frame it is about to be added to a position in.
    ///
    /// <para><b>Reads the live node rather than this mod's own bookkeeping.</b> The factor
    /// that has to be undone is whatever scale is actually on the UI, which is
    /// <see cref="UiLayout"/>'s while the mod is on, exactly 1 while it is off (the restore
    /// puts it back), and someone else's if another mod scales the same Control. All three
    /// come out right for free, and there is no switch to get out of step with.</para>
    ///
    /// <para><b>Cannot throw.</b> The caller is an <c>async void</c> state machine, so an
    /// exception here is handed to <c>AsyncVoidMethodBuilder.SetException</c> and rethrown on
    /// the synchronization context as an unhandled one. The offset the game chose is always an
    /// acceptable answer, so anything unexpected returns it.</para>
    /// </summary>
    internal static Vector2 ToFrame(Vector2 layoutOffset)
    {
        try
        {
            return Geometry.LayoutDistance(layoutOffset, Game.UI?.Scale.X ?? 1f);
        }
        catch
        {
            return layoutOffset;
        }
    }
}
