# Native Wayland qualification

Date: 2026-11-21, Asia/Karachi. **Result: PASS for the Phase 2 foundation gate.** This is actual Ubuntu desktop execution plus user interaction, not a build-only or screenshot-only inference.

## Target and method

Ubuntu GNOME Wayland session from Phase 1/baseline: Ubuntu 26.04.1, GNOME 50.1, native socket `/run/user/1000/wayland-0`, .NET 10.0.15 / SDK 10.0.115. Exact Avalonia packages 12.1.6 were restored and locked. The independent host uses `UseSkia().UseHarfBuzz().UseWayland()`; no fallback is configured.

REA 6.9.0 `capture_process_scenario` launched the selected built DLL with `/usr/bin/dotnet` and owned the bounded process. The native positive run supplied `DISPLAY=""`, `WAYLAND_DISPLAY=wayland-0`, `XDG_RUNTIME_DIR=/run/user/1000` and the actual session bus address. `WAYLAND_DEBUG=client` captured native protocol traffic for the prototype only. Subsequent interactive runs omitted verbose protocol tracing and never logged draft/typed text contents.

REA's inherited environment did **not** contain XDG_SESSION_TYPE: startup logs show null. That label is not backend evidence, and it was not forged to make the check pass. Independent baseline inspection established the desktop session; the selected window implementation and real Wayland protocol establish the application backend.

## Evidence ledger

All IDs below are retained in [the final REA bundle](../diagnostics/phase-2/rea-final-evidence.json).

| Evidence / test | Result | What was observed |
| --- | --- | --- |
| `ev_7045510ca1c75a4c5095bce4cf45f0c2453558cb16b2d5fa5b00729466a34b5f` | FAIL, corrected | First prototype exited 1: no text shaping configured; runtime requested UseHarfBuzz. Configuration error, not a demonstrated Wayland incompatibility. [Log](../diagnostics/phase-2/first-startup.log). |
| `ev_c6ab435d60a5fb5b3efe7403ddd954dda969605f049942a79ade59e61ed80108` | PASS | Corrected prototype opened `Avalonia.Wayland.WindowImpl, Version=12.1.6.0`; xdg_wm_base/get_xdg_surface/get_toplevel, app ID/title, configure/ack, EGL/dma-buf buffer attach and wl_surface commit; keyboard focus/activation; normal timed close, exit 0. DISPLAY disabled. [Protocol log](../diagnostics/phase-2/native-protocol.log). |
| `ev_47e81363321666543e60d599c6cb3d0f7796863a8f82734d549626ad433f618e` | PASS | Restarted prototype on native Wayland; real user resize changes 760×520 → 1869×1018 / 1920×1080 → original; focus, keyboard events, text length 1–7, confirmation button clicks, window close and exit 0. [Interactive log](../diagnostics/phase-2/interactive.log). |
| User response to prototype check | PASS, user-observed | User explicitly reported **All checks worked** for rendering, GNOME movement, resizing, mouse input and typing. Compositor movement is user-observed; no synthetic global-coordinate proof claimed. |
| `ev_e4b48bca006242a55ad0455fc012f893a48513052bd1d4127f9e97c7ac3bbca8` | FAIL (shutdown), corrected; interaction observed | Initial shell opened the native implementation and recorded page navigation. REA's default 30s idle timeout later sent SIGTERM, then SIGKILL because host stop was not yet bridged to UI shutdown. This capture is not a graceful-close pass. [Log](../diagnostics/phase-2/shell-interactive.log). |
| User response to shell check | PASS, user-observed | User reported **All shell checks worked** for navigation, Light/Dark appearance, draft retention, disabled Send, resizing/layout and closure. Captured navigation corroborates a subset; theme/draft/resize observations are the user's report, not independently instrumented claims. |
| `ev_2b17df90d95b08924971242c5c4df541cab220faccddc3bc94d7d343e26ca645` | PASS | Final rebuilt shell reopened native Wayland, credentialStorageAvailable=false, real Core state Idle. Explicit SIGTERM at 4s produced shell-closed, desktop-exit code 0, process-ended; no forced kill. [Log](../diagnostics/phase-2/shell-sigterm-final.log). |
| `ev_1b4e87e3ab32078361da2eb964b630c3eee4c00b2d16621420ce2528a9111178` | PASS, negative test | Invalid WAYLAND_DISPLAY with DISPLAY=:0 fails with sanitized AvaloniaWaylandException and exit 1, without opening a shell. Confirms no silent XWayland fallback. [Log](../diagnostics/phase-2/no-fallback-final.log). |

The first two successful prototype captures preceded all screen migration. Thus the mandatory native gate was satisfied before MainWindow/shell implementation.

## Coverage and limits

Window creation/backend/protocol submission, native focus, ordinary resizing, keyboard/text input, button activation, user-observed rendering/movement and restart were tested. Graceful normal prototype close and final host-signal shutdown were observed independently. User shell observations are identified separately from instrumented data; the early forced termination is preserved rather than presented as success.

No screenshot was captured; user visual confirmation complements protocol buffer submission and implementation type. Native TryGetPlatformHandle descriptor was null, so it was not used as proof. REA marked prototype captures truncated because rendered **terminal replay frames** exceeded their budget; raw terminal bytes/frames were fully retained (31,254 bytes/136 frames for native protocol and 3,479 bytes/36 frames for prototype interaction). Terminal replay is not a desktop screenshot. The final SIGTERM/negative captures were bounded and their selected results are preserved.

REA reports process sampling limits, unrecorded ambient environment, no network observation and no selected filesystem observation paths. Those residual Unknowns remain in the bundle. It observes `/usr/bin/dotnet` executable identity, not an atomic managed-DLL OS image binding; source/build/lockfiles and logs identify the selected DLL separately. No claim of complete machine/process/filesystem behavior is made.

**NOT RUN:** clipboard transfer, IME/Urdu composition, AT-SPI tree/screen reader, multi-monitor/fractional-scale transitions, transparency, frameless dragging, topmost/floating companion parity, tray/desktop notifications, portal capture/input, provider/cloud requests and packaging acceptance. They are not prerequisites to claiming the narrowly tested initial window/shell gate passed and must not be inferred from it. No global input automation, AT-SPI configuration change or Wayland restriction bypass occurred.
