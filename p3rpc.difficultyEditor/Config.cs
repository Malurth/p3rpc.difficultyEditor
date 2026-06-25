using p3rpc.difficultyEditor.Template.Configuration;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace p3rpc.difficultyEditor.Configuration;

/// <summary>
/// Per-difficulty combat multipliers for Persona 3 Reload.
/// All values are multipliers where <b>1.0 == Normal/vanilla baseline</b>, mapping directly onto the
/// game's DT_BtlDIfficultyParam data table. Fields default to the game's stock numbers.
///
/// The Preset field is a live mirror of the values: its getter reports whichever preset the current
/// values match (or Custom if none), and selecting a preset fills the fields with that preset's values.
/// </summary>
public class Config : Configurable<Config>, INotifyPropertyChanged
{
    // ---- value layout (field x difficulty) ------------------------------
    private static readonly string[] Rows = { "Peaceful", "Easy", "Normal", "Hard", "Merciless" };
    private static readonly string[] FieldNames =
    {
        "DamageDealt", "DamageDealtWeak", "DamageDealtCrit",
        "DamageTaken", "DamageTakenWeak", "DamageTakenCrit",
        "Exp", "Money", "AilmentsOnYou", "AilmentsByYou",
    };
    private const int EASY = 1, NORMAL = 2, MERCILESS = 4;

    // Stock values [field][row]. Also the default for every field. (Only Merciless raises weak/crit.)
    private static readonly double[][] VANILLA =
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

    private static double[][] CloneVanilla()
    {
        var t = new double[VANILLA.Length][];
        for (int f = 0; f < VANILLA.Length; f++) t[f] = (double[])VANILLA[f].Clone();
        return t;
    }

    // The author's gentle-but-harder Easy: deal 1.15x, take 0.85x, resist ailments at 0.85x. Others stock.
    private static double[][] MakeHarderEasy()
    {
        var t = CloneVanilla();
        t[0][EASY] = 1.15;  // DamageDealt
        t[3][EASY] = 0.85;  // DamageTaken
        t[8][EASY] = 0.85;  // AilmentsOnYou
        return t;
    }

