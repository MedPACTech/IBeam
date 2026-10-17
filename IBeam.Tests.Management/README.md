# Management foundation verification

Run `dotnet test IBeam.Tests.Management/IBeam.Tests.Management.csproj`.

In-process HTTP fixtures use explicitly named test authentication handlers; they are not production key/token verification. Tests demonstrate that customer/support/machine trust domains cannot substitute for each other, service guards reject direct unauthorized calls, deployment context is enforced, startup configuration fails closed, and IBeam service operations audit allowed and denied calls.

Tests have no cloud/database/secret prerequisites. Subsequent credential, Identity, diagnostics, persistence and multi-application features require their own meaningful tests before release.
