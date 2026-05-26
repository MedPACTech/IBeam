# IBeam.Utilities

Shared utility primitives used across the IBeam framework.

## Narrative Introduction

This package contains cross-cutting building blocks that do not belong to one domain package. It centralizes common concerns like exception shaping, audit-event structures, caching helpers, and token utilities so higher-level packages can stay focused.

## Features and Components

- exception infrastructure:
  - `ExceptionMiddleware` and related options
  - shared exception types/interfaces
- auditing primitives:
  - `AuditEvent`, `AuditAction`, `AuditEventBuilder`
- caching helpers:
  - `DataCache`
  - `NamespacedMemoryCache`
- utility models/helpers:
  - `BaseAppSettings` and `IBaseAppSettings`
  - `TokenGenerator`
  - validation and role helpers

## Dependencies

- Internal packages:
  - `IBeam.Repositories`
- External packages:
  - `Newtonsoft.Json`
  - `Microsoft.AspNetCore.App` framework reference
