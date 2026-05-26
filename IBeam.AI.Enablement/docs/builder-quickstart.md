# IBeam Blueprints for Builders: Tell Your AI Once, Then Build Fast

This guide is for builders using Codex/Clawbot/Copilot style tools who want results fast without learning all IBeam internals first.

## Step-by-Step

1. Open your AI coding tool in your app repo.
2. Paste the onboarding prompt from `examples/builder-onboarding-prompt.md`.
3. Let the AI ask onboarding questions one at a time.
4. Answer briefly; no architecture expertise needed.
5. Review the package recommendations the AI returns.
6. Tell the AI to proceed with setup and scaffolding.
7. Ask it to stop before major code writes so you can approve.
8. After scaffolding, ask it to run tests and list any architecture rule violations.

## What Good AI Behavior Looks Like

The AI should:

1. Ask questions before coding.
2. Recommend IBeam packages grouped by purpose.
3. Explain package choices in plain English.
4. Use service-layer ownership for business rules and role checks.
5. Avoid circular service dependencies.
6. Keep controllers thin and repository logic persistence-only.
7. Include a minimal test plan.

## Typical Questions You Should Expect

1. How do users log in? (password / OTP / OAuth / combo)
2. Is your app multi-tenant?
3. How do you want to store data? (EF / Azure Tables / other)
4. Do you need SMS/email providers? (Twilio etc.)
5. Where will you host the app?
6. Do you need strict roles/permissions?
7. What scale do you expect (users/tenants/throughput)?
8. Do you want default logging/error handling fallbacks?

## Quick Tip

If the AI starts coding before asking questions, stop it and say:

"Pause. Run the onboarding questionnaire first and recommend IBeam packages before code generation."
