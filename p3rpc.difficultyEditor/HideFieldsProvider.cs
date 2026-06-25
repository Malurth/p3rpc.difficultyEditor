using System.ComponentModel;

namespace p3rpc.difficultyEditor.Configuration;

/// <summary>
/// Reloaded's config grid (HandyControl PropertyGrid) enumerates properties via
/// <c>TypeDescriptor.GetProperties(obj.GetType())</c> — by Type, not instance — so an instance-level
/// ICustomTypeDescriptor is never consulted. This type-level provider is what lets us dynamically hide
/// the per-difficulty Weakness/Critical fields based on <see cref="Config.HideBurstActive"/>.
///
/// It only changes what the grid SHOWS; the underlying Config object keeps all properties and values
/// (serialization uses System.Text.Json, not TypeDescriptor), so hidden fields are still saved and applied.
/// </summary>
internal sealed class HideFieldsTypeDescriptionProvider : TypeDescriptionProvider
{
    public HideFieldsTypeDescriptionProvider()
        : base(TypeDescriptor.GetProvider(typeof(Config))) { }

    public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object? instance)
        => new HideFieldsTypeDescriptor(base.GetTypeDescriptor(objectType, instance)!);
}

internal sealed class HideFieldsTypeDescriptor : CustomTypeDescriptor
{
    public HideFieldsTypeDescriptor(ICustomTypeDescriptor parent) : base(parent) { }

    public override PropertyDescriptorCollection GetProperties() => Filter(base.GetProperties());
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => Filter(base.GetProperties(attributes));

    private static PropertyDescriptorCollection Filter(PropertyDescriptorCollection props)
    {
        // Always rebuild the collection the same way (same type, not read-only) whether or not we
        // filter, so the grid sorts both states identically. Returning the original collection in one
        // case and a read-only copy in the other made the grid order them differently, which swapped
        // the top-level toggles' positions when toggling.
        IEnumerable<PropertyDescriptor> list = props.Cast<PropertyDescriptor>();
        if (Config.HideBurstActive)
            list = list.Where(p => !IsBurstField(p.Name));
        return new PropertyDescriptorCollection(list.ToArray());
    }

    // Per-difficulty weak/crit fields are named "<Difficulty>_Damage...Weak" / "...Crit".
    // The "_" guard keeps the "HideWeakCrit"-style toggle itself visible.
    private static bool IsBurstField(string name) =>
        name.Contains('_') && (name.EndsWith("Weak") || name.EndsWith("Crit"));
}
