# IBeam.Management.Diagnostics.Api

Reusable target-side management diagnostics API library, installed separately for each application/environment. It does not create a runnable host or authenticate API keys by itself.

The host must first register a dedicated machine credential scheme. It must verify the key secret, expiry, revocation, allowed operations and deployment binding. Do not reuse the ordinary customer JWT/API-key scheme. The persisted management credential provider is a later epic feature.

```csharp
// Register "ManagementCredential" with trusted target credential verification first.
builder.Services.AddIBeamManagementDiagnosticsApi(builder.Configuration,
    options => options.AuthenticationScheme = "ManagementCredential");

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

Configuration:
```json
{
  "IBeam": {
    "Management": {
      "Diagnostics": {
        "ApplicationKey": "qurvia",
        "Environment": "Production",
        "Api": { "AuthenticationScheme": "ManagementCredential" }
      }
    }
  }
}
```

Blank/unregistered schemes and missing application/environment settings fail startup. Registration leaves customer authentication defaults unchanged.

`GET /api/management-diagnostics/info` requires one authenticated identity with:
- `ibeam_management_subject_type=credential`.
- `ibeam_management_credential_id=<nonempty GUID>`.
- `ibeam_management_application_key` and `ibeam_management_environment` exactly matching configuration (case-sensitive).
- `ibeam_management_permission=management.diagnostics.info`.

The IBeam response envelope contains application key, environment, protocol version and implemented capabilities. Only `management.diagnostics.info` is advertised by this foundation. Neither errors nor logs are exposed yet.

The API returns 401 for missing/wrong authentication and safe 403 responses for invalid subject/capability/deployment context. Services recheck context even when invoked without controllers. Deploy separately with narrow storage/network permissions when diagnostic reads are added. Existing raw SystemErrors/SystemLogs data must not be exposed without the safe reader features.
