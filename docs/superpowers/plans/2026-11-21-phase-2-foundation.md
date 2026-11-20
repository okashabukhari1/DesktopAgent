# Phase 2 Desktop Foundation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task. Stop at the native Wayland qualification gate if it fails.

**Goal:** Build and qualify an Avalonia 12.1.6 native Wayland foundation before porting the initial Desktop Agent shell.

**Architecture:** Add one independent net10.0 Desktop host alongside the preserved WPF app. Reuse Core directly. Leave the Windows Infrastructure graph unchanged until native qualification succeeds; no duplicate provider or conversation engine. Disconnected capabilities are explicit.

**Tech Stack:** Existing .NET 10, Avalonia 12.1.6 including Avalonia.Wayland, existing Microsoft.Extensions DI/hosting conventions, REA process Evidence for runtime checks.

**Spec:** User's Phase 2 attachment; docs/architecture/TARGET-ARCHITECTURE.md; docs/migration/PHASE-ROADMAP.md. The user's current instruction authorizes implementation of the gate-first prototype and shell; no new design approval is inferred necessary for that already specified work.

## Global constraints

- Do not edit/delete WPF source, old solution/build graph, original worker or user data.
- Native Wayland only; no X11 fallback, no default platform detection masquerading as native proof.
- Exact Avalonia 12.1.6 subject to actual package availability.
- Do not implement Phase 3, Linux Computer Use or a replacement AI pipeline.
- No Git initialization/commits/worktree possible without repository metadata. Use additive files and source fingerprints for reversible isolation.
- Only temporary/test-profile diagnostics; no existing settings/keyring reads or writes.

## Review focus

1. DISPLAY set while Wayland unavailable: fail rather than silently choose XWayland.
2. Package/native dependency missing: identify exact restore/startup failure and stop the gate.
3. Unknown backend: reported session type alone cannot pass qualification.
4. Cloud credentials unavailable: honest disconnected state, no fake replies or secret fallback.
5. Normal window close/restart: owned host disposes cleanly, no background worker/session residue.

### Task 1 — Baseline and gate prototype

Files: docs/migration/PHASE-2-BASELINE.md; src/DesktopAgent.Desktop/{DesktopAgent.Desktop.csproj,Program.cs,App.axaml,App.axaml.cs,QualificationWindow.cs}; scripts/phase-2 runtime helper only if needed.

- [x] Record Git unavailable, all original input hashes and existing solution/C#/Python failures.
- [x] Verify exact packages via NuGet and inspect installed package API/source documentation before using it.
- [x] Create minimal native-only host; no existing screens or Infrastructure reference yet. The explicit user-requested qualification prototype is the spike; later retained behavior gets contract tests.
- [x] Build independently and capture a bounded launch via REA. Record actual backend implementation and native Wayland protocol connection; never infer from XDG_SESSION_TYPE alone.
- [x] Test render, resize, compositor move, focus, keyboard/mouse, close and restart in the real session using supported UI tools/user interaction. Do not add global input automation.
- [x] Gate: all required prototype evidence established, or mark BLOCKED and stop before Task 2.

### Task 2 — Minimum boundaries and initial shell (only after gate)

Files: Desktop ViewModels, Views, local diagnostics/capability state; tests/DesktopAgent.Desktop.Tests; DesktopAgent.Linux.slnx.

- [x] Reference existing Core unchanged; first run portable behavioral integration checks and baseline available state.
- [x] Write/run failing meaningful tests for capability-denied submission/no fake transcript, navigation state and ephemeral theme settings before implementing ViewModels.
- [x] Implement DI, owned host lifecycle, sanitized exceptions and diagnostics with no personal profile load. Real credential/provider pipeline stays disconnected until a supported adapter exists; no fake business services.
- [x] Port initial shell/Home/Assistant/Settings retaining existing purple/orb identity and navigation intent; explicitly describe unavailable later-phase surfaces. Existing history is in-memory only and cannot be advertised as imported.
- [x] Add safe loading/empty/error/disconnected states, focused input behavior, normal-size responsive layout, theme foundation. No global hotkeys/tray/native integrations.
- [x] Run portable tests, independent build and actual native desktop smoke/interaction checks; record evidence.

### Task 3 — Review and delivery

- [x] Independently review final source/diffs (manifest since Git unavailable), security/data paths, gate evidence and tests.
- [x] Write the four requested reports and exact startup/build instructions with PASS/FAIL/BLOCKED/NOT RUN.
- [x] Verify all baseline input hashes, generated project dependencies and report links.
- [x] Save/export REA Evidence and close/reset this investigation. Stop after Phase 2; no Phase 3.

## Execution ledger

- Baseline: original solution fails with five errors (WindowsDesktop targeting plus stale Windows NuGet fallback path); C# harness blocked; Python suite 19 pass/two dependency errors.
- Ruling: execute inline in the current workspace because no Git repository exists; add only isolated new files. Do not initialize Git, move baseline files or seek repeated approval for the supplied gate-first plan.

- Native gate: REA native protocol/window implementation plus user-confirmed prototype interaction PASS before shell implementation.
- Task 2: initial eight behavioral tests RED→GREEN; existing Core integrated unchanged. Scope follows the current user initial-shell specification, not a claim all legacy screens migrated.
- Final: fixed unsanitized startup exception reporting — diagnostic regression RED→GREEN, suite 9/9.
- Final: fixed missing host-stop/UI bridge — initial REA idle-timeout SIGTERM/SIGKILL failure followed by explicit SIGTERM exit 0.
- Final verification: Linux solution zero warnings/errors; original 208 inputs unchanged; full requested reports/evidence saved.
- Ruling: retain Microsoft.AspNetCore.App for the existing Generic Host/DI/logging convention — avoids new framework packages; costs an additional ASP.NET Core 10 runtime prerequisite, documented.
- No Git finishing/commit/menu applies: no repository metadata. Preserve additive files in place; do not erase the implementation or initialize Git.
