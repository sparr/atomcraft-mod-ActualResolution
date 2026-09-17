using Atomcraft;
using Atomcraft.TestHarness;
using HarmonyLib;

namespace ActualResolution.Test;

/// <summary>
/// The mod is actually wired into the game.
///
/// <para><b>These fail first and loudest when a Harmony patch has gone missing</b>, which makes
/// every other failure in the suite easier to read: a mod whose hooks never installed fails its
/// behaviour tests for a reason that has nothing to do with its behaviour, and this one names
/// the real cause.</para>
///
/// <para>Worth knowing while reading a failure here: a postfix does not run when the original
/// method throws. <c>GetPatchedMethods</c> lists the method, the method demonstrably executes,
/// and the postfix silently never fires. If a hook seems not to be installed, check
/// <c>godot.log</c> for an exception inside the target before suspecting Harmony.</para>
/// </summary>
public static class RegistrationTests
{
    /// <summary>Every method this mod means to patch is patched.</summary>
    [GameTest]
    public static void TheHooksAreInstalled()
    {
        var patched = Harmony.GetAllPatchedMethods().ToList();

        foreach (var (type, name) in new (Type, string)[]
        {
            (typeof(Game), "_Ready"),
            (typeof(SaveData_Device), "ApplySettings"),
            (typeof(Gameplay), "ResizeDisplayTextures"),
            (typeof(FollowCam), "RecalculateMinZoom"),
            (typeof(FollowCam), "IncreaseZoom"),
        })
        {
            var target = AccessTools.Method(type, name)
                         ?? (System.Reflection.MethodBase?)AccessTools.PropertyGetter(type, name);
            if (target == null)
                throw new AssertionException(
                    $"the game no longer has {type.Name}.{name}; the mod needs an update");
            if (!patched.Contains(target))
                throw new AssertionException(
                    $"{type.Name}.{name} is not patched. ModEntry.Initialize may have " +
                    "returned early, or the game renamed what the patch matches on.");
        }
    }

    /// <summary>
    /// The language screen's outline correction is installed, which is two claims rather than
    /// one: Harmony bound the state machine hiding behind a private <c>async void</c> method,
    /// and the transpiler found the offset it edits. A game update can break either alone.
    ///
    /// <para>Not part of <see cref="TheHooksAreInstalled"/> because its target is not a method
    /// the game declares: it is the compiler-generated <c>MoveNext</c> of
    /// <c>FlagGrid.SetHighlightOnCurrentLocale</c>'s state machine, reached the way Harmony
    /// reaches it rather than by the mangled type name.</para>
    /// </summary>
    [GameTest]
    public static void TheFlagOutlineCorrectionIsInstalled()
    {
        var method = AccessTools.Method(typeof(FlagGrid), "SetHighlightOnCurrentLocale")
                     ?? throw new AssertionException(
                         "the game no longer has FlagGrid.SetHighlightOnCurrentLocale; the mod " +
                         "needs an update, and possibly no longer needs src/FlagOutline.cs at all");

        var moveNext = AccessTools.AsyncMoveNext(method)
                       ?? throw new AssertionException(
                           "FlagGrid.SetHighlightOnCurrentLocale is no longer async, so there is " +
                           "no state machine to patch. What src/FlagOutline.cs inserts has to " +
                           "move to wherever the assignment lives now.");

        if (!Harmony.GetAllPatchedMethods().Contains(moveNext))
            throw new AssertionException(
                $"{moveNext.DeclaringType?.Name}.MoveNext is not patched. ModEntry.Initialize may " +
                "have returned early, or Harmony resolved a different state machine.");

        if (!ActualResolutionApi.FlagOutlineCorrected)
            throw new AssertionException(
                "the transpiler ran and matched nothing: FlagGrid no longer builds its " +
                "(-60, -48) outline offset where the mod edits it. The language screen's " +
                "outline is being drawn off the flag whenever the UI is scaled.");
    }

    /// <summary>
    /// The patches are this mod's and not somebody else's, which is what the Harmony instance
    /// id is for and what makes an unpatch targeted rather than indiscriminate.
    /// </summary>
    [GameTest]
    public static void ThePatchesAreOurs()
    {
        var target = AccessTools.Method(typeof(FollowCam), nameof(FollowCam.IncreaseZoom))
                     ?? throw new AssertionException("the game no longer has FollowCam.IncreaseZoom");

        var info = Harmony.GetPatchInfo(target)
                   ?? throw new AssertionException("FollowCam.IncreaseZoom carries no patches at all");

        var owners = info.Owners;
        if (!owners.Contains(ModEntryOwner))
            throw new AssertionException(
                $"FollowCam.IncreaseZoom is patched by {string.Join(", ", owners)}, none of " +
                $"which is '{ModEntryOwner}'. Another mod is doing this, or the id changed.");
    }

    /// <summary>The Harmony id the mod registers under, which is its mod id.</summary>
    private const string ModEntryOwner = "ActualResolution";
}
