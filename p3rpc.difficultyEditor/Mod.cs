using p3rpc.difficultyEditor.Configuration;
using p3rpc.difficultyEditor.Template;
using Reloaded.Mod.Interfaces;
using System.Drawing;
using System.Reflection;
using UnrealEssentials.Interfaces;

namespace p3rpc.difficultyEditor;

/// <summary>
/// Reads the configured per-difficulty multipliers, patches a copy of the game's
/// DT_BtlDIfficultyParam data table, and feeds it to Unreal Essentials so the game
/// loads our values instead of the stock ones. No game hooks required.
/// (Presets are resolved in the Config class itself, inside the launcher; here we just read values.)
/// </summary>
public class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private Config _configuration;

    // Row order MUST match the row order inside the .uexp.
    private static readonly string[] Rows = { "Peaceful", "Easy", "Normal", "Hard", "Merciless" };
    private static readonly string[] Fields =
    {
        "DamageDealt", "DamageDealtWeak", "DamageDealtCrit",
        "DamageTaken", "DamageTakenWeak", "DamageTakenCrit",
        "Exp", "Money", "AilmentsOnYou", "AilmentsByYou",
    };

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
    private static readonly double[][] Vanilla =
    {
        new[] { 1.6, 1.25, 1.0, 0.8, 0.6  }, // DamageDealt
        new[] { 1.0, 1.0,  1.0, 1.0, 1.36 }, // DamageDealtWeak
        new[] { 1.0, 1.0,  1.0, 1.0, 1.34 }, // DamageDealtCrit
        new[] { 0.5, 0.5,  1.0, 1.3, 1.5  }, // DamageTaken
        new[] { 1.0, 1.0,  1.0, 1.0, 1.36 }, // DamageTakenWeak
        new[] { 1.0, 1.0,  1.0, 1.0, 1.34 }, // DamageTakenCrit
        new[] { 1.5, 1.2,  1.0, 1.0, 1.0  }, // Exp
        new[] { 1.5, 1.2,  1.0, 1.0, 1.0  }, // Money
        new[] { 0.1, 0.5,  1.0, 1.0, 1.2  }, // AilmentsOnYou
        new[] { 1.5, 1.2,  1.0, 1.0, 0.8  }, // AilmentsByYou
    };

    private static readonly PropertyInfo[][] Props = BuildPropMap();

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

    private static PropertyInfo[][] BuildPropMap()
    {
        var map = new PropertyInfo[Fields.Length][];
        for (int f = 0; f < Fields.Length; f++)
        {
            map[f] = new PropertyInfo[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                map[f][r] = typeof(Config).GetProperty($"{Rows[r]}_{Fields[f]}")!;
        }
        return map;
    }

    /// <summary>Reads the effective [field][row] multiplier table straight from the config fields.</summary>
    private float[][] BuildTable()
    {
        var t = new float[Fields.Length][];
        for (int f = 0; f < Fields.Length; f++)
        {
            t[f] = new float[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                t[f][r] = (float)(double)Props[f][r].GetValue(_configuration)!;
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

            var bytes = File.ReadAllBytes(srcUasset);

            // Validate the template is the table we think it is, before touching anything.
            for (int f = 0; f < Fields.Length; f++)
                for (int r = 0; r < Rows.Length; r++)
                {
                    int off = Offsets[f][r];
                    if (off + 4 > bytes.Length)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] Offset out of range; wrong template. Aborting (game keeps stock values).", Color.Red);
                        return null;
                    }
                    float cur = BitConverter.ToSingle(bytes, off);
                    if (Math.Abs(cur - (float)Vanilla[f][r]) > 0.01f)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] Template validation failed at {Fields[f]}/{Rows[r]} (got {cur}, expected {Vanilla[f][r]}). Game version may differ. Aborting (game keeps stock values).", Color.Red);
                        return null;
                    }
                }

            // Patch.
            var table = BuildTable();
            int changed = 0;
            for (int f = 0; f < Fields.Length; f++)
                for (int r = 0; r < Rows.Length; r++)
                {
                    float v = table[f][r];
                    if (Math.Abs(v - (float)Vanilla[f][r]) > 0.0001f) changed++;
                    BitConverter.GetBytes(v).CopyTo(bytes, Offsets[f][r]);
                }

            var outDir = Path.Combine(modDir, "Generated", VirtualSubPath);
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, AssetName + ".uasset"), bytes);

            _logger.WriteLine($"[{_modConfig.ModId}] Baked difficulty table ({changed} values changed from stock).", Color.LightGreen);
            if (_configuration.LogToConsole)
                LogTable(table);

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
