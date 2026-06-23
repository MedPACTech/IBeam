# IBeam.Ai

Core agent tooling contracts and MCP models for IBeam-backed applications.

## Purpose

`IBeam.Ai` contains contracts and models shared by the AI package family. Use `IBeam.Ai.Services` for MCP protocol orchestration and `IBeam.Ai.Api` for ASP.NET Core endpoint wiring.

Use this package when you need:

- agent tool definitions
- agent execution context
- JSON schema helpers
- MCP response models and error codes

Most host applications should reference `IBeam.Ai.Api`, which brings the service and core packages transitively.
