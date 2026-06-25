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
        [ResourceAccessLevels.Delete] = 30,
        [ResourceAccessLevels.Manage] = 40,
        [ResourceAccessLevels.Admin] = 50,
        [ResourceAccessLevels.Owner] = 60,
        ["*"] = int.MaxValue
    };

    public bool EmitResourceAccessClaim { get; set; } = true;

    public int MaxResourceAccessClaimsInJwt { get; set; } = 200;

    public void Validate()
    {
        AccessLevelRanks = AccessLevelRanks
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);

        if (!AccessLevelRanks.ContainsKey("*"))
            AccessLevelRanks["*"] = int.MaxValue;

        if (MaxResourceAccessClaimsInJwt < 1)
            MaxResourceAccessClaimsInJwt = 1;
    }
}
