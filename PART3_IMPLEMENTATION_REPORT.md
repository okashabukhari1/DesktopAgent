# Part 3 Implementation Report

Date: 2026-10-02. Extended the existing application in place; no rewrite and no Part 4 work. Implementation is delivered, but the full definition of done is NOT verified: Windows Application Control intermittently blocks rebuilt unsigned assemblies, and interactive/live-provider checks remain outstanding.

## Architecture

Core owns ProjectProfile, ProjectLocation, ProjectSettings, discovery/catalog/editor/open contracts, and CurrentProjectContext. Infrastructure separates local discovery, versioned catalog persistence, typed project tools, project-opening orchestration, and the Windows editor adapter. The existing AI router, application catalog, workspace engine, character state, activity history and policy services remain in use. Projects represent development folders; workspaces remain launch/layout definitions.

## Project Discovery

Deterministic local signature detection runs on a worker task. Configured location scans are cancellable and bounded by depth, directory/project counts, entry counts and elapsed time. Nested detected roots are consolidated beneath an existing primary root; detected sibling backend/frontend or client/server roots can share a primary parent. Manual addition and metadata refresh inspect the selected folder and at most two child levels.

Only package.json, composer.json, project XML and Gradle manifests are read for classification, each at most 128 KiB. Other indicators are filename-only. XML DTDs and external entity resolution are disabled. Malformed/oversized manifests retain conservative signature classifications instead of crashing.

## Supported Project Types

