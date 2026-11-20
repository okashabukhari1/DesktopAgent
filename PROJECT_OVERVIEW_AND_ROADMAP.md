# Desktop Agent — Project Overview, Built Features, and Roadmap

Documentation snapshot: **2026-10-12**.

This document explains what we are building, what already exists, how the system works, what has actually been verified, and what still needs work. It is a project handoff document, not a declaration that every feature is finished.

## 1. The product we are building

We are building a **native Windows personal AI desktop companion** inspired by the interaction style of JARVIS: a persistent assistant that understands text requests, knows available applications and registered projects, organizes workspaces, observes supported Windows UI state, and performs controlled computer actions.

The intended experience is:

- A floating desktop character provides a persistent, lightweight presence.
- A full desktop application acts as the control center.
- The user requests tasks in natural language or uses direct commands.
- The AI selects structured tools rather than executing arbitrary code.
- Local services validate the request and enforce permissions.
- The app performs supported actions, checks results, and explains what happened.
- The user can deny permissions, interrupt input, or press STOP.

Examples of the intended product direction:

```text
Open Chrome.
Start my programming workspace.
Open my project in VS Code.
Find where authentication is configured in this project.
Open Notepad and type Hello World.
Select all the text and copy it.
Scroll down in Chrome.
Switch to Chrome, press Ctrl+F, and type Python.
```

These examples are not all equally verified. The status tables below distinguish working demonstrations from implemented-but-unaccepted behavior.

### What this product is not

It is not fictional superintelligence, an unrestricted remote-control tool, or a shell that lets an AI run anything it wants. It does not automatically authorize actions because a website, document, source file, or another AI says to do them.

The current product is **text-first**. Earlier development explored voice and a “Hey, Jon” wake phrase, but the later master specification deliberately removed active voice functionality from scope. Microphone listening, wake-word detection, speech recognition, and text-to-speech are not current supported features.

## 2. Rules for interpreting project status

| Label | Meaning |
| --- | --- |
| Implemented | Code and the relevant product surface exist. This alone does not prove live acceptance. |
| Automated tests passed | The recorded test cases executed successfully for a particular revision. |
| User-confirmed working | The user reported or showed the behavior on the actual Windows desktop. |
| Live verified | A specific runtime action has both an actual result and supporting evidence. |
| Patched, retest pending | A correction was applied, but the latest real desktop scenario has not passed yet. |
| Experimental | A subsystem exists but is not fully accepted or production-ready. |
| Deferred / not implemented | A future direction, not a present capability. |

Different development phases reused “Part 1,” “Part 2,” and other numbers. The original desktop/controller/workspace/application phases are not the same numbering series as the later audit, AI-brain, project-intelligence, and computer-use phases. This document therefore describes features first and uses the existing reports as historical evidence.

Older README/report statements such as “no keyboard input” or “observation only” describe earlier revisions. They must not be used as the current computer-use capability list. Likewise, an old successful smoke test does not prove a newer patch works.

## 3. Where the project and app are located

Project folder on this machine:

```text
C:\Users\TESLA\Desktop\Data Backup\Data Backup\Okasha\Websites\Agent
```

The most recent app folder used in the current repair work is:

```text
artifacts\part73-chat-keys\
```

Its executable is:

```text
C:\Users\TESLA\Desktop\Data Backup\Data Backup\Okasha\Websites\Agent\artifacts\part73-chat-keys\DesktopAgent.App.exe
```

The corresponding private Python worker is packaged beside it:

```text
artifacts\part73-chat-keys\python-worker\worker.py
artifacts\part73-chat-keys\python-worker\windows.py
artifacts\part73-chat-keys\python-worker\validation.py
artifacts\part73-chat-keys\python-worker\requirements.txt
```

Earlier artifact folders, including `part72-editor-repair` and `part73-repair`, are historical builds. Do not accidentally launch multiple versions and assume they all contain the latest repairs. The latest Part 7.4 fixes were applied to Python source and the `part73-chat-keys` packaged worker without rebuilding its EXE.

