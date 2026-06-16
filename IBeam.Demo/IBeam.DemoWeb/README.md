# IBeam Demo Web

React/Vite developer console for exploring the IBeam Identity API.

## Run

```powershell
npm install
npm run dev
```

The Vite dev server runs on `http://127.0.0.1:5174`.

## API Proxy

Requests to `/api` are proxied to `https://localhost:7181` by `vite.config.ts`.
Update the proxy target if the demo API launches on a different port.

## First Milestone

- Wire the demo API to `AddIBeamIdentityApi(...)`.
- Run Azurite with `UseDevelopmentStorage=true`.
- Complete registration or OTP login.
- Inspect tokens, sessions, tenant role grants, permission mappings, and Azure Table records.
