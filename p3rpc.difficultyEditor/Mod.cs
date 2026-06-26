using p3rpc.difficultyEditor.Configuration;
using p3rpc.difficultyEditor.Template;
using Reloaded.Mod.Interfaces;
using System.Drawing;
using UnrealEssentials.Interfaces;

namespace p3rpc.difficultyEditor;

/// <summary>
/// Reads the configured per-difficulty multipliers, patches a copy of the game's
/// DT_BtlDIfficultyParam data table, and feeds it to Unreal Essentials so the game
/// loads our values instead of the stock ones. No game hooks required.
/// (Presets are resolved in the config window inside the launcher; here we just read the baked values.)
/// </summary>
public class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private Config _configuration;

    private static readonly string[] Rows = Presets.Rows;
    private static readonly string[] Fields = Presets.Fields;

    // Byte offset of each float value inside the (zen-format) DT_BtlDIfficultyParam.uasset.
    // Indexed [field][row]. Unreal Essentials wants zen/IoStore-format loose files, not legacy.
    private static readonly int[][] Offsets =
    {
        new[] { 1523, 1829, 2135, 2441, 2747 }, // DamageDealt      (DamageRateToPlayer)
        new[] { 1610, 1916, 2222, 2528, 2834 }, // DamageDealtWeak  (DamageRateToPlayerWeak)
        new[] { 1668, 1974, 2280, 2586, 2892 }, // DamageDealtCrit  (DamageRateToPlayerCritical)
        new[] { 1494, 1800, 2106, 2412, 2718 }, // DamageTaken      (DamageRateToEnemy)
        new[] { 1581, 1887, 2193, 2499, 2805 }, // DamageTakenWeak  (DamageRateToEnemyWeak)
        new[] { 1639, 1945, 2251, 2557, 2863 }, // DamageTakenCrit  (DamageRateToEnemyCritical)
        new[] { 1552, 1858, 2164, 2470, 2776 }, // Exp              (ExpRate)
        new[] { 1697, 2003, 2309, 2615, 2921 }, // Money            (MoneyRateToMaterials)
        new[] { 1726, 2032, 2338, 2644, 2950 }, // AilmentsOnYou    (BadStatusHitRateFromEnemy)
        new[] { 1755, 2061, 2367, 2673, 2979 }, // AilmentsByYou    (BadStatusHitRateFromPlayer)
    };

    // Stock value at each offset. Validates the bundled template before patching.
    private static readonly double[][] Vanilla = Presets.Vanilla;

    private const string VirtualSubPath = @"P3R\Content\Xrd777\Blueprints\Battle\Calculations";
    private const string AssetName = "DT_BtlDIfficultyParam";

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _configuration = context.Configuration;

        var generated = Bake();
        if (generated != null)
            Register(generated);
    }

    /// <summary>
    /// Pure core of the bake: validate the template is the table we expect, then write each configured
    /// multiplier (as a 32-bit float) into a copy of the bytes. Returns the patched copy, or null with a
    /// reason. No file I/O — kept separate so it can be unit-tested against the real bundled asset.
    /// </summary>
    public static byte[]? TryPatch(byte[] template, IReadOnlyDictionary<string, double> values, out int changed, out string? error)
    {
        changed = 0;
        error = null;

        for (int f = 0; f < Fields.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
            {
                int off = Offsets[f][r];
                if (off + 4 > template.Length)
                {
                    error = "Offset out of range; wrong template.";
                    return null;
                }
                float cur = BitConverter.ToSingle(template, off);
                if (Math.Abs(cur - (float)Vanilla[f][r]) > 0.01f)
                {
                    error = $"Template validation failed at {Fields[f]}/{Rows[r]} (got {cur}, expected {Vanilla[f][r]}). Game version may differ.";
                    return null;
                }
            }

        var bytes = (byte[])template.Clone();
        for (int f = 0; f < Fields.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
            {
                float v = (float)values[Presets.Key(f, r)];
                if (Math.Abs(v - (float)Vanilla[f][r]) > 0.0001f) changed++;
                BitConverter.GetBytes(v).CopyTo(bytes, Offsets[f][r]);
            }
        return bytes;
    }

    /// <summary>Reads the [field][row] float table back out of a (patched or stock) asset. For tests/logging.</summary>
    public static float[][] ReadTable(byte[] bytes)
    {
        var t = new float[Fields.Length][];
        for (int f = 0; f < Fields.Length; f++)
        {
            t[f] = new float[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                t[f][r] = BitConverter.ToSingle(bytes, Offsets[f][r]);
        }
        return t;
    }

    /// <summary>Patches the bundled template into the Generated folder. Returns that folder, or null on failure.</summary>
    private string? Bake()
    {
        try
        {
            var modDir = _modLoader.GetDirectoryForModId(_modConfig.ModId);
            var srcUasset = Path.Combine(modDir, "Assets", AssetName + ".uasset");
            if (!File.Exists(srcUasset))
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Bundled template missing, aborting.", Color.Red);
                return null;
            }

            var template = File.ReadAllBytes(srcUasset);
            var bytes = TryPatch(template, _configuration.GetValues(), out int changed, out string? error);
            if (bytes == null)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] {error} Aborting (game keeps stock values).", Color.Red);
                return null;
            }

            var outDir = Path.Combine(modDir, "Generated", VirtualSubPath);
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, AssetName + ".uasset"), bytes);

            _logger.WriteLine($"[{_modConfig.ModId}] Baked difficulty table ({changed} values changed from stock).", Color.LightGreen);
            if (_configuration.LogToConsole)
                LogTable(ReadTable(bytes));

            return Path.Combine(modDir, "Generated");
        }
        catch (Exception e)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Bake failed: {e}", Color.Red);
            return null;
        }
    }

    private void Register(string generatedFolder)
    {
        var controller = _modLoader.GetController<IUnrealEssentials>();
        if (controller == null || !controller.TryGetTarget(out var unrealEssentials))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Could not get the Unreal Essentials controller; is Unreal Essentials enabled?", Color.Red);
            return;
        }
        unrealEssentials.AddFromFolder(generatedFolder);
        _logger.WriteLine($"[{_modConfig.ModId}] Registered patched difficulty table with Unreal Essentials.", Color.LightGreen);
    }

    private void LogTable(float[][] t)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] {"",-10} {"Deal",6} {"DealW",6} {"DealC",6} {"Take",6} {"TakeW",6} {"TakeC",6} {"Exp",6} {"Money",6} {"AilOn",6} {"AilBy",6}");
        for (int r = 0; r < Rows.Length; r++)
            _logger.WriteLine($"[{_modConfig.ModId}] {Rows[r],-10} {t[0][r],6:0.###} {t[1][r],6:0.###} {t[2][r],6:0.###} {t[3][r],6:0.###} {t[4][r],6:0.###} {t[5][r],6:0.###} {t[6][r],6:0.###} {t[7][r],6:0.###} {t[8][r],6:0.###} {t[9][r],6:0.###}");
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _configuration = configuration;
        // The asset is already loaded in the running game, so re-baking only takes
        // effect on the next launch. Write it now so the next launch is correct.
        Bake();
        _logger.WriteLine($"[{_modConfig.ModId}] Config updated - restart the game for changes to take effect.", Color.Yellow);
    }

    public Mod() { _modLoader = null!; _logger = null!; _modConfig = null!; _configuration = null!; }
}
