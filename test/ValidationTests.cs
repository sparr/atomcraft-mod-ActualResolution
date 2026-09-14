using Atomcraft.TestHarness;

namespace ActualResolution.Test;

/// <summary>
/// Generic well-formedness, from the harness.
///
/// <para>Two lines, and they catch what produces no error at load and no crash, just a mod
/// that quietly does less than it says: a manifest data path matching nothing in the zip, a
/// renamed <c>initClass</c>, a dependency on a module id that no longer exists. Checks read
/// the mod's own zip rather than the live registries, so they also work on a mod that fails
/// to load, which is when they are worth the most.</para>
/// </summary>
public static class ValidationTests
{
    [GameTest]
    public static void TheModIsWellFormed() => Validation.Check("ActualResolution");

    [GameTest]
    public static void TheTestModIsWellFormed() => Validation.Check("ActualResolution.Test");
}
