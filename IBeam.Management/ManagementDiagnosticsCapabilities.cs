namespace IBeam.Management;

/// <summary>An explicitly installed HTTP module advertises its operation; this does not grant it to a credential.</summary>
public interface IManagementDiagnosticsCapability
{
    string Operation { get; }
    bool IsSanitized => true;
}