### Opening and restarting

The app can initially display only its character/tray presence, depending on saved startup settings and onboarding state. Use the Desktop Agent tray menu:

```text
Open Desktop App → Assistant
```

For a full restart, use **tray → Exit**. Closing only a Settings or Assistant window is not the same as stopping the application and its private worker.

The existing `--open-app` startup argument requests the main desktop window. That option does not bypass a Windows application-control block.

After Python worker edits, restart the app so an already-running Python child does not keep the old module loaded.

## 4. Technology and solution structure

The Windows application uses C#, .NET 10, WPF, dependency injection, service interfaces, Windows APIs, local JSON persistence, and local logging. Some tray/dialog integration uses Windows Forms. The Windows app target is `net10.0-windows`.

The controlled Python lane uses Python and `pywinauto==0.6.9`. The interpreter used during recorded testing on this machine was:

```text
C:\Users\TESLA\AppData\Local\Python\pythoncore-3.14-64\python.exe
```

Recorded worker health reported Python 3.14.7. This is a tested machine configuration, not a promise that every Python version is supported.

```text
Agent/
├── src/
│   ├── DesktopAgent.App/             WPF UI, startup, tray, composition
│   ├── DesktopAgent.Core/            Models, contracts, parsing, domain rules
│   ├── DesktopAgent.Infrastructure/  Windows/API/persistence implementations
│   ├── DesktopAgent.DevHook/         Experimental developer event relay
│   └── DesktopAgent.PythonWorker/    Private validated computer-use worker
├── tests/
│   └── DesktopAgent.Tests/           C# executable regression harness
├── artifacts/                      Published app folders and test fixtures
├── README.md
├── ARCHITECTURE.md
├── PART*_IMPLEMENTATION_REPORT.md   Historical implementation evidence
└── PROJECT_OVERVIEW_AND_ROADMAP.md   This document
```

### Responsibilities

| Component | Responsibility |
| --- | --- |
| Core | Character state, application/workspace/project models, interfaces, policy contracts, action/result types, deterministic command parsing. |
| Infrastructure | Application discovery, Windows control, AI provider communication, routing, persistence, project/file intelligence, UI awareness, native/Python execution selection. |
| App | Desktop pages, compact Assistant, character, settings, onboarding, permissions, action display, STOP controls, startup and service registration. |
| PythonWorker | A private, bounded execution lane for supported Notepad/Chrome operations. It is not a second AI planner. |
| DevHook | An optional developer-supervision relay, not a general terminal execution tool. |
| Tests | Automated contracts, safety rules, parsers, persistence, routing, provider fixtures, and explicitly selected integration checks. |

## 5. End-to-end architecture

```text
User text / explicit UI action
        |
        v
Exact command parser OR AI conversation/planner
        |
        v
Strict typed tool router
        |
        v
Local permissions + task scope + risk checks
        |
        v
Validated deterministic service / computer action executor
        |
        +--> Existing application/workspace/project/file services
        |
        +--> Native Windows/UIA backend
        |
        +--> Existing private Python worker
        |
        v
Actual Windows operation
        |
        v
Dispatch and verification result
        |
        v
Assistant action card and accurate summary
```

The golden rule is **AI proposes; local policy decides; a validated tool executes**. A model response cannot directly grant itself Windows access.

### Direct commands versus AI requests

Exact supported commands can use deterministic routing without spending a provider request. Natural-language or multi-step tasks use the configured AI provider. AI tool calls use typed services directly; they are not converted into arbitrary shell strings.

The full Assistant and compact character panel share conversation state. Context is bounded and contains explicit application/project references and actual tool results. Ambiguous references should produce a choice or safe failure, not a guess.

## 6. What has been built

### 6.1 Native desktop companion and control center

Implemented surfaces include the floating character, compact interaction panel, full desktop app, system tray, context menus, settings, onboarding, logging, persistence, startup support, always-on-top options, and monitor-aware positioning.

