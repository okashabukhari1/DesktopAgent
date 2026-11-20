# Migration roadmap and Phase 2 implementation plan

Date: 2026-11-21, Asia/Karachi. **Proposal only. Phase 1 ends with documentation; Phase 2 has not begun.** Scope follows [the audit](PHASE-1-AUDIT.md) and [target architecture](../architecture/TARGET-ARCHITECTURE.md).

Complexity is relative: Medium = bounded subsystem adaptation; High = multiple contracts/native dependencies; Very high = desktop authority, privacy and failure recovery across native processes. These are engineering complexity estimates, not elapsed-time promises. Existing behavior is retained whenever sound; unavailable platform capabilities remain visible and truthful.

## Phase gates

| Phase | Work and dependencies | Acceptance criteria | Complexity |
| --- | --- | --- | --- |
| **2 — Avalonia Desktop UI** | User approval of Phase 1/backend decision; isolate portable services; explicitly qualify native Wayland; incrementally port shell/MVVM/views and cancellable approval surfaces. | Linux-only dependency graph; main window usable on GNOME native Wayland; selected backend proven; shared full/compact conversation; truthful unavailable features; settings/permissions/results/STOP UI behavior preserved; original WPF source/data untouched. Full checklist below. | High |
| **3 — Linux system integration** | Phase 2 host/contracts; XDG paths, Secret Service, desktop-entry discovery/launch, safe opener/editor argv, monitors, notifications/tray/autostart, capability-qualified external window metadata. File-security adapter is prerequisite to enabling retrieval. | Disposable real-user-profile tests for desktop-entry precedence/field expansion, app launch/identity, file/URL restrictions, keyring locked/unavailable, case-sensitive paths, symlink/hardlink/rename races, import preview/backups/rollback, startup opt-in and no-tray fallback. Unsupported Wayland window operations explain limits. | High |
| **4 — Linux Computer Use** | Phase 3 platform identity/capability/safety seams; approved desktop permissions; versioned worker contract; GI/D-Bus/PipeWire/input dependency qualification. Implement semantic observation before mutation. | AT-SPI provider tests on declared app matrix; strict IPC and response validation; child crash/hang/EOF recovery; portal deny/revoke and screen-lock tests; foreground/identity/expiry/password guards; visible STOP and user takeover proven; no input replay after uncertain dispatch; no restriction bypass. If safety evidence is insufficient, mutation stays unavailable. | Very high |
| **5 — AI integration** | Reuse existing provider/router/planner from the start; this phase qualifies it end-to-end with completed Linux capabilities. Requires Phase 3 credential adapter and Phase 4 only for input tools. | Fake-provider regression plus separately consented live inert tool probe; strict schemas/capability filtering; actual action results, bounded sequential loops, untrusted-content restrictions and cancellation verified; no provider credential leakage, invented success or duplicate mutation. Offline direct commands still work. | Medium–High |
| **6 — Permissions and safety** | Cross-phase hardening/acceptance of existing Guardian; safety is required in Phases 2–5, never deferred until here. Address audit races/provenance/import failure behavior. | Same invocation prepared and authorized across all entry points; workspace edits/policy Allow→Ask/Deny changes invalidate authority; per-action approval where required; OS consent cannot be replaced by model/user-policy Allow; denied/revoked actions dispatch nothing; old Deny rules preserved; uncertainty explained. | High |
| **7 — Advanced agent features** | Stable earlier phases; preserve then port DeveloperSupervisor/Claude/OpenCode scope with authenticated Linux IPC and previewed configuration changes; advanced additions need separate product approval. | Correct UID/session/peer handling and stale/spoofed event rejection; bounded redacted events; structured exit evidence only; pending permission expiry/cancel; unrelated hooks preserved/rollback tested; detection-only adapters labeled; no assistant authority over developer permissions. No terminal/Git/file-write feature implicitly added. | High–Very high |
| **8 — Packaging and release** | All prior acceptance gates; explicit supported Ubuntu/compositor/backend/app matrix, version pinning and dependency/license inventory. | Reproducible clean Linux build; linux-x64 publication with native dependencies audited; desktop identity/icons/launch; tested upgrade/uninstall preserving data; documented consent/keyring/worker setup; clean-machine runtime tests; no Windows binary prerequisites or undeclared XWayland fallback. | High |