.NET (solutions, solution XML, C#/F# projects), ASP.NET Web SDK, Node.js, React, Next.js, Vite, Express, NestJS, TypeScript, Laravel/PHP, Gradle/Android/Kotlin/Java, Python manifests, Maven/Java, and conservative static web (index.html plus a root CSS/JS indicator). Manual folders with no recognized signatures remain valid with unknown technology. Multiple technologies/frameworks/languages can coexist. Classification does not execute scripts, package managers, MSBuild or Git.

## Project Locations

Settings → Projects provides Add Folder, Remove, editable Include Subfolders and depth 0–8, Scan Now, Cancel Scan, and catalog-backed development defaults. Maximum 32 locations. Drive/share roots, dependency/generated roots, directory links and linked ancestors are rejected. No locations are seeded and no recursive startup scan runs. User data/project directories were not deleted or silently added during development.

## Project Catalog

projects.json lives beside settings.json under the existing DesktopAgent local data directory. Schema version 1, atomic temporary-file replacement, bounded cache size/count, stable GUIDs across refresh/relocation, detached copies, favorites, editor/terminal/workspace preferences and agent-opening history. Startup loads cached data and probes only cached root availability on a worker, not recursive discovery. Corrupt or future-version documents are preserved and made read-only with a visible warning; recovery requires restoring/migrating the cache rather than silently overwriting it.

Search supports exact ID/name, prefix, name tokens, path, technology and bounded edit-distance fuzzy matching. Favorites and recency break ranking ties. Exact duplicate names and multi-candidate non-exact opening requests are never resolved by favorite/recency alone. Lists/search return bounded results; the entire catalog is not injected into every AI request.

## Projects UI

Added Projects navigation, a compact searchable picker/list, details, Open, Folder, Editor only, Workspace, Save preferences, Favorite, Refresh metadata, Locate Project and Remove from catalog. Settings includes project locations and .NET/Web/Android editor defaults selected from supported installed application catalog entries. Per-project terminal preferences are stored but not executed. A reusable ProjectPicker and ProjectPickerWindow serve navigation and workspace selection. Assistant ambiguity buttons use the same stable catalog project identities and preserve the original typed action; they are not a separate free-form filesystem picker.

Home displays actual current/recent projects with opening actions. Full and compact Assistant display current project and Clear. UI subscriptions attach/detach with view lifetime. No polling loops were added. Visual quality and interactive click-through remain to be manually verified.

## AI Tools

Eight new tools, bringing the application composition to 30 typed tools:

- list_projects
- search_projects
- get_project
- get_recent_projects
- get_project_technologies
- open_project
- associate_project_workspace
- get_current_project

Strict schemas reject unknown/duplicate/missing fields, invalid types, overlong/control-character input. Tools call typed services directly, never convert AI output into text commands. Association changes require catalog-modification approval. Per-invocation project/workspace snapshots invalidate approval if definitions change. Permissions are rechecked before execution. Project metadata queries also require ViewMetadata, so Search alone does not bypass a metadata denial.

## Project Opening

Resolve a real catalog project; require a physical available folder; prefer its associated workspace; select explicit editor, per-project preference, technology-specific configured default, then a compatible known installed editor. Explicit unknown/ambiguous editors fail clearly; no installation occurs. Missing workspaces fall back with a partial-result warning. Multiple inverse workspace associations require an explicit preferred workspace.

The editor adapter supports catalog entries whose installed executables are Code.exe, devenv.exe or studio[64].exe. VS Code receives --new-window and the absolute folder. Visual Studio receives an existing solution/project, otherwise the folder. Android Studio receives the folder. ProcessStartInfo.ArgumentList and UseShellExecute=false prevent project names from becoming generated shell commands. A successful process start is reported accurately as “Sent project to editor,” not proof that the IDE loaded it. Workspace results and editor failures are retained in final messages. Successful preparations update current session and recent agent openings; a missing editor with no usable workspace does not fake success.

## Workspace Integration

WorkspaceDefinition has optional ProjectId, retained by deep copy and the existing backward-compatible JSON format. Workspace editor has a Project tab with the reusable picker and Clear. ProjectProfile has optional AssociatedWorkspaceId; this explicit preference has priority, otherwise a unique workspace ProjectId match is usable. Missing associations do not invalidate the project.

OpenProject prepares the workspace through its existing service, then sends the actual project to the selected editor. A direct workspace launch can select its associated project as session context; it does not secretly inject editor arguments into old workspace application entries. Association references are separate, not a transactional bidirectional foreign-key system. Project variables were intentionally not implemented; existing resource validation and execution semantics remain unchanged. No terminal-at-directory adapter exists, so no terminal commands are generated.

## Current Context

CurrentProjectContext stores actual session-selected ID/name/path/workspace/open time, with change events and Clear. It is not permanent AI memory or window/IDE monitoring. The Assistant receives only this compact context and can query get_current_project, then use the returned path with the existing open_folder tool. Project-name requests bypass the application-only fast path, and project pronouns are left for contextual tool planning. Recent projects are persisted only when the agent's project-opening workflow succeeds. New conversation clears conversation history, while project selection remains session state until Clear or process exit.

## Git Metadata

Detect a local .git directory or worktree marker. Read only a bounded non-linked .git/HEAD (4 KiB) for branch/detached hash and record the selected repository root. Worktree pointers outside the project are not followed; their branch can remain unavailable. No Git process, config/credentials/status/diff reading, commits, pull/push/reset/checkout/merge or other writes.

## Privacy

No source-code contents, .env, appsettings.*.json, secrets.json, credentials files, certificates/keys, complete file trees or Git config are read for discovery or sent to cloud AI. Static web/PHP/artisan/Python/Maven indicators are inspected by name only. Tool responses project small safe metadata fields and bounded signature lists; raw manifest contents never enter prompts. No API credentials were printed or moved into settings.

## Permissions

Project.ViewMetadata, Project.Search, Project.Open and Project.ScanLocations default Allow. Project.ModifyCatalog defaults Ask and cannot be promoted to Always Allow by an approval button. open_project also requires application resolution/open capabilities and all capabilities derived from embedded workspace resources/window/close actions. Direct user UI actions follow the existing explicit Applications/Workspaces UI permission model; the scan capability is not exposed as an unrestricted AI filesystem tool.

## Performance

No recursive startup indexing, file watchers, perpetual rescans or idle polling. Limits: 32 locations, depth at most 8, 10,000 visited/queued directories, 2,000 projects, 512 immediate entries per directory, 60-second scan budget checked between filesystem operations, bounded manifests and bounded query outputs. Windows filesystem calls themselves cannot be forcibly cancelled while blocked by a disconnected/network drive; cancellation resumes between calls. Limit/partial scan warnings are shown. Activity logs catalog/scan/open outcomes, not every visited directory.

## Tests

Added 39 Part 3 cases; total 123 compile. Coverage includes requested types, exclusions/depth/boundaries, multi-tech aggregation, cancellation, malformed/oversized XML/JSON, secret metadata projection, read-only Git, ranking/ambiguity, persistence/identity/preferences, cached startup, corruption/schema protection, missing paths, no disk deletion, settings cloning, editor/workspace/context behavior, all eight tools, validation, permission/approval race, assistant ambiguity and contextual folder opening, workspace association persistence.

Actual earlier execution: 122/123 passed, including all 84 existing regression tests and 38/39 new tests. The remaining assertion compared a mixed-slash fixture path against a normalized Windows path; it was corrected to compare Path.GetFullPath. Later security/race and shallow-refresh improvements compile, but the FINAL suite rerun was blocked by Windows Application Control (0x800711C7). Therefore do NOT interpret the earlier 122 passes as a verified final 123/123 result. No passing results were fabricated.

## Manual Windows Tests

Actual checks: two earlier WPF smoke invocations exited 0 (before the expanded final Projects/picker/editor smoke coverage). The expanded final smoke attempt was blocked while loading the new Core assembly. Native interactive UI automation is not available in this session; no live-provider natural-language tests or actual IDE project-opening tests were completed.

All eight requested interactive scenarios remain pending: adding/scanning a real selected development location; listing actual projects through AI; filtering React; opening a selected project; contextual explicit VS Code opening; associated workspace opening; continuing a recent project; missing-folder recovery. Equivalent catalog/context/permission/missing-path behaviors have automated fixtures, but fixtures are NOT manual Windows/live-provider evidence. Only disposable test folders were renamed/removed; user projects were not touched.

## Regression

All 84 existing Part 1–2 automated cases passed in the earlier executed revision. Existing application/workspace/controller/provider systems were extended, not replaced. The final solution compiles, but latest runtime regression remains blocked and cannot be certified from the historical run. No microphone, arbitrary terminal, source intelligence, background autonomy or Part 4 capability was added.

## Known Limitations

Final runtime/manual verification remains incomplete. Unsigned EXE/DLLs can still be blocked by Windows; this task did not change security settings or produce a trusted signing certificate. The appropriate distribution fix requires trusted signing/reputation review, not security bypass.

Detection is heuristic, capped, and metadata-only. Static web expects root CSS/JS, worktree branch can be unknown, and arbitrary editor launch formats are unsupported. Manual refresh inspects two levels; configured discovery honors the chosen deeper bound. Project modification-today requests cannot be verified from agent-opening timestamps and are explicitly not guessed. No source semantics, Git writes, project variables, terminal commands, IDE monitoring or automatic stale-project scan. Picker displays up to 100 matching entries; search narrows larger catalogs. IDE process acceptance is not an IDE-loaded-project acknowledgement. Corrupt/future catalog recovery currently requires restoring/migrating its local file.

## Build Result

Release solution build and Windows single-file framework-dependent publish succeeded with no observed warnings/errors. Output: src/DesktopAgent.App/bin/Release/net10.0-windows/win-x64/publish/DesktopAgent.App.exe. Requires Windows and the .NET 10 Desktop Runtime; remains unsigned. Final runtime tests are blocked as described above.

## Files Changed

New:

- src/DesktopAgent.Core/Projects/ProjectModels.cs
- src/DesktopAgent.Infrastructure/Projects/ProjectDiscoveryService.cs
- src/DesktopAgent.Infrastructure/Projects/ProjectCatalog.cs
- src/DesktopAgent.Infrastructure/Projects/ProjectOpenService.cs
- src/DesktopAgent.Infrastructure/Projects/ProjectTools.cs
- src/DesktopAgent.Infrastructure/Windows/Automation/WindowsProjectEditorLauncher.cs
- src/DesktopAgent.App/Views/ProjectPicker.cs
- src/DesktopAgent.App/Views/ProjectSettingsView.cs
- src/DesktopAgent.App/Views/ProjectsView.cs
- tests/DesktopAgent.Tests/Part3Tests.cs
- PART3_IMPLEMENTATION_REPORT.md

Updated: Core AppSettings, AiModels/tool preparation, permission definitions, WorkspaceDefinition; infrastructure settings, workspace service, AI router/conversation; App composition and smoke check, WindowService, approval service, SettingsViewModel, SettingsWindow, DesktopAppWindow, AssistantView, WorkspaceEditorWindow; test harness, README.md and ARCHITECTURE.md.

Design references: bounded/link-skipping enumeration follows [Microsoft EnumerationOptions documentation](https://learn.microsoft.com/dotnet/api/system.io.enumerationoptions). Fixed editor argument behavior was checked against [VS Code command-line documentation](https://code.visualstudio.com/docs/configure/command-line), [Microsoft Visual Studio Open Folder documentation](https://devblogs.microsoft.com/cppblog/bring-your-c-codebase-to-visual-studio-with-open-folder/) and [JetBrains command-line documentation](https://www.jetbrains.com/help/idea/working-with-the-ide-features-from-command-line.html). No external code was copied.

Stopped at Part 3. Part 4 was not started.
