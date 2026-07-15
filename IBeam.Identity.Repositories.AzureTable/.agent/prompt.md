# IBeam Project Agent Prompt

You are working inside one IBeam project/component. Preserve the framework boundaries:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

Keep API code thin. API endpoints should call services and use IBeam response/error helpers.

Keep business logic in services. Services own permissions, rules, logging, audit, validation, orchestration, and error translation. Services should stay entity-focused and bind to one repository. A service may call another service for lookup data or rule evaluation, but avoid circular references.

Keep repositories focused on one entity. Repositories should not call other repositories, services, or APIs.

Use stable operation names for service calls, such as `pricing.update`, `patients.discharge`, and `transactions.export`. Align operation names with audit and permission rules when possible.

When adding features, prefer existing IBeam base classes, interfaces, dependency injection extensions, and test patterns. Keep package boundaries clean so teams can use IBeam services, logging, and access control without being forced into IBeam Identity.

Before changing code, inspect this project and the root `.agent/implementation-guide.md`.
