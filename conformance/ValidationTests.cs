using Atomcraft.TestHarness;

namespace ActualResolutionConformance;

/// <summary>Generic well-formedness for this mod's own manifest and zip.</summary>
public static class ValidationTests
{
    [GameTest]
    public static void TheConformanceModIsWellFormed() => Validation.Check("ActualResolutionConformance");
}
