# Hairscope Agent

A small Windows background agent that detects the **hardware snapshot button** on a
wired trichoscopy probe and relays the press to the Hairscope web app, so pressing
the button on the probe triggers an image capture in the browser.

Currently supports the **Firefly DE334T** ("Trichoscope 4K", `VID_21CD` / `PID_0834`).
It's designed to grow into other devices via `Config/devices.json`.

---

## Why it works this way

The DE334T is a standard UVC camera whose button is a standard **UVC still-image
button event** on the VideoControl status-interrupt endpoint (`0x87`). Two hard
constraints shaped the design:

1. **The camera is exclusive** — only one app can stream it at a time. The browser
   holds that stream on the capture page, so the agent cannot also open the camera.
2. **The button only appears on the USB bus while the camera is streaming.**

The vendor's own SDK (`SnapDll.dll` + a kernel driver) only supports their older
analog-capture product line and does not bind to the DE334T, so it never worked here.

The solution is an **out-of-band USB tap**: the agent observes the `0x87` status
packets on the USB bus using the **USBPcap** kernel driver — *without* opening the
camera. While the browser streams the probe, the button events flow on `0x87`, the
agent sees them, and relays a `snap` message to the web app over a localhost
WebSocket. No camera conflict, no vendor DLL, no bespoke kernel driver.

**Port note:** the WebSocket defaults to `127.0.0.1:8891`. It used to be `8787`,
but Firefly's own bundled Windows application also binds `8787` on startup, so any
PC that has both the Firefly software and this agent installed would fail with an
`HttpListener` "conflicts with an existing registration" error. `8891` was picked
as an arbitrary port unlikely to collide with either vendor tooling or common dev
ports (3000/8080/etc.) — there's nothing else significant about the number itself.

```
Probe button ─► USB 0x87 status event ─► USBPcap (kernel) ─► HairscopeAgent
                                                                  │  observes; never opens the camera
                                                                  ▼
                                              localhost WebSocket  ──►  Web app (useHardwareCaptureButton)
                                                                            │
                                                                            ▼
                                                         browser captures the 4K image
```

---

## How it runs

The same binary runs two ways:

- **Console (development):** run the exe directly to watch output live.
- **Windows Service (production):** installed once, runs at boot as **LocalSystem**
  (which has the rights USBPcap needs — no UAC, no manual launching).

**Lazy capture:** the agent keeps only a lightweight localhost WebSocket listener
open at idle. It starts the (heavier) USBPcap capture **only while the web app is
connected**, and stops it when the last client disconnects.

---

## Requirements

