using p3rpc.difficultyEditor.Template.Configuration;
using System.Runtime.CompilerServices;

namespace p3rpc.difficultyEditor.Configuration;

/// <summary>
/// Per-difficulty combat multipliers for Persona 3 Reload, plus any user-saved presets.
/// All values are multipliers where <b>1.0 == Normal/vanilla baseline</b>, mapping directly onto the
/// game's DT_BtlDIfficultyParam data table. Fields default to the game's stock numbers.
///
/// The launcher's default property grid is bypassed (see <see cref="ConfiguratorMixin"/>): editing
/// happens in our own window (PresetManagerWindow). The 50 multiplier properties are still real
/// properties so the JSON shape stays stable and <c>Mod</c> can read them by name.
/// </summary>
public class Config : Configurable<Config>
{
    // Backing store, keyed "{Difficulty}_{Field}". The 50 properties below are thin views over it.
    private readonly Dictionary<string, double> _values = new();

    public Config()
    {
        for (int f = 0; f < Presets.Fields.Length; f++)
            for (int r = 0; r < Presets.Rows.Length; r++)
                _values[Presets.Key(f, r)] = Presets.Vanilla[f][r];
    }

    private double Get([CallerMemberName] string name = "") => _values.TryGetValue(name, out var v) ? v : 1.0;
    private void Set(double value, [CallerMemberName] string name = "") => _values[name] = value;

    /// <summary>Snapshot of all 50 multipliers, keyed "{Difficulty}_{Field}".</summary>
    public Dictionary<string, double> GetValues() => new(_values);

    /// <summary>Overwrite multipliers from a value set. Unknown keys are ignored; missing keys are left as-is.</summary>
    public void SetValues(IReadOnlyDictionary<string, double> values)
    {
        foreach (var kv in values)
            if (_values.ContainsKey(kv.Key)) _values[kv.Key] = kv.Value;
    }

    // ---- exposed, serialized state --------------------------------------

    /// <summary>User-defined presets (plus the seeded defaults). Persisted here so they survive across launches.</summary>
    public List<UserPreset> CustomPresets { get; set; } = new();

    /// <summary>True once the bundled default presets have been seeded into <see cref="CustomPresets"/>,
    /// so we don't re-add ones the user has deleted.</summary>
    public bool SeededDefaultPresets { get; set; } = false;

    /// <summary>Remembered window preference: start with the weakness/crit rows collapsed.</summary>
    public bool HideBurstFields { get; set; } = true;

    /// <summary>Write what the mod baked to the Reloaded console (for debugging).</summary>
    public bool LogToConsole { get; set; } = false;

    // ---- the 50 multiplier properties (names drive JSON + Mod reflection) ----
    // Peaceful
    public double Peaceful_DamageDealt      { get => Get(); set => Set(value); }
    public double Peaceful_DamageDealtWeak  { get => Get(); set => Set(value); }
    public double Peaceful_DamageDealtCrit  { get => Get(); set => Set(value); }
    public double Peaceful_DamageTaken      { get => Get(); set => Set(value); }
    public double Peaceful_DamageTakenWeak  { get => Get(); set => Set(value); }
    public double Peaceful_DamageTakenCrit  { get => Get(); set => Set(value); }
    public double Peaceful_Exp              { get => Get(); set => Set(value); }
    public double Peaceful_Money            { get => Get(); set => Set(value); }
    public double Peaceful_AilmentsOnYou    { get => Get(); set => Set(value); }
    public double Peaceful_AilmentsByYou    { get => Get(); set => Set(value); }
    // Easy
    public double Easy_DamageDealt          { get => Get(); set => Set(value); }
    public double Easy_DamageDealtWeak      { get => Get(); set => Set(value); }
    public double Easy_DamageDealtCrit      { get => Get(); set => Set(value); }
    public double Easy_DamageTaken          { get => Get(); set => Set(value); }
    public double Easy_DamageTakenWeak      { get => Get(); set => Set(value); }
    public double Easy_DamageTakenCrit      { get => Get(); set => Set(value); }
    public double Easy_Exp                  { get => Get(); set => Set(value); }
    public double Easy_Money                { get => Get(); set => Set(value); }
    public double Easy_AilmentsOnYou        { get => Get(); set => Set(value); }
    public double Easy_AilmentsByYou        { get => Get(); set => Set(value); }
    // Normal
    public double Normal_DamageDealt        { get => Get(); set => Set(value); }
    public double Normal_DamageDealtWeak    { get => Get(); set => Set(value); }
    public double Normal_DamageDealtCrit    { get => Get(); set => Set(value); }
    public double Normal_DamageTaken        { get => Get(); set => Set(value); }
    public double Normal_DamageTakenWeak    { get => Get(); set => Set(value); }
    public double Normal_DamageTakenCrit    { get => Get(); set => Set(value); }
    public double Normal_Exp                { get => Get(); set => Set(value); }
    public double Normal_Money              { get => Get(); set => Set(value); }
    public double Normal_AilmentsOnYou      { get => Get(); set => Set(value); }
    public double Normal_AilmentsByYou      { get => Get(); set => Set(value); }
    // Hard
    public double Hard_DamageDealt          { get => Get(); set => Set(value); }
    public double Hard_DamageDealtWeak      { get => Get(); set => Set(value); }
    public double Hard_DamageDealtCrit      { get => Get(); set => Set(value); }
    public double Hard_DamageTaken          { get => Get(); set => Set(value); }
    public double Hard_DamageTakenWeak      { get => Get(); set => Set(value); }
    public double Hard_DamageTakenCrit      { get => Get(); set => Set(value); }
    public double Hard_Exp                  { get => Get(); set => Set(value); }
    public double Hard_Money                { get => Get(); set => Set(value); }
    public double Hard_AilmentsOnYou        { get => Get(); set => Set(value); }
    public double Hard_AilmentsByYou        { get => Get(); set => Set(value); }
    // Merciless
    public double Merciless_DamageDealt     { get => Get(); set => Set(value); }
    public double Merciless_DamageDealtWeak { get => Get(); set => Set(value); }
    public double Merciless_DamageDealtCrit { get => Get(); set => Set(value); }
    public double Merciless_DamageTaken     { get => Get(); set => Set(value); }
    public double Merciless_DamageTakenWeak { get => Get(); set => Set(value); }
    public double Merciless_DamageTakenCrit { get => Get(); set => Set(value); }
    public double Merciless_Exp             { get => Get(); set => Set(value); }
    public double Merciless_Money           { get => Get(); set => Set(value); }
    public double Merciless_AilmentsOnYou   { get => Get(); set => Set(value); }
    public double Merciless_AilmentsByYou   { get => Get(); set => Set(value); }
}

/// <summary>
/// Overrides the configuration creation process. We bypass Reloaded's default property grid entirely
/// and run our own editor window (so user-defined presets can be saved/loaded/deleted with a real list UI).
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
    public override bool TryRunCustomConfiguration(Configurator configurator)
    {
        var config = configurator.GetConfiguration<Config>(0);
        PresetManagerWindow.Edit(config);
        return true;
    }
}
