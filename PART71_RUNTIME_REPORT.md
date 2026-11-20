# Part 7.1 runtime repair — in progress

Updated 2026-10-11. **Not runtime-accepted. No Part 8 work.**

## Exact Root Cause

The original 2026-10-07 diagnostic rejected `get_foreground_window` with received shape `window_handle=String,query=Null,control_type=Null`. Its schema permitted any string while the implementation required a logical window reference or positive decimal HWND (`UI.ReferenceMustBeWindowPrefixOrPositiveDecimalHandle`). Optional/nullable and search requirements also diverged.

A later live HTTP 400 diagnostic identified `tools[52].function.parameters`, `find_ui_element`, and the provider's rejection of top-level schema combinators. The replacement schema no longer uses these combinators.

On 2026-10-11, the Notepad task's `focus_ui_element` was rejected with six correctly shaped properties before input. The old logger did not identify the precise failing value constraint; its literal argument values were not recorded. Do not claim a specific rejected value is known. Source audit confirmed action schemas omitted reference patterns, input bounds, key choices, and action-specific input requirements enforced by their validators. Those constraints now come from the exposed action schema, and safe `Action.*` rejection categories identify future failures.

The older native `ChangedOrSensitiveControl` failure has **not** been reproduced with the new flag-only diagnostic. Its exact failing clause remains unverified; no protected-field check has been weakened.

## Fix

`UiReadContract` drives all seven read schemas and validators. Foreground/list operations accept genuine `{}`. Targeted reads accept optional omitted/null logical references and filters. Find requires a nonblank query. Provider strictness matches optionality. Read-only foreground execution is pinned per invocation across approval, with PID/HWND-reuse checks.

Action definitions now expose and validate the same reference patterns, text limits, key choices, required-null unused inputs, and bounded scroll arguments. Diagnostics log fixed failure categories/boolean checks, not user text, API credentials, element names or identifiers.

## Calculator Targeting

The old already-running launch focused the generic `ApplicationFrameHost` process, which could select another hosted application. Launch now focuses the uniquely selected window identity, checks owning/hosted PID and process creation identity, and rejects ambiguity. Prior live requests focused Calculator and inspected its real controls; arithmetic has not passed.

## Provider Contract Tests

2026-10-11: **13/13 Part71 tests passed**, including provider-style JSON → provider assembly → router → validator → typed read, optional combinations, malformed calls, hosted-window distinction, pinned approval target and safe rejection logs. These tests use a controlled HTTP provider fixture; separate real app requests returned HTTP 200.

## Runtime Acceptance Matrix

| Test | Result / evidence |
|---|---|
| A Calculator | Not passed. Prior attempts paused on physical input; 13,872 not verified. |
| B Notepad | Not passed. Earlier native rejection; 2026-10-11 action argument rejection followed by a rebuilt retry paused after manual approval/input. |
| C Wrong-target protection | Not run end-to-end. Unit scope tests pass. |
| D Settings | Not run. |
| E Scroll | Not run. |
| F Shortcut | Not run against a real document. |
| G Visible STOP | Not verified during an active task with remaining actions. |
| H Emergency STOP | Not verified during an active task. |
| I User takeover | Physical input paused live tasks; the specified manual focus-switch text-entry test has not run. |

Prior real product observation verified Calculator identification and actual interactive controls, including cloud `get_foreground_window {}` and `get_interactive_elements`. This is not proof of any mutation acceptance test.

## Automated Tests

Latest complete execution: **260/263 passed** with temporary test fixtures placed under writable `artifacts/test-temp`. All 31 Computer Use cases and all 13 Part71 cases passed. Failures:

- Hard-link fixture creation failed.
- Junction fixture creation failed.
- Current-user named pipe test: access denied.

These tests loaded and executed; they are not test-assembly loader blocks. Earlier historical Application Control loader blocks remain distinct. No trust/security policy was changed or bypassed.

## Regression

Earlier application, settings, voice/parser, workspace, project, file and UI cases executed in the full suite. Three fixture/access failures prevent claiming a completely green regression suite. No comprehensive live Parts 1–6 regression was performed today.

## Remaining Limitations

Part 7.1 remains incomplete. Live acceptance is currently interrupted by physical input after permission approval. Permission prompts must be approved by the user; the computer-use skill forbids automating privacy/security approvals. Approval is not authorization to disable the takeover guard.

The authorized OpenRouter key-allowance check on 2026-10-07 returned HTTP 200, `limit=null`, `limit_remaining=null`, usage 0.02105445 and `is_free_tier=true`. No configured key spending cap was returned; this does not establish account credit balance or unlimited free-model quota. Latest observed real app requests returned HTTP 200; no new account allowance check was performed today.

## Build Result

2026-10-11 solution build: **0 warnings, 0 errors**. Framework-dependent Windows x64 single-file publish succeeded and launched normally.

Executable: `artifacts/part71/DesktopAgent.App.exe` (1,588,963 bytes).

SHA256: `E03A98E0C76406E8138AA32A7CB7B98865A98A5EAA8AB30BE2B793102EE5E9A6`.

Next: hands-off real Notepad retry, diagnose any exact action rejection, then complete the remaining acceptance tests. Do not mark Part 7 accepted or start Part 8.