    // Every cell pulled halfway to Normal (1.0). Softens Peaceful/Easy and Hard/Merciless alike.
    private static double[][] MakeHalfsies()
    {
        var t = new double[VANILLA.Length][];
        for (int f = 0; f < VANILLA.Length; f++)
        {
            t[f] = new double[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                t[f][r] = (VANILLA[f][r] + 1.0) / 2.0;
        }
        return t;
    }

    // Copy Merciless's weakness/crit damage multipliers onto every difficulty.
    private static double[][] MakeBursty()
    {
        var t = CloneVanilla();
        for (int r = 0; r < Rows.Length; r++)
        {
            t[1][r] = VANILLA[1][MERCILESS]; // DamageDealtWeak
            t[2][r] = VANILLA[2][MERCILESS]; // DamageDealtCrit
            t[4][r] = VANILLA[4][MERCILESS]; // DamageTakenWeak
            t[5][r] = VANILLA[5][MERCILESS]; // DamageTakenCrit
        }
        return t;
    }

    private static readonly (PresetType type, double[][] table)[] PresetTables =
    {
        (PresetType.Vanilla, VANILLA),
        (PresetType.HarderEasy, MakeHarderEasy()),
        (PresetType.Halfsies, MakeHalfsies()),
        (PresetType.Bursty, MakeBursty()),
    };

    private static readonly PropertyInfo[][] Props = BuildProps();
    private static PropertyInfo[][] BuildProps()
    {
        var map = new PropertyInfo[FieldNames.Length][];
        for (int f = 0; f < FieldNames.Length; f++)
        {
            map[f] = new PropertyInfo[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                map[f][r] = typeof(Config).GetProperty($"{Rows[r]}_{FieldNames[f]}")!;
        }
        return map;
    }

    // ---- dynamic field visibility (see HideFieldsProvider.cs) -----------
    // Type-level flag the grid's TypeDescriptionProvider reads to hide weak/crit fields.
    internal static bool HideBurstActive = true;
    static Config() => TypeDescriptor.AddProvider(new HideFieldsTypeDescriptionProvider(), typeof(Config));

    // ---- storage + change notification ----------------------------------
    private readonly Dictionary<string, double> _values = new();
    private bool _applyingPreset;

    public Config()
    {
        for (int f = 0; f < FieldNames.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                _values[$"{Rows[r]}_{FieldNames[f]}"] = VANILLA[f][r];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private double Get([CallerMemberName] string name = "") => _values.TryGetValue(name, out var v) ? v : 1.0;
    private void Set(double value, [CallerMemberName] string name = "")
    {
        if (_values.TryGetValue(name, out var cur) && cur.Equals(value)) return;
        _values[name] = value;
        if (_applyingPreset) return;
        Raise(name);
        Raise(nameof(Preset));
    }

    // ---- preset matching / applying -------------------------------------
    private PresetType MatchPreset()
    {
        foreach (var (type, table) in PresetTables)
            if (Matches(table)) return type;
        return PresetType.Custom;
    }

    private bool Matches(double[][] table)
    {
        for (int f = 0; f < FieldNames.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                if (Math.Abs((double)Props[f][r].GetValue(this)! - table[f][r]) > 1e-6)
                    return false;
        return true;
    }

    private void ApplyPreset(PresetType type)
    {
        double[][]? table = null;
        foreach (var p in PresetTables) if (p.type == type) { table = p.table; break; }
        if (table == null) return;

        _applyingPreset = true;
        for (int f = 0; f < FieldNames.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                Props[f][r].SetValue(this, table[f][r]);
        _applyingPreset = false;

        for (int f = 0; f < FieldNames.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                Raise($"{Rows[r]}_{FieldNames[f]}");
    }

    // ---- exposed config --------------------------------------------------
    [JsonIgnore]
    [Display(Order = 0)]
    [DisplayName("Preset")]
    [Description("Shows whichever preset the values below currently match, or Custom if they match none.\n" +
                 "Selecting a preset fills the fields with its values (you can then tweak them):\n" +
                 "Vanilla    = the game's stock values.\n" +
                 "HarderEasy = a less-cushy Easy: you deal 1.15x (vs 1.25), take 0.85x (vs 0.5), and resist ailments at 0.85x (vs 0.5). Other difficulties stock.\n" +
                 "Halfsies   = every difficulty's gap from Normal cut in half - milder Peaceful/Easy AND milder Hard/Merciless.\n" +
                 "Bursty     = gives every difficulty Merciless's weakness (1.36x) and critical (1.34x) damage, both dealt and taken.")]
    public PresetType Preset
    {
        get => MatchPreset();
        set { if (value != PresetType.Custom) ApplyPreset(value); Raise(nameof(Preset)); }
    }

    private bool _hideBurst = true;
    [Display(Order = 1)]
    [DisplayName("Hide weakness/crit settings")]
    [Description("Hides the per-difficulty Weakness and Critical damage multipliers (they're 1.0 except on Merciless). " +
                 "Untick to show and edit them. Hidden fields keep their values and still apply.")]
    [DefaultValue(true)]
    public bool HideBurstFields
    {
        get => _hideBurst;
        set
        {
            bool changed = _hideBurst != value;
            _hideBurst = value;
            HideBurstActive = value;   // type-level flag the grid's provider reads
            if (!changed) return;
            Raise(nameof(HideBurstFields));
            Save?.Invoke();            // force a config reload so the grid rebuilds its property list
        }
    }

    [Display(Order = 2)]
    [DisplayName("Log to console (debug)")]
    [Description("Write what the mod baked to the Reloaded console (for debugging).")]
    [DefaultValue(false)]
    public bool LogToConsole { get; set; } = false;

    // ---- Merciless -------------------------------------------------------
    [Category("Merciless")] [DisplayName("Damage You Deal")]
    [Display(Order = 50)]
    [DefaultValue(0.6)] public double Merciless_DamageDealt { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Damage You Deal (Weakness)")]
    [Description("Your damage when you strike an enemy's weakness. Stock 1.0 everywhere except Merciless (1.36).")]
    [Display(Order = 51)]
    [DefaultValue(1.36)] public double Merciless_DamageDealtWeak { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Damage You Deal (Critical)")]
    [Description("Your critical-hit damage. Stock 1.0 everywhere except Merciless (1.34).")]
    [Display(Order = 52)]
    [DefaultValue(1.34)] public double Merciless_DamageDealtCrit { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Damage You Take")]
    [Display(Order = 53)]
    [DefaultValue(1.5)] public double Merciless_DamageTaken { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Damage You Take (Weakness)")]
    [Description("Damage you take when an enemy strikes your weakness. Stock 1.0 everywhere except Merciless (1.36).")]
    [Display(Order = 54)]
    [DefaultValue(1.36)] public double Merciless_DamageTakenWeak { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Damage You Take (Critical)")]
    [Description("Critical-hit damage you take. Stock 1.0 everywhere except Merciless (1.34).")]
    [Display(Order = 55)]
    [DefaultValue(1.34)] public double Merciless_DamageTakenCrit { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("EXP Gained")]
    [Display(Order = 56)]
    [DefaultValue(1.0)] public double Merciless_Exp { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Item Sell Value")]
    [Display(Order = 57)]
    [DefaultValue(1.0)] public double Merciless_Money { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Ailments Landing On You")]
    [Display(Order = 58)]
    [DefaultValue(1.2)] public double Merciless_AilmentsOnYou { get => Get(); set => Set(value); }
    [Category("Merciless")] [DisplayName("Ailments You Inflict")]
    [Display(Order = 59)]
    [DefaultValue(0.8)] public double Merciless_AilmentsByYou { get => Get(); set => Set(value); }

    // ---- Hard ------------------------------------------------------------
    [Category("Hard")] [DisplayName("Damage You Deal")]
    [Display(Order = 40)]
    [DefaultValue(0.8)] public double Hard_DamageDealt { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Damage You Deal (Weakness)")]
    [Display(Order = 41)]
    [DefaultValue(1.0)] public double Hard_DamageDealtWeak { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Damage You Deal (Critical)")]
    [Display(Order = 42)]
    [DefaultValue(1.0)] public double Hard_DamageDealtCrit { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Damage You Take")]
    [Display(Order = 43)]
    [DefaultValue(1.3)] public double Hard_DamageTaken { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Damage You Take (Weakness)")]
    [Display(Order = 44)]
    [DefaultValue(1.0)] public double Hard_DamageTakenWeak { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Damage You Take (Critical)")]
    [Display(Order = 45)]
    [DefaultValue(1.0)] public double Hard_DamageTakenCrit { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("EXP Gained")]
    [Display(Order = 46)]
    [DefaultValue(1.0)] public double Hard_Exp { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Item Sell Value")]
    [Display(Order = 47)]
    [DefaultValue(1.0)] public double Hard_Money { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Ailments Landing On You")]
    [Display(Order = 48)]
    [DefaultValue(1.0)] public double Hard_AilmentsOnYou { get => Get(); set => Set(value); }
    [Category("Hard")] [DisplayName("Ailments You Inflict")]
    [Display(Order = 49)]
    [DefaultValue(1.0)] public double Hard_AilmentsByYou { get => Get(); set => Set(value); }

    // ---- Normal ----------------------------------------------------------
    [Category("Normal")] [DisplayName("Damage You Deal")]
    [Display(Order = 30)]
    [DefaultValue(1.0)] public double Normal_DamageDealt { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Damage You Deal (Weakness)")]
    [Display(Order = 31)]
    [DefaultValue(1.0)] public double Normal_DamageDealtWeak { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Damage You Deal (Critical)")]
    [Display(Order = 32)]
    [DefaultValue(1.0)] public double Normal_DamageDealtCrit { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Damage You Take")]
    [Display(Order = 33)]
    [DefaultValue(1.0)] public double Normal_DamageTaken { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Damage You Take (Weakness)")]
    [Display(Order = 34)]
    [DefaultValue(1.0)] public double Normal_DamageTakenWeak { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Damage You Take (Critical)")]
    [Display(Order = 35)]
    [DefaultValue(1.0)] public double Normal_DamageTakenCrit { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("EXP Gained")]
    [Display(Order = 36)]
    [DefaultValue(1.0)] public double Normal_Exp { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Item Sell Value")]
    [Display(Order = 37)]
    [DefaultValue(1.0)] public double Normal_Money { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Ailments Landing On You")]
    [Display(Order = 38)]
    [DefaultValue(1.0)] public double Normal_AilmentsOnYou { get => Get(); set => Set(value); }
    [Category("Normal")] [DisplayName("Ailments You Inflict")]
    [Display(Order = 39)]
    [DefaultValue(1.0)] public double Normal_AilmentsByYou { get => Get(); set => Set(value); }

    // ---- Easy ------------------------------------------------------------
    [Category("Easy")] [DisplayName("Damage You Deal")]
    [Description("Multiplier on damage you deal. Stock Easy = 1.25.")]
    [Display(Order = 20)]
    [DefaultValue(1.25)] public double Easy_DamageDealt { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Damage You Deal (Weakness)")]
    [Display(Order = 21)]
    [DefaultValue(1.0)] public double Easy_DamageDealtWeak { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Damage You Deal (Critical)")]
    [Display(Order = 22)]
    [DefaultValue(1.0)] public double Easy_DamageDealtCrit { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Damage You Take")]
    [Description("Multiplier on damage enemies deal to you. Stock Easy = 0.5 (you take half).")]
    [Display(Order = 23)]
    [DefaultValue(0.5)] public double Easy_DamageTaken { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Damage You Take (Weakness)")]
    [Display(Order = 24)]
    [DefaultValue(1.0)] public double Easy_DamageTakenWeak { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Damage You Take (Critical)")]
    [Display(Order = 25)]
    [DefaultValue(1.0)] public double Easy_DamageTakenCrit { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("EXP Gained")]
    [Description("Multiplier on EXP from battle. Stock Easy = 1.2.")]
    [Display(Order = 26)]
    [DefaultValue(1.2)] public double Easy_Exp { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Item Sell Value")]
    [Description("Multiplier on money from selling materials. Stock Easy = 1.2.")]
    [Display(Order = 27)]
    [DefaultValue(1.2)] public double Easy_Money { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Ailments Landing On You")]
    [Description("Multiplier on the chance enemy ailments hit you. Stock Easy = 0.5.")]
    [Display(Order = 28)]
    [DefaultValue(0.5)] public double Easy_AilmentsOnYou { get => Get(); set => Set(value); }
    [Category("Easy")] [DisplayName("Ailments You Inflict")]
    [Description("Multiplier on the chance your ailments land. Stock Easy = 1.2.")]
    [Display(Order = 29)]
    [DefaultValue(1.2)] public double Easy_AilmentsByYou { get => Get(); set => Set(value); }

    // ---- Peaceful --------------------------------------------------------
    [Category("Peaceful")] [DisplayName("Damage You Deal")]
    [Display(Order = 10)]
    [DefaultValue(1.6)] public double Peaceful_DamageDealt { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Damage You Deal (Weakness)")]
    [Display(Order = 11)]
    [DefaultValue(1.0)] public double Peaceful_DamageDealtWeak { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Damage You Deal (Critical)")]
    [Display(Order = 12)]
    [DefaultValue(1.0)] public double Peaceful_DamageDealtCrit { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Damage You Take")]
    [Display(Order = 13)]
    [DefaultValue(0.5)] public double Peaceful_DamageTaken { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Damage You Take (Weakness)")]
    [Display(Order = 14)]
    [DefaultValue(1.0)] public double Peaceful_DamageTakenWeak { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Damage You Take (Critical)")]
    [Display(Order = 15)]
    [DefaultValue(1.0)] public double Peaceful_DamageTakenCrit { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("EXP Gained")]
    [Display(Order = 16)]
    [DefaultValue(1.5)] public double Peaceful_Exp { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Item Sell Value")]
    [Display(Order = 17)]
    [DefaultValue(1.5)] public double Peaceful_Money { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Ailments Landing On You")]
    [Display(Order = 18)]
    [DefaultValue(0.1)] public double Peaceful_AilmentsOnYou { get => Get(); set => Set(value); }
    [Category("Peaceful")] [DisplayName("Ailments You Inflict")]
    [Display(Order = 19)]
    [DefaultValue(1.5)] public double Peaceful_AilmentsByYou { get => Get(); set => Set(value); }

    public enum PresetType
    {
        Custom,
        Vanilla,
        HarderEasy,
        Halfsies,
        Bursty,
    }
}

/// <summary>
/// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
/// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
    //
}
