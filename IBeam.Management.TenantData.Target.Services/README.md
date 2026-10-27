# IBeam.Management.TenantData.Target.Services

Optional tenant export target adapter with versioned safe job/coverage contracts. No native Identity or commercial dependency. Register AddIBeamManagementTenantExportTarget with explicit enabling and approved policy keys. The host must supply a usable exporter, delivery and customer-authorization policies, native tenant-version policy, live signed Management access and a durable job service. Missing providers fail startup; default registration installs nothing.

This contract card does not implement the framework's durable worker/store. IBM-0131 supplies that implementation; hosts may implement the same port. See docs/management/tenant-export.md for the contract, routes and ordered rollout. No customer business content is returned through Management.
