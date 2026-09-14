using System.Reflection;
using HarmonyLib;

namespace ActualResolution;

/// <summary>
/// Entry point, as named by <c>mod.json</c>.
///
/// <para><b>Initialize runs before the game has initialized anything.</b> The mod loader
/// loads every mod during <c>SceneTree._initialize()</c>, and <c>Game._Ready</c> does not run
/// until the following frame, so there is no window, no viewport and no camera to look at
/// here. The only correct thing to do is install patches and return.</para>
///
/// <para>Nothing is patched yet. This commit is the project that loads, so that everything
/// after it is a change to a mod the loader already accepts rather than a change to a build
/// that has never run.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "ActualResolution";

    private static Harmony? _harmony;

    public static void Initialize()
    {
        _harmony = new Harmony(ModId);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log.Info($"initialized, {_harmony.GetPatchedMethods().Count()} method(s) patched");
    }
}
