# IBeam.Management.Identity.Api
Read .agent/implementation-guide.md. Install only selected management auth routes. Never add the generic Identity controller assembly: it exposes public enrollment/recovery flows outside this scope.
Controllers delegate to services. JWT validation must use a dedicated audience, strict signature/lifetime checks and live membership/session validation. No cached authorization. Do not log request bodies, tokens, OTP codes, or secret-bearing models. Keep the optional adapter out of Management core and general API packages.

Support directory/invitation/PATCH routes are explicitly opt-in. Invitation acceptance is anonymous but verifies a canonical email-delivered one-time capability and existing-password identity where applicable in the service. Body logging is disabled and malformed models receive generic responses. It never returns a session, token, submitted password or invitation token.
