# IBeam Anti-Patterns

1. Controller directly instantiates or queries provider store classes.
2. Service depends on concrete Azure Table or EF entity types.
3. Tenant ID is optional in write workflows that should be tenant-scoped.
4. Skipping default/fallback connection configuration documentation.
5. Embedding authorization decisions only in UI/API and not in service layer.
6. Reusing one DTO for API + persistence + domain logic.
7. Creating duplicate user identities for alias conflicts without merge strategy.
8. Hard-coding provider choices in business services.