- Windows 10/11 (x64).
- **USBPcap** — **bundled with the installer** (signed by the USBPcap author) and
  installed silently if not already present. A reboot after install is required
  for its driver to attach. (Manual download: https://desowin.org/usbpcap/)
- For building: **.NET 8 SDK**.
- The web app must be reachable and, on the capture page, streaming the probe.

---

## Build & run (development / console)

From `src/HairscopeAgent`:

```powershell
dotnet build -c Release
```

Run the built exe **as Administrator** (USBPcap needs elevation in console mode):

```powershell
cd bin\Release\net8.0-windows
HairscopeAgent.exe
```

Expected output:

```
Configured device: Firefly DE Wired Series DE334T (VID_21CD&PID_0834)
[ws] listening on ws://127.0.0.1:8891/
[ws] allowed origins: http://localhost:3000/1/2, https://localhost:3000/1/2
Agent ready. USB capture starts only while the web app is connected.
```

Open the capture page in the browser; you'll see `[ws] web client connected`, then
`[usbtap] listening ...`. Press the probe button → `>>> BUTTON PRESSED -> broadcasting snap`.

---

## Publish & install (production / service)

Publish a self-contained single-file build, then compile the installer:

```powershell
# from src/HairscopeAgent
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish

# from installer/
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" HairscopeAgent.iss
```

Output: `installer/Output/HairscopeAgentSetup-<version>.exe`.

Running the setup (as admin):
- silently installs **USBPcap** if it isn't already present (bundled, signed),
- installs the agent to `Program Files\Hairscope\Agent`,
- registers and starts the **`HairscopeAgent`** Windows Service (auto-start, LocalSystem),
- prompts for a **restart** (needed for the USBPcap driver to attach).

After install the service runs at every boot with no user action. The clinician just
opens the capture page and uses the probe button.

Service management:

```powershell
sc query HairscopeAgent
sc stop HairscopeAgent
sc start HairscopeAgent
```

---

## Configuration — `Config/devices.json`

```json
{
  "agent": {
    "webSocket": {
      "host": "127.0.0.1",
      "port": 8891,
      "allowedOrigins": ["http://localhost:3000", "https://localhost:3000"]
    }
  },
  "brands": [
    {
      "brand": "Firefly",
      "series": [
        {
          "series": "DE Wired Series",
          "connection": "USB",
          "devices": [
            { "model": "DE334T", "productString": "Trichoscope 4K",
              "vid": "0x21CD", "pid": "0x0834", "enabled": true,
              "capabilities": { "button": { "method": "uvc-still-trigger" } } }
          ]
        }
      ]
    }
  ]
}
```

- `agent.webSocket.allowedOrigins` — **add your production web-app origin here** for
  deployment. Empty list = accept any origin (dev convenience only).
- Add new devices under `brands`/`series`/`devices` with their `vid`/`pid` and
  `enabled: true`.

---

## Web integration

The web app connects via the `useHardwareCaptureButton` hook
(`src/hooks/useHardwareCaptureButton.ts` in `hairscope-clinic-web`), which:

- connects to `ws://127.0.0.1:8787` (override with `NEXT_PUBLIC_HW_CAPTURE_WS_URL`),
- fires the capture on a `{ "type": "snap" }` message,
- is a silent no-op if the agent isn't running — the on-screen Capture button is
  unaffected.

Note: browsers treat `127.0.0.1` as a trustworthy origin, so an HTTPS page can
connect to the insecure `ws://127.0.0.1` without a mixed-content error (Chromium).

---

## Security

- The WebSocket server binds to **127.0.0.1 only**.
- Handshakes are rejected unless the browser **Origin** is in the allowlist.
- The agent only **observes** USB traffic; it never opens the camera or modifies
  device state.

---

## Troubleshooting

- **Logs:**
  - Console/dev run: `%LOCALAPPDATA%\Hairscope\agent.log`
  - Service (LocalSystem): `C:\Windows\System32\config\systemprofile\AppData\Local\Hairscope\agent.log`
- `no USBPcap interfaces found` → USBPcap not installed, or needs a reboot after install.
- `Couldn't open device` / access errors → not elevated (console mode) — run as admin,
  or use the service (which runs as LocalSystem).
- `no 0x87 status packets seen` → nothing was streaming the camera; open the capture
  page (or Windows Camera app) so the probe is active.
- Button detected in the agent but the browser doesn't capture → check the browser
  DevTools console for `[hw-capture] connected`, and confirm the page's origin is in
  `allowedOrigins`.

---

## Project structure

```
src/HairscopeAgent/
  Program.cs                     host bootstrap (console or Windows Service)
  AgentWorker.cs                 lifecycle: WS server + lazy USB capture
  Config/
    devices.json                 device + WebSocket configuration
    AgentConfig.cs               config models + loader
  Devices/
    UsbDeviceScanner.cs          WMI VID/PID presence detection
  Buttons/
    IButtonWatcher.cs            button-source abstraction
    UsbStatusButtonWatcher.cs    out-of-band USBPcap 0x87 tap
  Server/
    SnapWebSocketServer.cs       localhost WS relay + origin allowlist
installer/
  HairscopeAgent.iss             Inno Setup: installs + registers the service
```

---

## Roadmap

- Support additional probes/devices via `devices.json`.
- Optional: open the USBPcap control device directly (drop the `USBPcapCMD.exe`
  child process) or, longer term, a bespoke signed driver to remove the USBPcap
  dependency.
- Extend the agent to other device features as needed.
