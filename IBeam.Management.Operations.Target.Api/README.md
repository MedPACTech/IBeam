# IBeam.Management.Operations.Target.Api

Optional registered-operation discovery and typed signed target dispatch. No Identity, Licensing or consumer module is forced into this package. Hosts own registered contracts, trusted portal bindings, live machine storage, pinned signing keys, atomic replay storage and metadata audit.

Register `AddIBeamManagementRegisteredOperationsTargetApi(configuration)`, then map `MapIBeamManagementRegisteredOperations()`. Only installed host-owned descriptors are mapped. This is a dedicated key-plus-short-lived-actor boundary; customer cookies/JWTs and arbitrary forwarding are unsupported. See the registered-operation dispatch guide in the source repository.
