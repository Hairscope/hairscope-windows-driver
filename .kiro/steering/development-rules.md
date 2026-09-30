---
inclusion: auto
description: "General development rules for the Hairscope Windows Driver (Agent) - branch strategy, commit rules, and release process"
---

# Hairscope Windows Driver — Development Rules

## Branch Strategy

**Never write code or push directly to `dev`, `staging`, or `main`.** Every
change lands on a working branch first (e.g. `feature/*`, `fix/*`), then
flows through a strict promotion chain:

```
working branch → PR → dev → PR → staging → PR → main
```

- All new work starts on a working branch, off `dev`.
- A working branch PRs into `dev`. Confirm with the user before merging into
  `dev`.
- `dev` merges into `staging` only when a change is deliberately picked to
  ship next — not automatically on every merge to `dev`.
- `staging` merges into `main` only once everything on `staging` has been
  verified. `main` always mirrors `staging` at the moment of promotion.
- Never skip a step in the chain (e.g. never merge a working branch directly
  into `staging` or `main`).
- Commit frequently with descriptive messages on the working branch.

## Release Process (installer)

- `installer/HairscopeAgent.iss` has its own `AppVersion`, independent of any
  other repo's versioning. Bump it whenever a change should ship as a new
  installer.
- Building a new installer requires, in order:
  1. `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish` (from `src/HairscopeAgent`)
  2. Compile `installer/HairscopeAgent.iss` with Inno Setup (`ISCC.exe`)
- `Config/devices.json` is embedded into the published exe at build time
  (see `AgentConfig.LoadEmbedded()`). There is no loose, editable config file
  on an installed machine — editing the source `devices.json` does nothing
  until the agent is republished and reinstalled. Keep this in mind before
  telling a user a config change is "live."
- The agent's WebSocket port (`Config/devices.json` → `agent.webSocket.port`,
  default `8891`) must always match the default in
  `hairscope-clinic-web`'s `src/hooks/useHardwareCaptureButton.ts`
  (`NEXT_PUBLIC_HW_CAPTURE_WS_URL` fallback). If one changes, the other must
  change too, and both need a real release (rebuilt/reinstalled agent,
  redeployed web app) — not just a source edit — before they take effect.

## Before Committing

1. Build (`dotnet build -c Release`) and confirm no errors/warnings.
2. If the change affects the installer, bump `AppVersion` and rebuild it.
3. Confirm with the user before merging into `dev`.
