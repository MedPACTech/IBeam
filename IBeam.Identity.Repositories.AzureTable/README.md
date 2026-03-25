# IBeam.Identity.Repositories.AzureTable

Azure Table Storage provider for IBeam identity store contracts.

## Narrative Introduction

This package connects identity orchestration to Azure Table persistence. It wires ASP.NET Core Identity + ElCamino Azure Table stores, registers IBeam store abstractions, and includes schema initialization so hosts can start with minimal persistence setup.

## Features and Components

- DI extension:
  - `AddIBeamIdentityAzureTable(IConfiguration)`
- Azure Table option binding and validation (`AzureTableIdentityOptions`)
- Identity store registrations for:
  - users
  - tenants and memberships
  - tenant roles and user-role assignments
  - permission role-mapping store (`IPermissionAccessStore`)
  - OTP challenges
  - external logins
  - auth sessions
- schema management services:
  - `IIdentitySchemaManager`
  - hosted schema bootstrap

## Dependencies

- Internal packages:
  - `IBeam.Identity.Services`
- External packages:
  - `ElCamino.AspNetCore.Identity.AzureTable`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.App` framework reference

## Configuration

Primary section:
- `IBeam:Identity:AzureTable`

Includes connection-string fallback resolution across `IBeam:*` and `ConnectionStrings:*` keys.