The character is a WPF vector control, not a requirement for an external animation service. It reflects useful states such as idle, thinking, working, and attention. Asset folders leave room for later visual improvements.

The desktop navigation includes Home, Assistant, Applications, Projects, Workspaces, Permissions, Activity, and experimental Developer functionality.

The most recent chat-input change implements:

```text
Enter        → send the message
Shift+Enter  → insert a new line
Ctrl+Enter   → retained send compatibility
```

The handler uses `PreviewKeyDown` so the multiline TextBox cannot consume Enter first. Auto-repeat does not send duplicate messages. The change applies to the shared full/compact Assistant view. Its build/publish succeeded; a separate recorded live keyboard acceptance test is not available.

### 6.2 Application discovery and window management

The application catalog combines installed and running application information from Windows sources such as Start Menu shortcuts, registered application paths, installed-desktop registry entries, Shell AppsFolder packaged entries, and visible running applications.

Implemented behavior includes normalized search, aliases, deduplication, ambiguity-aware resolution, caching, icons where available, favorites, running state, application opening/focusing, and window operations.

The existing controller supports operations such as close, minimize, maximize, restore, move, resize, layouts, monitor targeting, and opening permitted resources. Success and failure are returned as structured results.

Support is constrained by Windows foreground rules, app behavior, permissions, and process integrity. Discovery does not imply reliable automation of every installed application's internal controls.

### 6.3 Workspace manager

A workspace is a saved launch/layout definition. It can contain ordered applications, files, folders, HTTP/HTTPS URLs, monitors, layouts, launch delays, and a final focus target.

Implemented features include create/edit/delete/duplicate, local persistence, validation, ordered execution, progress, cancellation, per-step results, session outcomes, and partial-failure reporting.

Removing a workspace removes its definition, not its referenced files, programs, or browser data. AI workspace launches must also satisfy permissions for embedded child actions.

### 6.4 Cloud AI brain and provider panel

The app has an `IAiProvider` abstraction and an OpenAI-compatible provider implementation with streaming/tool-call parsing, cancellation, timeouts, connection testing, model fetching, and structured error handling.

Settings configure provider, API base URL, model, credential, and connection status. OpenRouter has been used during actual runtime tests. Other providers can be used where their endpoint/model/tool-calling behavior is compatible; dedicated native adapters for every vendor are not implied.

Credentials are kept in Windows Credential Manager and scoped to the provider URL, not stored as plaintext keys in settings JSON. Fetching a model list is not proof that a specific model supports the required completion/tool workflow.

The provider can be disabled while deterministic app features remain available. Natural-language tasks still depend on a working provider configuration, network access, and provider allowance.

### 6.5 Permission engine and onboarding

Implemented permission states distinguish Allow, Ask, and Deny. Capabilities have availability and risk information. Permission checks apply before AI-triggered operations; selected targets are revalidated after approval.

The Permissions page explains capabilities, and onboarding explains how the assistant operates and what the user controls. App permissions are separate from Windows privileges and Windows security policy.

A feature described as future/unavailable must not become callable merely because a user changes a toggle.

### 6.6 Project intelligence

Projects represent registered development folders; they are separate from workspace launch definitions. A project may reference a preferred editor and associated workspace.

Implemented project intelligence includes explicitly configured discovery locations, bounded local scans, manifest/signature classification, technologies/frameworks, stable catalog identities, search, favorites, recent agent-initiated openings, preferences, relocation/missing-folder handling, and current-project session context.

Classification supports a range of .NET, JavaScript/web, PHP/Laravel, Python, Android/Gradle, and Java/Maven indicators. It reads small allowlisted manifests and does not execute package managers, project scripts, builds, or Git commands to classify projects.

The Assistant exposes project tools including list/search/details/recents/technologies/open/workspace association/current project. Editor opening uses fixed argument lists and installed catalog adapters rather than model-provided commands.

