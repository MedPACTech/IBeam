namespace IBeam.Management.Operations;

/// <summary>Supported typed portal sender for registered app operations. The registry supplies fixed routes,
/// methods and schemas; the selected live connection supplies its approved origin and secret reference.
/// Callers supply scoped IDs and typed values, never URLs, headers, credentials or arbitrary HTTP bodies.
/// The inherited legacy client contract remains available for existing consumers.</summary>
public interface IManagementTargetOperationSender : IManagementOperationClient { }
