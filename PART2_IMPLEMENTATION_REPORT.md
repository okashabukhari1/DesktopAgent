# Part 2 Implementation Report

Date: 2026-10-01. The implementation builds and publishes successfully. Part 2's definition of done is **not yet satisfied**: Windows Application Control blocks the unsigned assemblies, preventing automated execution, live provider verification, startup smoke verification, and interactive Windows checks. No Windows security setting was changed.

## AI Architecture

The existing solution was extended in place. Both Assistant surfaces share one in-memory conversation service. Exact direct commands use the retained parser without a cloud request. Other requests follow conversation → configurable cloud provider → structured call → registry/router → local policy → typed controller/catalog/workspace service → structured result → provider. AI tools never build or parse command strings.

The loop permits eight provider rounds, sixteen tool calls, and at most four calls in a single returned batch. Calls execute sequentially. Repeated calls are rejected, denied actions end the turn, and ambiguous actions stop for a user selection. Completed mutations are summarized from actual tool messages rather than a potentially invented provider success statement. General conversational and read-only summaries still depend on the configured model.

## Provider

The OpenAI-compatible Chat Completions adapter uses configurable provider, HTTPS base URL, model and timeout. It assembles streamed function arguments, rejects malformed/truncated streams, bounds response sizes, records actual returned usage, and allows one bounded retry for transient HTTP status failures. Configured request timeout is 10–180 seconds; response output is limited to 1,024 completion tokens. One provider architecture is implemented; individual third-party model compatibility still needs live verification.

Test Connection submits a real completion request with an inert connection-probe function to the configured model. It tests function calling without executing a Windows action. Fetch Models remains a separate operation and does not mark a model connected.

## Credential Security

Keys remain in Windows Credential Manager, scoped to a canonical provider URL that preserves case-sensitive API paths. The UI asks only whether a credential exists; saved secrets are not populated back into the input. Replace/delete actions remain available. Provider-controlled error text is not copied to UI or logs because it may contain secret fragments. HTTP redirects are disabled on the application's provider client.

## Natural Language

Scripted-provider tests cover app requests, compound side-by-side requests, programming-workspace requests, and “Minimize it” references. These tests compile but could not execute on this host. This is not evidence that a live model understood the examples; live natural-language verification remains open.

## Tool System

22 registered tools:

- list_applications
- search_applications
- get_running_applications
- open_application
- focus_application
- close_application
- minimize_application
- maximize_application
- restore_application
- move_application_window
- resize_application_window
- move_to_monitor
- list_workspaces
- get_workspace
- launch_workspace
- get_workspace_status
- cancel_workspace_launch
- create_workspace
- update_workspace
- open_folder
- open_file
- open_url

Schemas reject extra/duplicate/missing properties, incorrect types, invalid enums, unbounded strings/lists, invalid numeric ranges, relative paths, and unsupported URL schemes. Workspace creation/update accepts catalog applications and changes the application list through the existing validated save service. It does not launch a workspace or add arbitrary executable paths. Updating preserves other workspace settings/resources.

## Direct Service Integration

Application tools resolve a catalog identity and call IComputerController with a typed request. Queries use the catalog or application service directly. Workspace tools call IWorkspaceService. IComputerCommandService is used only for text supplied directly by the user; the AI router has no dependency on it.

## Application Control

Existing launch/focus/close/window implementations remain. New compiled tests check search, focus, missing applications, ambiguity and exact typed identities. Actual Windows operations have not been verified in this phase because the newly built assemblies cannot load.

## Workspace Control

Tools expose actual saved definitions and structured session outcomes, counts and bounded failed-step details. Embedded permissions for opening applications, changing windows, opening resources and closing applications are checked before an AI/Assistant workspace launch. The typed router checks that a workspace did not change while permission was pending. Context retains prior results so “What failed?” can inspect real failures. Stop propagates cancellation to the running workspace operation.

## Multi-Step Planning

The provider may return sequential actions; there is no separate autonomous planner. A scripted test covers opening Chrome/VS Code followed by left/right layouts and verifies that real tool results are supplied to the next provider request. Runtime verification is blocked.

## Conversation Context

Context tracks conversation ID, last referenced app/workspace, last tool and last actual result. Completed protocol turns are kept together and bounded to ten turns / 32,000 content characters. Tool-result data is bounded to 12,000 characters while preserving status and reference identities. UI entries are limited to 200 and remain in memory. New Conversation clears history, choices and references. No long-term memory or automatic full-machine context was added.

## Permissions

Every registered tool checks Allow/Ask/Deny before execution. Close defaults to Ask. Workspace modifications require the existing modification policy; their approval prompt does not offer persistent Always Allow. Assistant direct commands also apply these rules, including workspace child capabilities. Applications/Workspaces page buttons remain explicit user actions. The confirmation window is asynchronous, can be denied or allowed once, and closes on cancellation while keeping the Assistant Stop control usable.

## Assistant UI

The full Assistant has shared history, message bubbles, action cards, selectable application choices, Send, Stop, New Conversation, empty states and verified connection status. It hides raw JSON. The compact panel reuses this interface and opens the full Assistant without losing the conversation. Home shows actual connection/agent state, saved workspaces, recent activity and quick navigation.

AI onboarding embeds the same settings panel with provider/model/key, test connection and Skip for now. Continue saves configuration; Skip does not require AI. Settings exposes request timeout and removes the inactive history-saving checkbox.

## Character Integration

Existing state events and conversation change events drive Thinking, Working, Attention and Idle. Permission waits set Attention. The existing vector character adds restrained blinking, hover feedback and a brief completion pulse; animation preferences still apply. No high-frequency environment polling or voice state was added.

