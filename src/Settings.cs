using Godot;
using FileAccess = Godot.FileAccess;

namespace ActualResolution;

/// <summary>
/// What the player can change, and the one file they change it in.
///
/// <para>The file is <c>user://ActualResolution.json</c>, beside the game's own
/// <c>DeviceSettings.json</c>, and it is written with the defaults the first time the mod
/// runs so that the options are discoverable without reading the README. It is read once, at
/// <c>Initialize</c>; nothing here is reread while the game runs.</para>
///
/// <para>Parsed with Godot's own <see cref="Json"/> rather than Newtonsoft. Newtonsoft is
/// loaded and would do, but a mod that reaches for the game's copy of a library takes on that
/// version; a handful of scalars do not need it.</para>
/// </summary>
public static class Settings
{
    /// <summary>Where the settings file lives, in Godot's user data directory.</summary>
    public const string Path = "user://ActualResolution.json";

    /// <summary>
    /// Master switch. Off, every patch falls straight through and the render target is
    /// handed back to the engine, so the mod can be turned off without uninstalling it.
    /// Also what a test toggles; see <see cref="ActualResolutionApi.Enabled"/>.
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// How many window pixels one render-target pixel becomes. Zero picks
    /// <see cref="Geometry.AutoDivisor"/>: 1 up to 1080p, 2 from 1440p.
    ///
    /// <para>1 renders at the window's own resolution, which is the sharpest the game can be,
    /// at the cost of about six times the fragments at 4K. 2 renders a smaller frame and
    /// doubles it on the way out, which is exact and much cheaper.</para>
    /// </summary>
    public static int RenderDivisor;

    /// <summary>
    /// Whether the zoom-in limit is scaled to the render target
    /// (<see cref="Geometry.MaxZoom"/>) so the closest view keeps the apparent size it has in
    /// the shipped game. Off, the game's flat 1.5 stands, which on a large screen is further
    /// out than the game shipped.
    /// </summary>
    public static bool PreserveMaxZoom = true;

    /// <summary>
    /// Whether the camera's two zoom limits are put on a whole number of pixels per cell.
    ///
    /// <para>The game comes to rest on those two limits and, reliably, nowhere else: the
    /// widest view is where a session starts and where zooming out stops, the closest view is
    /// where zooming in stops, and everywhere in between is wherever the player happened to
    /// let go of the key. So rounding just those two is most of what it takes for the view to
    /// be exact without any further help — and it costs at most half a pixel per cell of the
    /// framing the shipped game would have given.</para>
    ///
    /// <para>It does not make every zoom whole; only the ends. The IntegerZoom mod is what
    /// does the rest, and it composes with this: quantizing an already-whole limit is a
    /// no-op.</para>
    /// </summary>
    public static bool IntegerLimits = true;

    /// <summary>
    /// What the game's fixed 1600x900 UI layout is scaled by. Zero reproduces the scale the
    /// engine's blit used to apply, so the UI looks exactly as it does in the shipped game.
    ///
    /// <para>1.0 is the other interesting value: the UI is then drawn at the frame's own
    /// resolution, so its pixels are square and sharp, at the price of a HUD that gets smaller
    /// as the screen gets bigger. See <see cref="UiLayout"/>.</para>
    /// </summary>
    public static float UiScale;

    /// <summary>Whether <see cref="Load"/> has run, so it does not run twice.</summary>
    private static bool _loaded;

