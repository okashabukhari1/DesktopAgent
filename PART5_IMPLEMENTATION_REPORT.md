# Part 5 Implementation Report

Status: implementation built and **184/184 automated tests passed**, but **acceptance NOT COMPLETE**. Windows Application Control blocks the unsigned desktop launch; Claude Code was not detected. No live Claude/OpenCode configuration was modified. No Part 6 work was started.

## Architecture

Core typed contracts → bounded event normalizer → DeveloperSupervisor channel/session manager → activity, character, UI and eight read-only AI tools. The existing controller, project catalog, file intelligence, policy and conversation architecture remain in place. Adapters have independent lifecycle/status methods. No shell-execution tool was added to our assistant.

Studied [Coucou's relay](https://github.com/Louis-CFM/coucou/blob/main/windows/hook/src/main.rs), [Windows identity checks](https://github.com/Louis-CFM/coucou/blob/main/windows/hook/src/win.rs) and [MIT license](https://github.com/Louis-CFM/coucou/blob/main/LICENSE). Our implementation is original .NET code. No branding, character, sounds or artwork was copied. Relevant design ideas: a small local relay, finite deadlines, merge/preview installation and human-only decisions.

## Claude Code Integration

Based on the [current official hooks reference](https://code.claude.com/docs/en/hooks). Command hooks receive stdin JSON. Configured events: SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, PostToolUseFailure, PermissionRequest, Notification, Stop and SessionEnd. Stop records a finished response, not a finished task. SessionEnd records termination, not proof that requested work succeeded. Local Windows CLI sessions only; no WSL/cloud support or transcript reading. Native deny rules still apply to hook responses.

## Hook Installation

Settings → Developer Tools → Preview Install/Repair or Preview Remove. An explicit confirmation is required. Validate bounded JSON and hook structure; reject duplicate keys, invalid/linked paths and known concurrent edits. Merge exact handlers, preserve other hooks/settings and additional matcher-group properties. Preview exposes only our changes, not the entire potentially sensitive config. Create a timestamped exact-byte backup before modification, preserve existing access ACLs on backups/replacement, validate the replacement and rename from a same-directory temporary file. Removal matches exact generated handlers, not a broad command substring. Repair is designed to be idempotent. Backup files may contain original secrets and must remain private.

The first installer test failed with Access denied. Persistence was revised to the existing same-directory rename pattern with ACL preservation. **The final automated installer backup/preservation/idempotence/removal roundtrip passes.** Optimistic hash checks detect known edits, but are not a cross-application transaction lock: perform installation with other settings editors closed.

## Local Communication

`agent-dev-hook.exe` plus its runtime files ships beside the app. A length-prefixed JSON envelope uses a user-SID-derived, interactive-session-scoped pipe name. Both ends use [CurrentUserOnly](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0); both verify the peer's Windows session through pipe process-ID APIs. FirstPipeInstance prevents a second app from initially sharing the endpoint. Four async server instances; no HTTP listener.

Limits: 256 KiB frame/input, 300 ms connect, two-second ordinary exchange/read, 45-second pending decision, finite relay watchdog. Empty stdout and exit 0 preserve native handling on missing app, malformed data, timeout, busy endpoint or failure. No persistent permission updates/input rewriting. A direct app-closed relay probe passed with empty stdout/exit 0 in under two seconds; this is not a live Claude end-to-end test.

## Developer Sessions

Independent source + opaque SHA-256 session identity keys. External session/tool-call identifiers are hashed before persistence. Track start/last/end times, observed state, recent timeline, files and commands. Maximum 64 sessions, 128 recent events per session. Active recovered sessions become Disconnected; pending approvals are never restored. Unknown-session activity/results are rejected until a start/snapshot is observed. Terminal sessions reject late non-start events. States are last observed telemetry, not live process heartbeats.

## Project Association

Choose the longest matching registered canonical project root for cwd. Unknown projects remain unregistered and cannot receive bridge approvals. Never auto-create projects or change current-project context. Reject a session being silently rebound to another root/catalog identity. Project catalog changes may require restarting/resuming the coding session to re-associate.

## Events

Actually emitted from Claude: SessionStarted, Working, ToolRequested, CommandRequested, FileRead, FileModified, FileCreated (only structured Write response type=create), CommandCompleted, CommandFailed, PermissionRequested, PermissionApproved/Denied (explicit user decision), WaitingForUser, TurnCompleted and SessionCompleted (session termination only).

OpenCode emits SessionStarted, Working, TurnCompleted/WaitingForUser and SessionFailed. No implemented file deletion, session cancellation, global filesystem change, arbitrary terminal, semantic task-success or separate BuildStarted/TestsStarted events. Some enum states reserve future vocabulary but are not advertised as observed capabilities. Duplicate tool events and uncorrelated results are rejected.

## Permission Integration

Supported Claude PermissionRequest replies only. Eligible plain recognized build/test commands, or physically validated project Read requests, can produce expiring Allow Once/Deny cards. All decisions require an explicit human click even when policy is Allow. Pending requests are cancelled when the relay disconnects, session ends/resumes, app stops or the deadline expires. Reused IDs are rejected; policy/project identity/file boundary are checked again at click time. Unseen edit/write diffs and unsupported operations remain in the native prompt. No always-allow feature or LLM approval tool.

## Policy

Added Developer.Observe, ReadFile, ModifyFile, RunBuild, RunTests, RunCommand, NetworkAccess and InstallPackage descriptors. Observe defaults Allow; execution-related grants default Ask. Existing unrestricted terminal/Git execution capabilities remain unavailable. Deterministic classifier rejects destructive/elevated/credential/registry/download/package-install/destructive-Git/process-kill/dynamic/compound/unknown commands from bridge approval. Arguments require native review. Builds/tests are High risk because repository scripts can execute code and restore/access network. Policy denials never become allows. Our bridge declining participation is not represented as a native denial.

## Build/Test Detection

Recognizes common dotnet/npm/pnpm/yarn/gradle builds and dotnet/npm/pnpm/pytest/gradle tests. PreToolUse means Requested, not Running. A correlated structured exit_code=0 means Succeeded; nonzero or PostToolUseFailure means Failed. A successful tool envelope without a process exit code remains Unknown. Native Bash response schemas do not always supply exit_code. No guessed pass counts, console parsing, full output, error counts or file/line diagnosis. Different tool versions may therefore provide only requested/unknown status.

## File Activity

Read/Edit/Write result hooks track validated project-relative paths. Write is Created only with explicit response evidence; otherwise Modified. No automatic file content reads or watcher. Source/private paths, traversal, ADS, UNC/device paths and linked ancestors are rejected. Bridge Read approval reuses the Part 4 physical/hard-link path guard without reading bytes. Counts cover the bounded recent event window, not the full session or all filesystem writes. Shell-driven file changes cannot be inferred from these hooks.

## AI Tools

get_developer_sessions, get_developer_session, get_developer_activity, get_session_files, get_session_commands, get_pending_developer_permissions, get_build_status, get_test_status. Strict one-field typed schemas, bounded results and explicit ambiguity. Retrieve only on demand. No event feed is automatically injected into every request. Raw developer tool data is omitted from subsequent-turn history/reference context. Once developer data is retrieved, unrelated actions are blocked locally; event text cannot grant permissions. Developer questions without retrieved evidence receive an explicit unverified-data answer.

## Developer UI

Developer navigation/page, session selection, recent grouped counts/timeline, deterministic summary and permission cards. Home Developer Activity with View; compact companion shows View Session. Settings includes integration state refresh, opt-in supervision, retention, notification toggle and explicit installation/removal previews. Existing project/file/workspace/assistant pages remain. Source compiles; actual rendered interaction is unverified because Windows blocks launch.

## Character

One arbiter combines existing assistant state and developer state. Developer permissions take priority, followed by assistant attention/work, brief result reaction, developer work and idle/sleep. Added brief success/error indicators using our existing vector character; a cancelled one-shot timer restores normal state. Responses finishing are not evidence of successful task fulfillment. STOP still only cancels our own assistant operation and queued tools.

## Activity & Notifications

Debounced structured activity summaries rather than a record/notification for every file. Optional companion messages for permission, session termination and command failure. Pending cards stay accessible in Developer/compact UI. Native Windows toast registration and toast-click routing are **not implemented**. No notifications or external messages were sent during development.

## Cursor

PATH executable detection only. Reviewed [Cursor's supported hooks](https://cursor.com/docs/hooks), including distinct failure/permission response behavior. No hooks were installed and no sessions/permissions observed. It is not falsely reported as integrated/connected.

## OpenCode

Optional original read-only v1 plugin compatible with the [documented plugin event interface](https://opencode.ai/docs/plugins/) and [official v1 SDK schemas](https://github.com/anomalyco/opencode/blob/dev/packages/sdk/js/src/gen/types.gen.ts). It sends only session identity/cwd/start/busy/idle/error through the fixed relay; no prompts, messages, source, output, environment or permission decisions. Bounded 64-ID map/16-event queue/one relay child; child timeout only terminates that spawned relay, never OpenCode. Installation refuses differing existing files; removal verifies exact content and backs it up. No existing OpenCode configuration is rewritten. Session-less file.edited events are intentionally not attributed. v2 plugin APIs unsupported. OpenCode was detected locally, but no plugin was installed or live-tested.

## VS Code

PATH detection only. Its [public task process events](https://code.visualstudio.com/api/references/vscode-api#tasks) would support a future explicit extension. No extension, diagnostics feed, task observer, private protocol or scraping was added. Terminal support is limited to coding-tool structured events; no global terminal capture.

## Security

Untrusted bounded JSON, duplicate/depth/type/identifier/path checks, enum validation and failure isolation. Discard prompt/transcript/source/stdout/stderr/environment fields before persistence/UI/cloud; redact command/tool summaries and hash opaque external IDs. Unknown commands and credential-bearing data cannot authorize actions. Same-user code can fabricate telemetry: local pipe identity is not cryptographic proof of a real Claude operation. Approvals are never autonomous. No screens, OCR, keylogging, credential scraping, unrestricted shell, arbitrary input, autonomous writes or Git operations.

## Performance

256-slot channel with explicit rejection/backpressure; 100 accepted incoming attempts/second limit; four pipe listeners and at most eight in-memory pending cards. 500 ms UI/persistence debounce; no idle process polling or screenshot loop. Persistence retains at most 2,048 events/64 start records and 8 MiB JSON with atomic temporary-file replacement. Retention choices 7/30/90 days, default 30, pruned at startup/events. Bounded counts can omit old activity and floods can drop events. Idle CPU and interactive responsiveness have not been measured.

## Tests

Added 19 test cases: malformed/missing/duplicate/unknown/oversized data, unsafe paths/secret summaries, risk classification, independent projects/sessions, correlated file/results, actual exit semantics, human decisions/reuse/policy changes/cancellation, stress bounds, recovery, eight typed tools/ambiguity, character priority, installer preservation/backup/repair/removal/races, frame validation, actual current-user pipe, OpenCode mapping/plugin ownership and developer retrieval action isolation.

First revision: **180/181 passed**; installer preservation returned Access denied. After revisions, the sandboxed suite passed **183/184**; its secure named-pipe test was denied by the restricted execution environment. Approved normal-user execution passed that isolated pipe test, then the **complete final suite passed 184/184** (165 existing + 19 new). No Windows security policy was disabled, changed or bypassed. Earlier Code Integrity events 3077/3033 confirmed assembly signing-level blocks; desktop launch remains blocked independently of the now-passing tests.

## Manual Tests

1 Detection: CLI/PATH diagnostic found OpenCode and VS Code; Claude/Cursor not found (limited detection, not installation proof).
2 Claude install preservation/backup: not live-tested; no live settings changed.
3 App closed: direct relay probe passed; actual Claude flow unverified.
4 Registered project session start: automated source case, live unverified.
5 Two projects: automated source case, live unverified.
6 File activity: automated source case, live unverified.
7 Permission attention: earlier automated case passed; UI unverified.
8 Deny: earlier automated case passed; actual Claude unverified.
9 Allow Once: earlier automated case passed; actual Claude unverified.
10 Build: synthetic structured evidence tested earlier; real command unverified.
11 Tests: synthetic evidence tested earlier; real command unverified.
12 Assistant state query: typed tool case passed earlier; live API unverified.
13 Changed files query: read tool implemented; live API unverified.
14 Test outcome/Unknown: earlier automated case passed; live API unverified.
15 Completion: response/session distinction automated earlier; completion UI unverified.
16 Removal: final automated preservation/removal roundtrip passed; live Claude configuration removal unverified.

No claim that all 16 manual tests passed. Current desktop smoke launch is blocked with 0x800711C7. Finishing acceptance requires a Windows-authorized trusted build and a real local Claude installation/session; these require user/environment changes, not a code/security bypass.

## Regression

All 165 Parts 1–4 automated regressions passed in the final 184-case suite. Current solution compiles, but the requested real Chrome/PowerLens/editor/workspace/API scenarios are unverified. No exact-command, catalog, file-search, workspace, current-project or STOP system was replaced.

## Known Limitations

Not acceptance complete. Unsigned desktop assemblies currently blocked; trusted publisher signing is not supplied. Claude absent from detected locations; no live integration configuration changes made. Final automated installer roundtrip passes; live installation remains unverified. OpenCode v1 only, live plugin unverified; Cursor/VS Code detection-only. No actual process heartbeat, comprehensive change tracking, complete-session counts, task-success proof, missing exit-code inference, detailed errors/test counts, WSL/cloud integrations, Windows toasts or privileged command approval. Exact handler removal is tied to the configured relay path; moving an installed deployment requires removing its old integration before relocation. Existing API billing/configuration failures are outside this implementation and were not fixed by developer supervision.

## Build Result

Release solution builds with zero warnings/errors. Windows x64 framework-dependent publish succeeds and includes the app executable plus relay executable/DLL/deps/runtimeconfig. Requires the .NET 10 desktop/runtime prerequisites already used by the app. Initial sandboxed NuGet restore was network-blocked; approved normal dependency restore then succeeded. Publishing does not sign the app or remove Windows Application Control restrictions. App SHA-256: A7E2EDFBEDA79D07E1F10BFD986491386DFE88F979155913A4E76411DE620BE3; Authenticode: NotSigned. Final automated tests pass; desktop launch and live acceptance remain blocked.

## Files Changed

Core DeveloperModels, capabilities/settings/character contracts and arbitration; infrastructure Developer normalizer, supervisor, pipe protocol/server, Claude installer/adapters, OpenCode adapter and query tools; assistant retrieval/grounding protection; App DI/lifecycle, Developer views/navigation/home/compact card/settings and character visuals; relay project/solution/publish packaging; Part5Tests/test filtering; README and this report. No project source files were changed by the agent as a runtime feature. No Part 6 implementation.
