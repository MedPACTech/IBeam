namespace IBeam.AccessControl;

public sealed class AccessControlOptions
{
    public const string SectionName = "IBeam:AccessControl";

    public Dictionary<string, int> AccessLevelRanks { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [ResourceAccessLevels.Read] = 10,
        [ResourceAccessLevels.View] = 10,
        [ResourceAccessLevels.Write] = 20,
        [ResourceAccessLevels.Edit] = 20,
        [ResourceAccessLevels.Manage] = 30,
        [ResourceAccessLevels.Admin] = 40,
        [ResourceAccessLevels.Owner] = 50,
        ["*"] = int.MaxValue
    };

    public void Validate()
    {
        AccessLevelRanks = AccessLevelRanks
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);

        if (!AccessLevelRanks.ContainsKey("*"))
            AccessLevelRanks["*"] = int.MaxValue;
    }
}
