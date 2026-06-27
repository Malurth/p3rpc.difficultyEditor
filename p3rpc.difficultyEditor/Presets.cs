namespace p3rpc.difficultyEditor.Configuration;

/// <summary>
/// Canonical definitions of the per-difficulty multiplier table, the seed presets shipped with the
/// mod, and helpers to compare/convert preset value sets. Deliberately free of any WPF or Reloaded
/// dependency so it's offline-testable and shared by <see cref="Config"/>, <c>Mod</c>, and the window.
///
/// A "value set" is a flat <see cref="Dictionary{TKey,TValue}"/> keyed by "{Difficulty}_{Field}"
/// (e.g. "Easy_DamageTaken") — exactly how <see cref="Config"/> exposes its 50 properties.
///
/// Only <see cref="Vanilla"/> is treated as a protected, code-defined preset. The others
/// (<see cref="DefaultPresets"/>) are seeded into the user's editable preset list on first use, so
/// the user can delete/replace them just like their own presets.
/// </summary>
public static class Presets
{
    // Row (difficulty) order MUST match the row order inside the .uexp and Mod.Offsets.
    public static readonly string[] Rows = { "Peaceful", "Easy", "Normal", "Hard", "Merciless" };

    public static readonly string[] Fields =
    {
        "DamageDealt", "DamageDealtWeak", "DamageDealtCrit",
        "DamageTaken", "DamageTakenWeak", "DamageTakenCrit",
        "Exp", "Money", "AilmentsOnYou", "AilmentsByYou",
    };

    // Friendly labels for the window, indexed like Fields.
    public static readonly string[] FieldLabels =
    {
        "Damage You Deal", "Damage You Deal (Weakness)", "Damage You Deal (Critical)",
        "Damage You Take", "Damage You Take (Weakness)", "Damage You Take (Critical)",
        "EXP Gained", "Item Sell Value", "Ailments Landing On You", "Ailments You Inflict",
    };

    private const int EASY = 1, MERCILESS = 4;

    // Stock values [field][row]. Also the default for every field. (Only Merciless raises weak/crit.)
    public static readonly double[][] Vanilla =
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

    /// <summary>"{Difficulty}_{Field}" key for a cell, e.g. (field 3, row 1) => "Easy_DamageTaken".</summary>
    public static string Key(int field, int row) => $"{Rows[row]}_{Fields[field]}";

    public static bool IsBurstField(int field) =>
        Fields[field].EndsWith("Weak") || Fields[field].EndsWith("Crit");

    public static double[][] Clone(double[][] t)
    {
        var r = new double[t.Length][];
        for (int i = 0; i < t.Length; i++) r[i] = (double[])t[i].Clone();
        return r;
    }

    /// <summary>Flatten a [field][row] table into a "{Difficulty}_{Field}"-keyed value set.</summary>
    public static Dictionary<string, double> ToValues(double[][] table)
    {
        var d = new Dictionary<string, double>(Fields.Length * Rows.Length);
        for (int f = 0; f < Fields.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                d[Key(f, r)] = table[f][r];
        return d;
    }

    /// <summary>True if every cell of <paramref name="values"/> matches <paramref name="table"/>.</summary>
    public static bool Matches(IReadOnlyDictionary<string, double> values, double[][] table)
    {
        for (int f = 0; f < Fields.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
                if (!values.TryGetValue(Key(f, r), out var v) || Math.Abs(v - table[f][r]) > 1e-6)
                    return false;
        return true;
    }

    /// <summary>True if two value sets are equal across all 50 cells.</summary>
    public static bool ValuesEqual(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        for (int f = 0; f < Fields.Length; f++)
            for (int r = 0; r < Rows.Length; r++)
            {
                var k = Key(f, r);
                if (!a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || Math.Abs(av - bv) > 1e-6)
                    return false;
            }
        return true;
    }

    // ---- seed presets ----------------------------------------------------
    private static double[][] MakeHarderEasy()
    {
        var t = Clone(Vanilla);
        t[0][EASY] = 1.15;  // DamageDealt
        t[3][EASY] = 0.85;  // DamageTaken
        t[8][EASY] = 0.85;  // AilmentsOnYou
        return t;
    }

    private static double[][] MakeHalfsies()
    {
        var t = new double[Vanilla.Length][];
        for (int f = 0; f < Vanilla.Length; f++)
        {
            t[f] = new double[Rows.Length];
            for (int r = 0; r < Rows.Length; r++)
                // Round so the computed value snaps to a clean double (e.g. (1.36+1)/2 -> 1.18,
                // not 1.1800000000000002). Multipliers never need more than a few decimals.
                t[f][r] = Math.Round((Vanilla[f][r] + 1.0) / 2.0, 6);
        }
        return t;
    }

    private static double[][] MakeBursty()
    {
        var t = Clone(Vanilla);
        for (int r = 0; r < Rows.Length; r++)
        {
            t[1][r] = Vanilla[1][MERCILESS]; // DamageDealtWeak
            t[2][r] = Vanilla[2][MERCILESS]; // DamageDealtCrit
            t[4][r] = Vanilla[4][MERCILESS]; // DamageTakenWeak
            t[5][r] = Vanilla[5][MERCILESS]; // DamageTakenCrit
        }
        return t;
    }

    public sealed record NamedPreset(string Name, double[][] Table);

    /// <summary>Presets seeded into the user's editable list on first use (deletable thereafter).</summary>
    public static readonly NamedPreset[] DefaultPresets =
    {
        new("HarderEasy", MakeHarderEasy()),
        new("Halfsies",   MakeHalfsies()),
        new("Bursty",     MakeBursty()),
    };

    /// <summary>Name of whichever code-defined preset the value set matches (Vanilla or a seed), else null.</summary>
    public static string? MatchName(IReadOnlyDictionary<string, double> values)
    {
        if (Matches(values, Vanilla)) return "Vanilla";
        foreach (var d in DefaultPresets)
            if (Matches(values, d.Table)) return d.Name;
        return null;
    }
}

/// <summary>
/// A named snapshot of the 50 multipliers. The user's saved presets (and, after first-run seeding,
/// the bundled HarderEasy/Halfsies/Bursty) are stored as these inside Config.json
/// (<see cref="Config.CustomPresets"/>), so they persist across launches and are all deletable.
/// </summary>
public sealed class UserPreset
{
    public string Name { get; set; } = "";

    /// <summary>"{Difficulty}_{Field}" => multiplier. Same key shape as <see cref="Presets.Key"/>.</summary>
    public Dictionary<string, double> Values { get; set; } = new();
}