## Exact Phase 2 execution order (after approval)

### 2.1 Establish preservation and build boundaries

1. Establish a real version-controlled baseline or backup: this supplied workspace is not a usable Git repository. Capture source/build/settings schema fingerprints and historical report inventory. Do not initialize/reset Git or move personal data implicitly.
2. Add a Linux-specific solution/build entry while keeping `DesktopAgent.sln` and WPF source available. Ensure Linux entry never loads WPF/Forms/WindowsDesktop/DevHook copy targets. Regenerate build intermediates in a platform-specific output path instead of sharing copied Windows assets.
3. Maintain an old-file → sole-owner map. Core remains `net10.0`; move Windows-only Infrastructure compilation units out of the Linux graph before retargeting the reusable Infrastructure. Preserve those source files and the existing Windows compilation graph/references while adding the separate Linux build entry. Avoid duplicate compiled services in either graph. Reusing the existing Windows graph is preservation, not implementing future Windows adapters; no fresh Windows build certification is claimed.
4. Add Linux-runnable behavioral tests by extracting the already existing portable parser/geometry/catalog/model/provider/router/policy/workspace cases. Leave native Windows fixtures in the old harness. Do not claim all 267 original cases are portable.

Acceptance: clean Linux build restores without Windows assets; no original source/functionality discarded; only one compiled owner for each shared service; portable checks run in isolated temporary storage.

### 2.2 Qualify the backend before porting all views

5. Pin matching Avalonia 12.1.6 package versions (or reverify a newer release explicitly at implementation time), use `net10.0`, and create only the minimal Desktop host needed for qualification. Native Wayland is explicitly selected; record backend/version in local diagnostics. Do not use default platform detection as proof of native Wayland.
6. Test the real GNOME session: main-window create/show/close/restore, GPU/EGL initialization, scale changes/multi-monitor handling, focused keyboard/IME/Urdu input, clipboard, modal/nonmodal dialogs, focus recovery and shutdown. Confirm native status through backend diagnostics and an independent connection/protocol observation rather than merely checking XDG_SESSION_TYPE.
7. Qualify frameless/transparent companion, drag/resize, topmost/position limitations, tray restore, notifications and AT-SPI roles/names/focus separately. Do not change accessibility settings silently. Headless rendering tests cannot certify these desktop behaviors.
8. If a required capability fails: document exact backend/version/compositor failure and retain a usable standard main window; seek a concrete product decision on reduced companion behavior or explicitly labeled XWayland compatibility. Do not silently switch or claim full native parity.

Acceptance: recorded native-Wayland runtime evidence; each requested feature is supported, explicitly degraded or blocked with evidence. This is the first implementation checkpoint before large view-port effort.

### 2.3 Connect the existing services and presentation state

9. Create one Generic Host composition root in Desktop, reusing existing singleton conversation, provider, router, policy, catalog, workspace/project services and character state. Replace WPF dispatching/approval/navigation with small presentation contracts. Keep unavailable Linux integrations out of the active tool capability set; do not register success-returning stubs. Until Phase 3 supplies Secret Service, compose an explicitly unavailable credential store, disable cloud requests and show Not tested/unavailable rather than Connected. Scripted providers belong only in an explicit test/preview profile; no plaintext credential fallback.
10. Evolve Windows-shaped identity/observation contracts only as needed to compile shared orchestration. Preserve opaque typed references and backward-compatible stored DTOs; no fake HWNDs. Add explicit unavailable diagnostics for unimplemented platform capabilities.
11. Extract ViewModels from code-behind by feature, retaining current commands, copy, validation, action cards, partial results, ambiguity choices and subscriptions. RelayCommand/ObservableObject are reusable; no new MVVM framework is required merely for migration.
12. Add test-profile settings/path injection before runtime tests. Phase 2 must not auto-import, modify, reset or overwrite actual old user data. If final XDG persistence is deferred to Phase 3, use the explicit isolated test profile for qualification and mark production persistence unavailable until its policy is decided.