## Activity

Activity records request receipt metadata, provider failures/actual usage, tool start/results and policy outcomes. It does not persist hidden reasoning or the entire user conversation. Existing workspace notifications and character feedback remain.

## API Failure Handling

Missing setup, authentication errors, invalid-model error codes, network errors, timeouts and incomplete responses receive friendly messages. Provider error bodies are not exposed. Stop cancels the provider call and prevents subsequent tools. Tool execution has a one-minute deadline (three minutes for workspace launch), with cooperative cancellation through existing services; the overall turn is bounded to five minutes. Native synchronous Windows calls cannot be forcibly interrupted safely.

## Offline Fallback

Exact commands still use the deterministic path when AI is disabled or unavailable, without a provider request. Compound or contextual requests go to AI. An unpunctuated open request matching a saved workspace is routed to AI when enabled rather than blindly treated as an application name.

## Tests

The test harness contains 84 compiled tests: the existing 55 plus 29 Part 2 cases. New cases cover provider failures/usage/connection, strict validation, application and workspace tools, child permissions, editing, ordered actions, references, false-success prevention, repeated/denied actions, Stop, ambiguity selection and history limits.

Execution attempt: **BLOCKED**, not passed. Windows returned an Application Control policy error (0x800711C7) while loading DesktopAgent.Core.dll. The harness now reports the environment block explicitly. WSL and Docker/Podman were not available for isolated execution.

## Manual Windows Tests

None of the nine requested interactive scenarios passed or failed through the new app: they were not executed. The startup smoke attempt was also blocked while loading DesktopAgent.App.dll. No valid live-provider result was obtained. The prior Part 1 catalog/test results are historical evidence only and are not presented as verification of Part 2.

## Known Limitations

- A trusted signed build or a suitable development/test machine is needed to complete execution verification.
- Live provider, actual window operations, permission interaction, onboarding and compact-panel sizing remain unverified.
- Application controls cover catalog applications and supported visible windows, not arbitrary UI.
- Read-only summaries and language understanding depend on the configured model; code cannot guarantee the accuracy of unrestricted conversational text.
- Ambiguity selection executes only the originally requested ambiguous action. It does not automatically resume later actions from an interrupted multi-action batch.
- Workspace queries bound returned failure detail; the Workspaces page retains complete local launch results.
- Cooperative cancellation does not undo actions that already completed.
- Provider compatibility varies, including support for strict tools, stream usage and completion-token parameters.
- Conversation storage is session-only; activity is bounded local metadata.
- Voice, local models, project intelligence, screen understanding, autonomous work and other future systems were not implemented.

## Build Result

Release solution build: **success, zero warnings, zero errors**.
Windows x64 single-file publish: **success**.
Automated execution / smoke / live cloud / manual Windows scenarios: **blocked or not verified**.

Executable: src/DesktopAgent.App/bin/Release/net10.0-windows/win-x64/publish/DesktopAgent.App.exe.
It remains framework-dependent (.NET 10 Desktop Runtime) and unsigned.

## Files Changed

Important implementation files:

- src/DesktopAgent.Core/Ai/AiModels.cs
- src/DesktopAgent.Core/Permissions/WorkspaceCapabilities.cs
- src/DesktopAgent.Infrastructure/Ai/AgentToolRegistry.cs
- src/DesktopAgent.Infrastructure/Ai/WindowsAgentTools.cs
- src/DesktopAgent.Infrastructure/Ai/ComputerAgentToolRouter.cs
- src/DesktopAgent.Infrastructure/Ai/AssistantConversationService.cs
- src/DesktopAgent.Infrastructure/Ai/OpenAiCompatibleProvider.cs
- src/DesktopAgent.Infrastructure/Ai/AiProviderErrors.cs
- src/DesktopAgent.Infrastructure/Ai/ProviderCredentialNames.cs
- src/DesktopAgent.Infrastructure/Ai/WindowsCredentialStore.cs
- src/DesktopAgent.Infrastructure/Automation/ComputerCommandService.cs
- src/DesktopAgent.App/App.xaml.cs
- src/DesktopAgent.App/Services/AgentActionApprovalService.cs
- src/DesktopAgent.App/Views/AssistantView.xaml and code-behind
- src/DesktopAgent.App/Views/InteractionPanelWindow.xaml and code-behind
- src/DesktopAgent.App/Views/AiSettingsView.xaml and code-behind
- src/DesktopAgent.App/Views/OnboardingWindow.xaml and code-behind
- src/DesktopAgent.App/Views/DesktopAppWindow.xaml and code-behind
- src/DesktopAgent.App/Views/PermissionsView.xaml
- src/DesktopAgent.App/Controls/CharacterControl.xaml and code-behind
- tests/DesktopAgent.Tests/Program.cs
- tests/DesktopAgent.Tests/Part2Tests.cs
- README.md, ARCHITECTURE.md and this report

## Research

The provider protocol follows [official OpenAI function-calling documentation](https://developers.openai.com/api/docs/guides/function-calling): aggregate streamed calls, execute locally, and return actual outputs, with strict schemas and sequential calls. Windows threading and credential boundaries were checked against [WPF threading documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model) and [CredWriteW documentation](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew). [Coucou's Windows architecture](https://github.com/Louis-CFM/coucou/blob/main/windows/README.md) informed event-driven companion and permission UX principles. No third-party code, branding, artwork or sounds were copied.

Stopped at Part 2. Part 3 was not started.