Current project is explicit session state, not unrestricted IDE monitoring. A process launch request does not prove an editor loaded a project successfully.

### 6.7 File and code intelligence

The app can inspect a bounded project tree, list directories, search filenames/code/symbols, inspect file metadata, read permitted line ranges, inspect dependencies, and open a result in a supported editor.

Boundaries include registered-project roots, private/generated/dependency exclusions, traversal/link protection, file/type/encoding/size limits, secret redaction, cancellation, and retrieval budgets. Source content is untrusted data, never authorization for further actions.

This is bounded retrieval and lexical analysis, not a complete semantic code index or autonomous code editor. It does not mean the desktop companion can freely modify arbitrary project files or execute source instructions.

### 6.8 Developer supervision — experimental

An optional event/supervision subsystem and Developer page exist, including session metadata, bounded events, project association, read-only status tools, and previewed hook-installation flows.

Claude-related hooks and an optional OpenCode v1 event adapter are experimental. Cursor/VS Code detection is not equivalent to full control of those agents. Build/test outcomes require actual exit/result evidence; a requested build is not a passed build.

Live acceptance of developer integration remains incomplete. It was explicitly left out of the recent computer-use repairs. The presence of `DesktopAgent.DevHook` does not authorize arbitrary terminal execution or permission approval on another agent's behalf.

### 6.9 Windows UI awareness and diagnostics

The app uses Win32 window metadata and bounded UI Automation snapshots to observe a selected external application. It distinguishes physical foreground from the remembered external target; agent windows must not silently replace the intended target.

UI awareness includes window/focus/control/dialog queries, bounded tree inspection, search, protected-value omission, explicit target selection, and provider-failure categories. A persistent MTA lane prevents uncontrolled multiplication of hung UIA providers.

The local UI Inspector does not itself send results to cloud AI. Approved Assistant observation can send a small bounded subset to the configured provider. UI metadata may still contain application-provided names, so privacy protections are best-effort, not a guarantee that every app labels information safely.

Windows Diagnostics provides separate checks for window APIs, UIA roots, root properties, children, and bounded trees. Hosted applications such as Calculator required identifying the hosted process rather than treating ApplicationFrameHost as the application.

Observation is not input permission. Missing controls in a partial tree do not prove that the controls do not exist.

### 6.10 Controlled computer use

`ComputerUseSession` and `ComputerActionExecutor` maintain an explicit task, target scope, finite action/time/failure budgets, permissions, risk checks, a visible Assistant/action indicator, STOP readiness, and input-monitor readiness.

Native UIA supports implemented pattern paths such as invoke, toggle, selection, value/range changes, expand/collapse, and selected fallbacks. Those paths are not universally runtime-accepted across all Windows apps.

Keyboard shortcuts and active-window scrolling are distinguished from element-specific actions. Keyboard actions should not fail solely because a previously observed `ui:N` became stale after typing.

The hybrid backend selects one execution lane before preparation. It does not automatically fall back to another input backend after potentially dispatched input.

## 7. The Python computer-use worker

The Python worker is a private child of the existing C# app, communicating through bounded NDJSON on standard input/output. It is not a separate server, remote listener, unrestricted scripting tool, or replacement for the C# permission system.

The transport correlates request IDs, validates allowed actions, rejects malformed/oversized data, limits one active request, discovers/configures the interpreter, bounds output/timeouts, and terminates its own child on interruption where required. A timeout does not authorize repeating input.

Supported protocol actions currently include health, window lookup/focus/info, focused-control/editor inspection, text typing, fixed keyboard shortcuts, scroll-region inspection, and bounded scrolling.

### Notepad lane

The worker validates the Notepad executable/process identity, HWND, PID, process creation identity, target editor runtime, visible/enabled state, explicit non-password/writable evidence, foreground, and relevant popup/desktop conditions.

