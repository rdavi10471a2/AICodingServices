# Harness Verification

Use when deciding whether CodingServices, CodexUI, MCP, workflow, index, browser, or watched-source work is actually done.

## Rule

A change is complete only when the relevant harness surface has been checked. Prefer a focused proof over a broad ritual.

## Verification Ladder

- Code-only service behavior: run the smallest relevant unit tests.
- Repository/data behavior: run tests against the real storage shape when practical.
- CodexUI behavior: build the app, restart the app if needed, and verify the affected route in the in-app browser.
- Blazor interactivity: verify the page loads scripts and the control actually responds, not just renders.
- MCP/workflow behavior: check live MCP status and run the workflow step that proves the changed path.
- Watched-source edits: stage, review in WinMerge, record the decision, refresh after accepted changes, then ask whether the user wants validation on the real watched tree.

## Evidence To Prefer

- Exact test project or command outcome.
- Live browser route and visible state.
- MCP status counts, diagnostics, stale-count, or workflow state when relevant.
- Diff/staged record ids when using the watched-source workflow.
- A small failing/repaired reproduction when the work fixes a bug.

## Stop Conditions

- App build passes but the browser route was not checked for UI work.
- UI renders but the interactive behavior was not exercised.
- MCP status was assumed from memory instead of the live app/tool.
- A watched-source change was edited directly instead of through Working candidate, stage, review, and decision.
- A test was skipped without saying why.

## Related

`TroubleshootingDashboard.md`, `AICodingServicesWorkflowQuickStart.md`, `SystemMonitorStaging.md`, `ReviewQueueAndGates.md`, `BlazorRadzenAuthoring.md`.
