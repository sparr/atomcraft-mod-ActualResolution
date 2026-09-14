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