Modern Notepad exposes a Document editor and can rename an untitled tab to typed content. The implementation therefore uses the selected tab's runtime identity during typing rather than interpreting every automatic title change as a different document. Classic Notepad retains its title guard.

Ctrl+A can verify selection against the document range. Ctrl+C observes a clipboard sequence update without reading or claiming exact copied content. Other dispatched shortcuts do not automatically become verified simply because keys were sent.

### Chrome lane

Chrome handling is limited to validated installed Chrome process paths and intended windows. Ambiguous targets must be clarified or pinned using a locally issued reference.

Scrolling targets an actual page Document region, validates the pointer hit-test, and dispatches a small bounded wheel amount. The AI does not supply raw screen coordinates. Injected pointer movement must not be mistaken for physical user takeover.

Chrome typing is restricted to its browser-owned Find field after a connected verified Ctrl+F workflow. This is not general webpage-form or address-bar typing.

## 8. What is actually working, and what is not yet finished

| Feature/scenario | Latest evidence/status |
| --- | --- |
| Standalone Python Notepad typing | User-run PASS: Hello World visibly appeared; CompletedAndVerified returned. |
| Fixed C# → Python Notepad typing | User-run PASS with visible text and verified result. |
| Existing policy/executor → Python Notepad test | Recorded PASS through the real app policy test. |
| Real Assistant → Python → Notepad Hello World | User screenshot showed full text and CompletedAndVerified. |
| Ctrl+C | User-confirmed clipboard update; exact clipboard contents were not verified. |
| Chrome window focus | User-confirmed working. |
| Chrome wheel scrolling | User-confirmed visible movement before the latest verification patch. |
| Chrome scroll verification | Previously returned InputDispatchedUnverified despite movement; new accessibility-geometry fallback applied, live retest pending. |
| Chrome Ctrl+F | User screenshot showed CompletedAndVerified. |
| Chrome Find full Python query | Latest live test FAIL: only P entered, then ChromeFindEditorUnavailableOrAmbiguous. Latest pinned-field patch awaits retest. |
| Chrome Escape closes Find | Implemented narrowly; latest real Assistant closure test pending. |
| Enter sends / Shift+Enter newline | Implemented and published; separate recorded live key acceptance pending. |
| Complete type → select → copy acceptance matrix | Individual behavior has evidence; the exact full matrix is not fully recorded as passed. |
| STOP during a live multi-action sequence | Automated safety coverage exists; latest requested live test not recorded as passed. |
| User takeover during a live sequence | Automated safety coverage exists; latest requested live test not recorded as passed. |
| General Chrome webpage typing | Unsupported. |
| Screenshot-based vision / OCR control | Not implemented in this work. |
| Voice / wake word / speech output | Deferred; not active product functionality. |
| Part 8 | Not started. No accepted Part 8 implementation is claimed. |

## 9. Important problems we investigated and repaired

### App appears to run without a main window

Saved startup behavior can show only the character/tray after onboarding. Opening the desktop app through the tray is distinct from launching its background presence. We also checked actual running process paths to avoid testing an old or duplicate copy.

### Windows blocks an unsigned build

A recorded Windows crash event showed `DesktopAgent.App.dll` blocked by Application Control with `0x800711C7`, before the app could write a startup log. This is different from a hidden tray window or an AI error.

Successful compilation does not establish publisher trust. Building another unsigned EXE does not guarantee Windows will permit it. Security settings were not changed or bypassed during diagnosis. A particular historical blocked launch does not prove every later launch is currently blocked.

### Provider and schema failures

Earlier problems included an invalid API base/model-page URL, provider HTTP errors, schema mismatch, and network resolution failure. Structured schema and endpoint errors were investigated separately from input safety failures.

An HTTP 402 provider response was observed in a historical log. That is provider-side evidence, not a current balance check, and is separate from an EXE startup problem. Never publish API keys, raw credential values, or sensitive provider payloads in diagnostics.

### Notepad editor omitted from observations

