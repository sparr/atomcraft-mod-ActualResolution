using Atomcraft.TestHarness;

namespace ActualResolution.Test;

/// <summary>
/// Entry point for the test mod.
///
/// <para>A peer of <c>ActualResolution</c> rather than a module of it, because the mod loader treats a
/// missing dependency as an error: a test module shipped inside the mod's own zip would show a
/// red entry in the loader report for every player who did not also install the harness.</para>
///
/// <para>Nothing to install: the harness discovers <c>[GameTest]</c> methods in every loaded mod
/// assembly by itself. The version pin is the whole job, and it has to be here rather than in a
/// test, because the loader's dependencies carry no version constraint and a harness whose API
/// has moved would otherwise surface as a MissingMethodException from an unrelated place.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "ActualResolution.Test";

    /// <summary>The harness this mod is written against. Its 0.x API changes between minors.</summary>
    public const string HarnessVersion = "0.4";

    public static void Initialize()
    {
        Harness.RequireVersion(HarnessVersion);

        // Configuration is state, and it is the kind no rectangle describes. Registering it
        // means a test that changes a knob and then throws cannot quietly change the meaning of
        // every test after it. No checksum: these are settings a test sets deliberately, not
        // something the simulation writes to.
        //
        // The reset covers the mod's runtime state as well as its settings, and that half is
        // the half that bites. A fault latch is set once and never cleared, so one throw
        // disables the mod for the rest of the run -- and a test asserting something true of
        // the unmodded game still passes with the mod dead, which is a green run that measured
        // nothing. The mod template clears its latch on Simulation.Init and Simulation.Reset;
        // these mods never touch the simulation and have no such hook, so it belongs here.
        StateRegistry.Register(new StateSpec
        {
            Name = "actualresolution",
            OnReset = ActualResolutionApi.ResetState,
            OnDescribe = ActualResolutionApi.DescribeState,
        });
    }
}
