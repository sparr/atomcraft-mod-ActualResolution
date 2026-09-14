using Atomcraft.TestHarness;

namespace ActualResolution.Test;

/// <summary>
/// The reset this mod registers with the harness puts the mod back, not just its settings.
///
/// <para>Its own file because it is about the test machinery rather than about the mod's
/// behaviour, and because the thing it guards is easy to widen by accident: the day somebody
/// makes <c>Settings.Reset</c> cover more, this test should say so rather than keep passing.</para>
/// </summary>
public static class StateResetTests
{
    /// <summary>
    /// The state registration puts the mod back on, not just its settings.
    ///
    /// <para>The registered reset runs between tests, and the state a test is most likely to
    /// leave behind is the state it did not change on purpose: a latched fault, or the off
    /// switch flipped by a test that threw before its <c>finally</c>. Neither is reachable
    /// from <c>Settings.Reset</c> — <c>Enabled</c> is the conjunction of a setting and a
    /// field, so restoring the setting leaves a false field false — and a mod left switched
    /// off does not fail the tests after it. It passes the ones that assert the game behaves
    /// as it does unmodded, which is worse.</para>
    ///
    /// <para>The first assertion is a premise check: if <c>Settings.Reset</c> ever does
    /// restore the mod on its own, this test is measuring nothing and says so rather than
    /// passing quietly.</para>
    /// </summary>
    [GameTest]
    public static void ResettingTheStateRestoresTheModAndNotOnlyItsSettings()
    {
        ActualResolutionApi.Enabled = false;

        Settings.Reset();
        if (ActualResolutionApi.Enabled)
            throw new AssertionException(
                "Settings.Reset restored the mod by itself, so this test no longer covers the " +
                "gap it was written for. Check what ResetState is still needed for.");

        ActualResolutionApi.ResetState();
        if (!ActualResolutionApi.Enabled)
            throw new AssertionException(
                "ResetState left the mod switched off. Every test after this one would run " +
                "against an unmodded game, and the ones asserting unmodded behaviour would pass.");
        if (ActualResolutionApi.Faulted)
            throw new AssertionException("ResetState left a fault latched");
    }
}
