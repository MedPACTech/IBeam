# Blueprint: Package Communications (`IBeam.Communications*`)

## Objective

Guide AI to wire SMS/email providers while keeping domain rules in services.

## Compose

1. `IBeam.Communications`
2. Provider package(s):
- `IBeam.Communications.Sms.Twilio`
- `IBeam.Communications.Email.*` as needed

## Rules

1. Services decide when/why to send messages.
2. Communications providers only deliver.
3. Keep provider credentials in configuration/options.
4. Use templating abstractions rather than provider-specific payload logic in services.