The native tree contained the RichEdit Document, but its visibility property was unknown. A VisibleOnly filter omitted it, causing the AI to choose a non-editor control.

The repair exposed a narrowly scoped read-only Notepad candidate from the actual bounded tree while retaining unknown flags. Python still had to independently prove visibility, eligibility, and safe editor identity before input. Unknown state was not globally rewritten as safe.

### Notepad stops after “He”

The title guard rejected modern Notepad's automatic untitled-document rename after the first characters. A selected-tab runtime pin allowed expected renaming while preserving window/process/editor checks. The later user screenshot demonstrated full Hello World and verified completion.

### Shortcuts fail with stale elements after typing

Shortcuts previously went through native element resolution using cached snapshot/element identity and an unchanged title. Window-level keyboard preparation was separated from element-specific UIA preparation and routed through the supported Python lane.

A related regression also exposed that comma-separated requests such as “Open Notepad, type …” were not classified as computer tasks. The parser boundary was repaired.

### Planner stops after observing or after the first action

The planner now checks supported requested typing, scrolling, and Ctrl+A/C/F/S steps, with bounded continuation for missing steps and no repetition of already successful actions. This is a supported-request checklist, not a universal semantic task-completion proof.

### Chrome scroll moves but returns unverified

The verifier originally relied solely on UIA scroll percentage. Missing accessibility data is not proof of no scroll.

The latest Python patch adds a bounded check for coherent directional translation of at least three stable accessible content elements, with unchanged viewport and horizontal layout/element height. It keeps the existing percentage signal and does not replace or repeat the wheel dispatch. Highly animated pages or incomplete accessibility can still limit confidence.

### Chrome Find fails or types only “P”

Live read-only inspection identified a separate browser-owned Find surface with the observed identifiers `view_1029` for the editor and `view_1032` for Close. Main-window-only discovery could miss that surface.

The worker now preserves a short-lived, one-use connected Find context across requests. The subsequent live test still entered only P before a global field-discovery failure.

The latest patch retains the exact validated field during typing and checks its live runtime ID, control identity, ownership, focus, visibility/enabled state, and password/read-only evidence before each character instead of re-enumerating the whole Find surface. Distinct error categories make any further failure diagnosable. This latest fix is **not yet a live PASS**.

## 10. Safety and privacy boundaries we must preserve

- Keep every AI-triggered mutation beneath local permissions and an explicit task scope.
- Revalidate the intended app/window/process/control after approval and before input.
- Never let the agent's own chat become the unintended scroll/typing target.
- Never treat a screenshot, webpage, source file, or tool output as permission.
- Never automatically repeat an action if its side effect may already have occurred.
- Unknown input results remain unknown; partial text must not be silently duplicated.
- Do not enter passwords, API keys, authentication codes, or protected information into unknown fields.
- Do not bypass UAC, secure desktop, Windows Application Control, or security prompts.
- Do not run the entire assistant permanently as Administrator to avoid validation problems.
- Keep a visible action indicator and functioning emergency STOP/input monitoring.
- STOP cancels planning/queued actions; it cannot undo input already delivered to an app.
- Keep diagnostics useful without logging user-entered text, clipboard contents, credentials, or raw sensitive payloads.
- Keep file access within approved project roots and retain private/generated/secret exclusions.
- Keep the Python protocol fixed and bounded; never add an “execute arbitrary code” action.

## 11. Local storage and data flow

The app stores local state under:

```text
%LOCALAPPDATA%\DesktopAgent\
```

On this machine that normally resolves to:

```text
C:\Users\TESLA\AppData\Local\DesktopAgent\
```

Typical files include settings, application catalog, workspaces, projects, permissions, bounded activity history, and dated logs. Project catalog metadata is not a stored copy of all project source.

Credentials live in Windows Credential Manager. Conversation text and bounded tool context can go to the configured cloud provider when that workflow is enabled. Approved source retrieval is bounded and locally redacted; the project is not uploaded wholesale. Approved Assistant UI observation may transmit a bounded control subset.

