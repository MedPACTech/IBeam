# Hubbsly Work Card Publishing

`publish-work-cards.ps1` validates IBeam work-card JSON and can publish it to the Hubbsly MCP endpoint.

The script is safe by default:

- It runs as a dry run unless `-Publish` is passed.
- It reads the key from `HUBBSLY_API_KEY`.
- It never writes the key to source files or output.
- It can list available MCP tools before publishing.
- It can publish one card at a time, an array of cards, or the raw JSON document.

## Payload Shape

Each card must include:

```text
boardKey
laneKey
title
description
workType
priority
sourceSystem
sourceKey
sourceUrl
sortOrder
```

The payload can be:

- an array of cards
- a single card object
- an object with `cards`, `Cards`, `items`, or `Items`

## Dry Run

```powershell
.\scripts\hubbsly\publish-work-cards.ps1 `
  -CardsPath .\scripts\hubbsly\work-cards.sample.json
```

## List Hubbsly MCP Tools

```powershell
$env:HUBBSLY_API_KEY = "<secret>"

.\scripts\hubbsly\publish-work-cards.ps1 `
  -CardsPath .\scripts\hubbsly\work-cards.sample.json `
  -ListTools
```

## Publish

When the MCP endpoint exposes exactly one likely work-card tool, the script can discover it automatically:

```powershell
.\scripts\hubbsly\publish-work-cards.ps1 `
  -CardsPath .\cards\ibeam-licensing-billing-hubbsly-cards.json `
  -Publish
```

If multiple likely tools exist, pass the exact tool name:

```powershell
.\scripts\hubbsly\publish-work-cards.ps1 `
  -CardsPath .\cards\ibeam-licensing-billing-hubbsly-cards.json `
  -ToolName work_cards_upsert `
  -Publish
```

If the tool expects a batch instead of one card at a time:

```powershell
.\scripts\hubbsly\publish-work-cards.ps1 `
  -CardsPath .\cards\ibeam-licensing-billing-hubbsly-cards.json `
  -ToolName work_cards_import `
  -PayloadMode CardsArray `
  -ArgumentName cards `
  -Publish
```

Use `HUBBSLY_MCP_ENDPOINT` to override the default production MCP endpoint.