Acceptance: DI resolves without native Windows classes; full and compact UI share the same state; unsupported actions return a truthful unavailable result before any effect; data tests run only in disposable profiles.

### 2.4 Port feature surfaces in reviewable increments

13. Port themed main shell/Home/navigation/character control first, preserving identity and state model rather than introducing new branding.
14. Port Assistant and compact interaction panel together: command entry, conversation rows, structured tool/source cards, application/project choice replay, progress and continuously reachable STOP. Keep one conversation instance.
15. Port Applications and Workspaces/editor/pickers using existing catalog/CRUD/validation/progress interfaces. Linux launch/window actions remain capability-gated pending Phase 3; a saved workspace never implies its Linux effects succeeded.
16. Port Projects, metadata settings and Files surface. Existing bounded file intelligence remains unavailable until its physical-access guard has a tested Linux adapter. Preserve stored editor/terminal preferences while making unsupported launch behavior explicit.
17. Port Permissions, Activity, provider/general/file/developer/computer-use settings and onboarding. Approval is asynchronous/cancellable: STOP/Escape/window close denies or cancels safely. Resolve stale permission copy using the actual available capabilities. Make partial settings saves and immediate button effects explicit or redesign transaction handling with targeted regression checks.
18. Port Developer display as observation-only/unavailable until authenticated Linux relay integration exists; keep read-only vs permission decisions distinct. Move Windows Diagnostics into clearly legacy diagnostics rather than presenting it as a Linux test.
19. Port optional companion/tray/notification lifetimes after backend qualification. Keep normal window access if tray/topmost is unavailable; preserve app shutdown and dispose/unsubscribe behavior. Computer Use visibility/reference-count guards and STOP UI remain intact even while Linux mutations are disabled.

Acceptance: every original surface accounted for; no duplicate provider/router/conversation or silently removed feature; UI clearly distinguishes stored metadata, unavailable effects, partial outcomes and successful operations. No Linux input automation implemented in Phase 2.

### 2.5 Verify and stop at the Phase 2 gate

20. Run portable regression, fake-provider/schema/routing/approval/cancel tests, ViewModel/navigation/subscription tests and an Avalonia real-session startup/shutdown/accessibility smoke test. Check settings in test profiles only, compact/full context sharing, prompt cancellation, hidden/closed views and lost worker/session prerequisites.
21. Record PASS/FAIL/BLOCKED/NOT RUN for native session, tray, clipboard, IME, accessibility, transparency, topmost and companion placement. Record API-provider live tests separately; never treat mocked responses as a live-provider pass.
22. Deliver the Phase 2 change inventory and acceptance report for approval before proceeding to Phase 3. If any capability blocks product requirements, provide the concrete failing test and options; do not conceal it with XWayland or placeholder results.

## Test ownership and release criteria

Unit/contract tests run without a desktop or real credential: provider protocol, routing/schema, policies/preparation races, complete-turn context trimming, persistence/version/corruption, workspace order/partial/cancel, metadata/path comparison, retrieval budgets and strict worker framing/response states. Use existing cases as the starting point.

Real Ubuntu tests have explicit prerequisites and fixtures: app launch, Secret Service availability/lock, physical file access/link/race containment, authenticated IPC peers, native backend/window lifecycle, AT-SPI provider coverage, portal consent/revocation/PipeWire frames, input target/focus/user takeover, STOP and owned-child termination. Never label an omitted desktop test PASS because a build or fake backend passed. A release requires an explicit supported matrix and documented unsupported window operations.

## Phase 1 stop condition

Phase 1 delivers only the audit, target architecture, this roadmap, test report and diagnostic evidence. WPF, native Windows implementations, original Python backend and user data remain preserved. Await user approval before creating the Avalonia host or installing its packages.