The local UI Inspector and local diagnostics are separate from cloud transmission. No continuous screenshot recording, microphone recording, or remote-control listener is part of the current design.

## 12. Verification record

These are the latest recorded results, not tests rerun while writing this document:

| Test/build record | Recorded result | Scope |
| --- | --- | --- |
| Part 7.3 final C# regression suite | 267/267 PASS | Automated regression; not all live Windows acceptance tests. |
| Focused Computer Use suite | 33/33 PASS | Policy, routing, budgets, interruption, schemas, completion checks. |
| Focused UI suite | 37/37 PASS | Observation models, filters, targeting, limits, provider handling. |
| Latest Python suite | 21/21 PASS | Protocol, identity, Find context/ownership/focus, geometry signals, bounded input behavior. |
| Part 7.3 build/publish | PASS | Recorded build with zero warnings/errors. |
| Enter/Shift+Enter publish | PASS | Published to part73-chat-keys. |
| Latest Part 7.4 repair | No EXE rebuild | Source and packaged Python updated; live retest pending. |

The latest Python regression specifically simulates global Find discovery becoming unavailable after the first character while the pinned field remains valid. It also checks safe interruption when the live field loses focus after Py. A simulated test does not replace real Chrome verification.

## 13. How to test the current repair

Restart the same current app first so the private child loads the latest packaged worker. Run one Assistant request at a time. Approve permissions yourself where required. Leave mouse/keyboard untouched during input except when deliberately testing STOP or takeover.

### Part 7.4 acceptance checklist

| Test | Assistant request | Evidence required |
| --- | --- | --- |
| A | Scroll down in Chrome by 3 steps. | Visible downward page movement plus accurate verification/result status. |
| B | Scroll up in Chrome by 3 steps. | Visible reverse movement; no repeat of a potentially dispatched wheel action. |
| C | Switch to Chrome, press Ctrl+F, and type Python. | Find bar contains the complete query, not just P; correct target retained. |
| D | Press Escape in Chrome. | The Find bar actually closes, with honest result evidence. |
| E | Open Notepad and type Hello World. | Full requested text appears; existing Notepad behavior remains intact. |
| F | STOP during a safe multi-action task. | Remaining actions do not run after cancellation; earlier effects remain accurately reported. |

Also retain wrong-window, stale-reference, and user-takeover acceptance scenarios from Part 7.3. Use disposable documents and harmless public pages, not unsaved important work, authentication screens, or sensitive forms.

If a task stops after partial input, record the exact category and visible text before restarting. Do not repeatedly resend it into the same field without understanding whether the next task replaces or appends text.

Automated Chrome keyboard input through the development computer-use tool was stopped by its URL-enforcement safety check. That limit was not bypassed through another automation route. For now, those live browser-action tests are user-run through the actual Desktop Agent, with read-only diagnostics where available.

## 14. What we plan to build next

### Priority 1 — finish the current repair, not a new phase

1. Retest the complete Chrome Find query after the pinned-field patch.
2. Verify scroll-down and scroll-up evidence on real pages.
3. Verify Escape closes the actual Find surface.
4. Recheck Notepad as a regression.
5. Execute live STOP and takeover scenarios.
6. Record exact PASS/FAIL/NOT RUN evidence and remaining limits.

Do not declare Part 7.4 complete from compilation or mocks. Do not start Part 8 as a substitute for finishing these tests.

### Priority 2 — strengthen reliability within the existing architecture

Potential follow-up work, subject to an explicit approved scope:

- Better distinction between unsupported accessibility, changed focus, changed owner, protected fields, and timeouts.
- Version-tolerant supported Find identification without accepting arbitrary webpage inputs.
- Clearer user messages for dispatched-but-unverified input.
- More complete supported multi-step completion accounting.
- Better multiple-window selection and target continuity.
- Mixed-DPI/multi-monitor and app-restart acceptance coverage.
- A clearer startup/open-app experience and reduced confusion between old artifact folders.