    /// <summary>
    /// Reads the settings file, writing it with the defaults first if it is not there.
    ///
    /// Never throws: a settings file that cannot be read or does not parse leaves the
    /// defaults in place and says so in the log. Refusing to start over a stray comma would
    /// be a worse outcome than ignoring it.
    /// </summary>
    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            if (!FileAccess.FileExists(Path))
            {
                Save();
                return;
            }

            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Log.Warn($"could not open {Path} ({FileAccess.GetOpenError()}); using defaults");
                return;
            }

            var parsed = Json.ParseString(file.GetAsText());
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                Log.Warn($"{Path} is not a JSON object; using defaults");
                return;
            }

            var settings = parsed.AsGodotDictionary();
            Enabled = Bool(settings, "enabled", Enabled);
            RenderDivisor = Math.Max(0, Int(settings, "renderDivisor", RenderDivisor));
            PreserveMaxZoom = Bool(settings, "preserveMaxZoom", PreserveMaxZoom);
            IntegerLimits = Bool(settings, "integerLimits", IntegerLimits);
            UiScale = MathF.Max(0f, Float(settings, "uiScale", UiScale));

            Log.Info($"settings: {Describe()}");
        }
        catch (Exception e)
        {
            Log.Warn($"could not read {Path}; using defaults: {e.Message}");
        }
    }

    /// <summary>
    /// The current values on one line. Used in the startup log, and handed to the harness's
    /// <c>StateRegistry</c> so a test failure report says what the knobs were set to.
    /// </summary>
    public static string Describe() =>
        $"enabled={Enabled} " +
        $"renderDivisor={(RenderDivisor == 0 ? "auto" : RenderDivisor.ToString())} " +
        $"preserveMaxZoom={PreserveMaxZoom} integerLimits={IntegerLimits} " +
        $"uiScale={(UiScale == 0f ? "auto" : UiScale.ToString(System.Globalization.CultureInfo.InvariantCulture))}";

    /// <summary>
    /// Restores every setting to its default, without touching the file. Used between tests,
    /// via the <c>StateSpec</c> the test mod registers.
    ///
    /// Written out longhand rather than by re-reading the file, because a test must not depend
    /// on what happens to be on the developer's disk. A field added above and not added here
    /// leaks one test's configuration into every test after it.
    /// </summary>
    public static void Reset()
    {
        Enabled = true;
        RenderDivisor = 0;
        PreserveMaxZoom = true;
        IntegerLimits = true;
        UiScale = 0f;
    }

    /// <summary>Writes the current values, which on a first run are the defaults.</summary>
    private static void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Log.Warn($"could not write {Path} ({FileAccess.GetOpenError()})");
            return;
        }

        // Hand-written rather than serialized, because the comments are the point: this file
        // is the only documentation a player who never finds the README will see.
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        file.StoreString(
            "{\n" +
            "    \"_\": \"Settings for the Actual Resolution mod. Delete this file to restore defaults.\",\n" +
            "    \"_enabled\": \"false turns the whole mod off without uninstalling it.\",\n" +
            $"    \"enabled\": {(Enabled ? "true" : "false")},\n" +
            "    \"_renderDivisor\": \"Window pixels per rendered pixel. 0 picks one below 1440p and two above. 1 is sharpest; 2 is much cheaper on a 4K screen.\",\n" +
            $"    \"renderDivisor\": {RenderDivisor},\n" +
            "    \"_preserveMaxZoom\": \"true scales the zoom-in limit with the window so the closest view looks as it did before the mod.\",\n" +
            $"    \"preserveMaxZoom\": {(PreserveMaxZoom ? "true" : "false")},\n" +
            "    \"_integerLimits\": \"true puts the widest and closest views on a whole number of pixels per cell. Those are the two zooms the game comes to rest on.\",\n" +
            $"    \"integerLimits\": {(IntegerLimits ? "true" : "false")},\n" +
            "    \"_uiScale\": \"What the game's fixed 1600x900 UI layout is scaled by. 0 keeps it the size the game shipped. 1 draws it sharp at the screen's own resolution, which is smaller.\",\n" +
            $"    \"uiScale\": {UiScale.ToString(invariant)}\n" +
            "}\n");
        Log.Info($"wrote default settings to {Path}");
    }

    private static bool Bool(Godot.Collections.Dictionary settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) ? value.AsBool() : fallback;

    private static int Int(Godot.Collections.Dictionary settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) ? (int)value.AsDouble() : fallback;

    private static float Float(Godot.Collections.Dictionary settings, string key, float fallback) =>
        settings.TryGetValue(key, out var value) ? (float)value.AsDouble() : fallback;
}
