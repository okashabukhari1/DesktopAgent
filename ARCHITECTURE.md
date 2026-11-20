# Current architecture and audit

This is the existing four-project .NET 10 Windows/WPF application, repaired and extended in place through Part 3. `DesktopAgent.Core` owns models, contracts, character state, deterministic parsing, application resolution, workspace validation, and layout calculations. `DesktopAgent.Infrastructure` implements Windows discovery/control, JSON persistence, logging, workspaces, permissions, project intelligence, and the cloud AI adapter. `DesktopAgent.App` is the WPF shell and composition root. `DesktopAgent.Tests` is a dependency-free executable test harness.

## Project intelligence

ProjectProfile and WorkspaceDefinition remain separate, connected by optional IDs. ProjectDiscoveryService locally classifies allowlisted small manifests within explicitly selected locations; ProjectCatalog caches versioned metadata in projects.json with atomic persistence, stable identities and deterministic ambiguity-aware search. CurrentProjectContext is explicit session state, not IDE monitoring. Startup loads the catalog without a recursive scan.

ProjectTools expose eight typed functions through the existing router. OpenProject derives child workspace/application permissions and captures a per-invocation snapshot to reject changes during approval. ProjectOpenService launches a preferred workspace, then WindowsProjectEditorLauncher safely passes the selected folder/solution to a supported catalog editor using ArgumentList and no shell interpreter. No terminal or build commands are generated. Explicit Projects/Settings UI actions retain the existing direct-user interaction model.

Project variables are deliberately not implemented. Existing workspace resources retain their original validation/execution semantics. Workspace ProjectId feeds session context after a successful launch; opening an editor with the associated project path occurs in the OpenProject workflow. Refer to PART3_IMPLEMENTATION_REPORT.md for current verification evidence.

## Runtime paths and trust boundaries

- Startup: `App.xaml.cs` composes services, loads settings/policy/activity/catalog cache/workspaces, opens the character/tray, and refreshes the application catalog in the background. Optional subsystem failures are logged and contained where possible.
- Assistant direct text: `ComputerCommandService` parses with `ComputerCommandParser`, routes workspace commands to `WorkspaceService`, and other actions through `ComputerController`; activity records real results. Exact direct commands in Assistant chat also pass the Assistant policy; Applications/Workspaces page buttons remain explicit user actions.
- Applications: `ApplicationCatalog` combines cached and discovered Start Menu, registry, AppsFolder, desktop, and running entries. `ApplicationResolver` handles exact names, aliases, ambiguity, and missing apps. Windows implementations perform launch, focus, close, state, and layout operations.
- Workspaces: the UI edits definitions; `WorkspaceService` validates/persists them and manages launch sessions; `WorkspaceLaunchEngine` executes ordered steps, reports progress and partial failures, and supports cancellation. The validator performs launch-time syntax preflight even for previously saved malformed data.
- Cloud AI: `AssistantConversationService` calls the configured `IAiProvider` only when enabled and credentials are available. `OpenAiCompatibleProvider` streams an OpenAI-compatible response. `ComputerAgentToolRouter` validates tool arguments and checks Allow/Ask/Deny policy before an AI-triggered action. It uses AgentToolRegistry and typed tool implementations; AI tool calls never pass through the command parser. The full and compact UI share an in-memory conversation, structured reference context, bounded complete protocol history and cancellable sequential tool execution.
- Security/persistence: API credentials live in Windows Credential Manager under a target scoped to the provider URL, never in settings JSON. Settings, catalog, workspaces, policy, and bounded activity history are stored under `%LOCALAPPDATA%\DesktopAgent`. Logs are local. There is no remote access or unrestricted terminal/screen/keyboard control.

## Part 1 classification

| Component | Decision | Reason |
| --- | --- | --- |
| WPF shell, floating character, tray, deterministic commands | KEEP/FIX | Existing useful product surface; preserve behavior and contain startup faults. |
| Windows controller and application catalog | KEEP/FIX | Existing APIs are the correct boundary; repair alias ambiguity, discovery lifetime, and UI activity routing. |
| Workspace service, engine, editor and page | KEEP/FIX | Repair invalid saved definitions, stale app identities, cancellation/session race, missing-monitor fallback, and result/progress presentation. |
| Cloud provider, settings, credentials and permission policy | KEEP/FIX | Keep optional cloud API; fix settings save, endpoint-scoped keys, AI disable behavior, and failure reporting. |
| AI tool-to-text adapter | REPLACED | Part 2 introduced typed tools calling the existing controller/catalog/workspace services after validation and policy. |
| Voice/SAPI code, voice UI/settings, listening/speaking state | REMOVE | Text-only product decision; no active voice capability remains. |
| Placeholder UI-scale setting | REMOVE | It did not actually scale the UI. |
| Local models, screen understanding, autonomous tools, file intelligence | FUTURE | Out of Part 1 scope and not advertised as current capabilities. |

## Verification boundary

Part 3 builds and publishes with no observed warnings/errors; 123 automated cases compile. An earlier executed revision passed all 84 existing regression tests and 38 of 39 new cases (122/123); the remaining mixed-slash fixture assertion was corrected. Final reruns and expanded WPF smoke were blocked by Windows Application Control (0x800711C7). Earlier smoke attempts exited 0, but those do not certify the latest revision or interactive/live-provider/IDE checks. See PART3_IMPLEMENTATION_REPORT.md for current evidence and explicit pending manual scenarios; Part 1/2 reports are historical snapshots.
