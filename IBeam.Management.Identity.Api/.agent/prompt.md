# IBeam.Management.Identity.Api
Read .agent/implementation-guide.md. Install only selected management auth routes. Never add the generic Identity controller assembly: it exposes public enrollment/recovery flows outside this scope.
Controllers delegate to services. JWT validation must use a dedicated audience, strict signature/lifetime checks and live membership/session validation. No cached authorization. Do not log request bodies, tokens, OTP codes, or secret-bearing models. Keep the optional adapter out of Management core and general API packages.
