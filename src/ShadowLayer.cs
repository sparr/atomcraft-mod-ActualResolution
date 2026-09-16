using System.Reflection;
using Atomcraft;
using Godot;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// Tells the shadow and fog shader how big the frame is, because the game never does.
///
/// <para><b>The problem.</b> <c>Art/Shaders/Shadowmap.tres</c> finds the texel of the
/// light-and-fog texture that belongs to a screen pixel with</para>
/// <code>
/// vec2 screen_pos  = SCREEN_UV * viewport_size;
/// vec2 lightmap_uv = screen_pos / lightmap_size + lightmap_uv_offset;
/// </code>
/// <para>and of those three uniforms <c>Gameplay.RefreshShaderParameters</c> sets only the
/// last two, every frame, from the window it is drawing. <c>viewport_size</c> is never
/// assigned from code at all — the one assignment of that name in the whole assembly is
/// <c>Gameplay.UpdateHeatwave</c> setting it on the <i>heatwave</i> material — so the shadow
/// layer uses whatever value is saved in the material, which is the design 1600x900.</para>
///
/// <para><b>Why the shipped game is fine and this mod is not.</b> Shipped, every frame is
/// drawn into a render target of exactly 1600x900, so the baked value is always right. This
/// mod's whole purpose is to stop that, and from then on the shader is wrong by the ratio
/// between the two sizes. Since <c>screen_pos</c> feeds <c>lightmap_uv</c> directly, the
/// entire shadow and fog layer is sampled at the wrong scale, anchored at the top-left corner
/// and drifting from the terrain toward the far corner: the darkness that hides unexplored
/// rock underground, and the dark of space, land somewhere other than the terrain they belong
/// to. Nothing in the game's own arithmetic goes wrong, so there is no error in the log and no
/// exception. The only symptom is on screen.</para>
///
/// <para><b>Why it mirrors <c>ContentScaleSize</c> rather than measuring the viewport.</b>
/// Under <c>ContentScaleModeEnum.Viewport</c>, which is what the game ships and what
/// <see cref="RenderTarget"/> keeps, the root window's content scale size <i>is</i> the render
/// target, and it is the value this mod just assigned rather than one the engine may update a
/// frame later. Reading the live viewport instead would be measuring this mod's own effect
/// back. The conformance suite makes the independent check, against
/// <c>GetViewport().GetVisibleRect()</c>, so the day those two disagree a test says so.</para>
///
/// <para><b>This is a game defect, not a design.</b> The game could set this uniform beside
/// the two it already sets, in one line, and then this file would have nothing to do. See
/// <c>test/RetirementTests.cs</c>.</para>
/// </summary>
public static class ShadowLayer
{
    /// <summary>The shader uniform, spelled once.</summary>
    private const string Uniform = "viewport_size";

    /// <summary>
    /// <c>Gameplay.Shadowmap</c>, the sprite the light-and-fog texture is drawn on.
    ///
    /// <para><b>The sprite rather than <c>Gameplay.ShadowmapMaterial</c>,</b> which is right
    /// beside it and looks like the more direct route. That field is null until
    /// <c>Gameplay.ResizeDisplayTextures</c> has run, while the sprite carries its material
    /// from the moment the scene is instanced, so going through the sprite is the one that
    /// cannot be too early. The game only ever re-reads <c>Shadowmap.Material</c> into its own
    /// field; it never replaces it.</para>
    ///
    /// <para>Resolved once, and null if the game renames it, which
    /// <see cref="ModEntry.Initialize"/> reports as a sentence at startup. The alternative is
    /// <c>GetNodeOrNull("/root/Game/World/Shadowmap")</c>, which is a string of the same
    /// fragility with no place to check it.</para>
    /// </summary>
    private static readonly FieldInfo? SpriteField = AccessTools.Field(typeof(Gameplay), "Shadowmap");

    /// <summary>Whether the sprite this works through is still where it was.</summary>
    internal static bool Bound => SpriteField != null;

    /// <summary>
    /// The frame size the shadow shader is currently mapping with, or null when there is no
    /// material to ask or it carries no such uniform.
    ///
    /// <para>Read from the material rather than remembered, so it answers for what the shader
    /// will actually do rather than for what this mod last tried to make it do.</para>
    /// </summary>
    public static Vector2? DeclaredFrame
    {
        get
        {
            if (Material is not { } material)
                return null;
            var declared = material.GetShaderParameter(Uniform);
            return declared.VariantType == Variant.Type.Vector2 ? declared.AsVector2() : null;
        }
    }

    /// <summary>The shadow sprite's shader material, or null before the game has a scene.</summary>
    private static ShaderMaterial? Material =>
        SpriteField?.GetValue(null) is Sprite2D sprite ? sprite.Material as ShaderMaterial : null;

    /// <summary>
    /// Makes the shader's idea of the frame size equal the render target's size, whatever
    /// that currently is.
    ///
    /// <para>Deliberately does not consult <see cref="ActualResolutionApi.Enabled"/>: this
    /// mirrors the render target rather than implementing a policy about it, so it is correct
    /// both after <see cref="RenderTarget.SyncIfChanged"/> has resized the target and after
    /// <see cref="RenderTarget.Restore"/> has handed it back. Idempotent and cheap enough to
    /// call from any of the events that could have changed either side.</para>
    /// </summary>
    public static void Sync()
    {
        if (Material is not { } material)
            return;

        var frame = (Vector2)RenderTarget.ContentSize;
        if (frame.X <= 0f || frame.Y <= 0f)
            return;

        var declared = material.GetShaderParameter(Uniform);
        if (declared.VariantType == Variant.Type.Vector2 && declared.AsVector2().IsEqualApprox(frame))
            return;

        material.SetShaderParameter(Uniform, frame);
        Log.Info($"shadow layer mapped to {frame.X}x{frame.Y}");
    }
}

/// <summary>
/// The game rebinding the shadow material, which is the one moment this mod could be left
/// holding a stale one.
///
/// <para><c>Gameplay.ResizeDisplayTextures</c> rebuilds every display texture and re-reads
/// <c>Shadowmap.Material</c> into <c>Gameplay</c>'s own field. It runs from
/// <c>Gameplay.Init</c> at startup and from <c>SaveData_Device.ApplySettings</c> on every
/// change to the simulated area. Today it keeps the same <c>ShaderMaterial</c> instance, so
/// the uniform survives and this postfix has nothing to do; it is here for the case it stops
/// doing that, and for a re-instanced scene handing out a fresh material from the
/// <c>.tres</c>. Cheap insurance on a path that runs a handful of times a session, not a
/// per-frame hook.</para>
///
/// <para>Routed through <see cref="WindowWatcher.Sync"/> rather than calling
/// <see cref="ShadowLayer.Sync"/> directly, so it inherits the same fault latch as every other
/// event that sizes the frame, and so a mod that is switched off does not quietly start
/// writing shader parameters again.</para>
/// </summary>
[HarmonyPatch(typeof(Gameplay), nameof(Gameplay.ResizeDisplayTextures))]
internal static class ShadowLayerRebindPatch
{
    [HarmonyPostfix]
    internal static void Postfix() => WindowWatcher.Sync();
}
