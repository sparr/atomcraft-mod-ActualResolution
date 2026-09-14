using Atomcraft.TestHarness;

namespace ActualResolution.Test;

/// <summary>
/// Entry point for the test mod.
///
/// <para>A peer of <c>ActualResolution</c> rather than a module of it, because the mod loader
/// treats a missing dependency as an error: a test module shipped inside the mod's own zip
/// would show a red entry in the loader report for every player who did not also install the
/// harness.</para>
///
/// <para>Nothing to install: the harness discovers <c>[GameTest]</c> methods in every loaded
/// mod assembly by itself. The version pin is the whole job, and it has to be here rather than
/// in a test, because the loader's dependencies carry no version constraint and a harness whose
/// API has moved would otherwise surface as a MissingMethodException from an unrelated
/// place.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "ActualResolution.Test";

    /// <summary>The harness this mod is written against. Its 0.x API changes between minors.</summary>
    public const string HarnessVersion = "0.4";

    public static void Initialize() => Harness.RequireVersion(HarnessVersion);
}