These are roadmap items, not features claimed as completed now.

### Priority 3 — release readiness

Before treating the app as a normal distributable product:

- Complete interactive acceptance of important screens and existing features.
- Establish trusted signing/distribution and test actual Windows policy compatibility.
- Package dependencies clearly, including the Python worker and interpreter requirements.
- Provide predictable versioned release folders and upgrade/restart instructions.
- Document supported providers, applications, workflows, and privacy boundaries.
- Exercise failure, cancellation, unavailable-provider, unavailable-Python, and missing-package paths.

No certificate purchase, trust-policy change, installer, or release automation is implied by this documentation task.

### Longer-term direction — deferred, not authorized implementation

The larger product could eventually gain richer workspace/project context, expanded controlled app adapters, improved developer supervision, more polished companion behavior, or a separately approved optional voice adapter.

Screenshot/vision/OCR assistance, general browser-form automation, terminal/build execution, file mutations, Git writes, and other consequential capabilities require their own design, permission model, scope, and acceptance tests. They are not automatically included in “JARVIS-inspired.”

**Part 8 has not been started, and this document does not invent its specification.**

## 15. Guidance for the next developer or AI agent

Before changing the product:

1. Read this overview and the latest relevant repair report.
2. Inspect current source and the actual running EXE path.
3. Preserve existing app, catalog, workspace, policy, and worker architecture.
4. Identify the exact failure stage before modifying validation or adding recovery.
5. Do not trust older build/test counts as current live acceptance.
6. Distinguish window-level actions from element-level actions.
7. Keep one execution lane per prepared action; no post-dispatch fallback/retry.
8. Keep user-controlled approvals, STOP, and takeover handling.
9. Update source and the intended packaged Python worker together where required, and verify matching files.
10. Ask the user to restart when a loaded worker must be replaced; do not pretend file edits reload it automatically.
11. Mark every real test PASS, FAIL, or NOT RUN with evidence.
12. Stop at the requested phase boundary. Do not start Part 8 without authorization.

## 16. Related documentation

These reports are historical snapshots. Later code and user-provided runtime evidence can supersede individual statements; they should not be silently treated as one fully accepted release.

- [README](README.md) — setup and earlier feature descriptions.
- [Architecture](ARCHITECTURE.md) — earlier architecture/audit snapshot.
- [AI brain and companion report](PART2_IMPLEMENTATION_REPORT.md).
- [Project intelligence report](PART3_IMPLEMENTATION_REPORT.md).
- [File and code intelligence report](PART4_IMPLEMENTATION_REPORT.md).
- [Experimental developer supervision report](PART5_IMPLEMENTATION_REPORT.md).
- [UI awareness report](PART6_IMPLEMENTATION_REPORT.md).
- [Raw Windows diagnostics report](PART6_2_DIAGNOSTIC_REPORT.md).
- [Controlled-action foundation report](PART7_IMPLEMENTATION_REPORT.md).
- [Runtime/schema repair report](PART71_RUNTIME_REPORT.md).
- [Python integration report](PART72_IMPLEMENTATION_REPORT.md).
- [Multi-action and Chrome repair report](PART73_IMPLEMENTATION_REPORT.md).
- [Current scroll/Find verification repair report](PART74_IMPLEMENTATION_REPORT.md).

## 17. Bottom line

We have built a substantial Windows desktop-agent foundation: native companion UI, application and window tools, workspaces, cloud AI tool calling, explicit permissions, project/file intelligence, bounded UI awareness, and a private Python execution lane.

Real Notepad typing is demonstrated. Clipboard-update detection, Chrome focus/Ctrl+F, and visible Chrome scrolling have user evidence. **The complete Chrome Find typing workflow still needs a live pass after the latest repair, and the remaining safety/acceptance matrix is not complete.**

The next job is to finish and verify those existing workflows—not replace the architecture, pretend every feature works, or jump to a new phase.
