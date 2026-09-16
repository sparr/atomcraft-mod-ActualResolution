using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using HarmonyLib;
using System.Collections;
using Session = Atomcraft.TestHarness.Session;

namespace ActualResolutionConformance;

/// <summary>
/// Is the layer that hides unexplored terrain mapped onto the frame the game is actually
/// drawing?
///
/// <para><b>This suite names no mod.</b> It states a property the game either has or does not,
/// and can be installed alongside any mod, alongside several, or alongside none. Nothing here
/// references ActualResolution, or names it, or depends on its assembly.</para>
///
/// <para><b>Why this property.</b> <c>Art/Shaders/Shadowmap.tres</c> finds its texel for a
/// screen pixel with</para>
/// <code>
/// vec2 screen_pos  = SCREEN_UV * viewport_size;
/// vec2 lightmap_uv = screen_pos / lightmap_size + lightmap_uv_offset;
/// </code>
/// <para>and of those three uniforms the game sets only the last two, every frame, from the
/// window it is drawing. <c>viewport_size</c> is never assigned from code — <c>Gameplay</c>
/// sets that uniform on the heatwave material and on nothing else — so it keeps whatever value
/// is saved in the material. That is the design 1600x900, and the shipped game always renders
/// into exactly that, so the two agree and nobody notices.</para>
///
/// <para>They stop agreeing the moment anything resizes the render target, and then the whole
/// shadow and fog layer is sampled at the wrong scale: the darkness that hides unexplored rock
/// underground, and the dark of space, land somewhere other than the terrain they belong to.
/// Nothing in the game's own arithmetic goes wrong, so there is no error in the log and no
/// exception; the only symptom is on screen.</para>
///
/// <para>Asserted without reading a single pixel, because the disagreement is between two
/// numbers. It also measures the <i>live viewport</i> rather than anything a mod told it,
/// which is what makes it an independent check on a mod that mirrors its own render target
/// into the shader: if the two ever part company, this says so.</para>
///
/// <para><b>Originally written in the Zoooom project</b>, which depends on this property and
/// does not affect it, as <c>ZoooomConformance.ShadowLayer</c>. It lives here as well because
/// this is the project whose mod resizes the render target, and a suite that never runs
/// beside the fix tests nothing about the fix.</para>
/// </summary>
public static class ShadowLayer
{
    /// <summary>
    /// The shadow layer's idea of the frame size is the frame size.
    ///
    /// <para>A property a mod that resizes the render target has to maintain, and which the
    /// game does not maintain for it, so a failure names the value found: that is the number
    /// whoever fixes it needs.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator TheShadowLayerIsMappedWithTheRealFrameSize()
    {
        yield return Session.Enter("flat");
        yield return Wait.Frames(2);

        var sprite = AccessTools.Field(typeof(Gameplay), "Shadowmap")?.GetValue(null) as Sprite2D;
        if (sprite?.Material is not ShaderMaterial material)
        {
            Harness.Inapplicable(
                sprite == null
                    ? "Gameplay no longer has a Shadowmap sprite to ask, so this property " +
                      "cannot be checked the way it was written"
                    : "the Shadowmap sprite carries no shader material");
            yield break;
        }

        var declared = material.GetShaderParameter("viewport_size");
        if (declared.VariantType != Variant.Type.Vector2)
        {
            Harness.Inapplicable(
                $"the shadow shader's viewport_size is a {declared.VariantType} rather than a " +
                "Vector2, so either the shader has been rewritten or the material no longer " +
                "carries a value for it and the shader's own default is in force, which this " +
                "test cannot read");
            yield break;
        }

        var declaredSize = declared.AsVector2();
        var actual = Game.CanvasLayer.GetViewport().GetVisibleRect().Size;

        if (declaredSize.X <= 0f || declaredSize.Y <= 0f)
            throw new AssertionException(
                $"the shadow shader is mapping the frame as {declaredSize}, which is not a frame " +
                $"at all, while the game draws into {actual}");

        if (Mathf.Abs(declaredSize.X - actual.X) > 1f || Mathf.Abs(declaredSize.Y - actual.Y) > 1f)
            throw new AssertionException(
                $"the shadow and fog layer is mapped as though the frame were {declaredSize}, " +
                $"while the game is drawing into {actual}: a factor of " +
                $"{actual.X / declaredSize.X:0.###} across and " +
                $"{actual.Y / declaredSize.Y:0.###} down. Everything that layer hides -- " +
                "unexplored rock underground, the dark of space -- is drawn that far from the " +
                "terrain it belongs to. The game sets this shader's lightmap_size and " +
                "lightmap_uv_offset every frame but never its viewport_size, so whatever resized " +
                "the render target has to set it too.");

        yield return Session.Leave();
    }
}
