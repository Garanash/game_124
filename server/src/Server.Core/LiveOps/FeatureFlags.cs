namespace Realmforge.Server.Core.LiveOps;

/// <summary>
/// Server-side switches for rolling out features (a new continent, the auction house, a store sale) without
/// a client release. Unknown flags are off, so a typo can only hide a feature, never expose an unfinished one.
/// </summary>
public sealed class FeatureFlags
{
    public const string SectionName = "Features";

    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string flag) => Flags.TryGetValue(flag, out bool enabled) && enabled;

    public IReadOnlyCollection<string> Enabled() => Flags.Where(flag => flag.Value).Select(flag => flag.Key).OrderBy(flag => flag).ToList();
}
