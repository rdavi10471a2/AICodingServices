# Independent Review — `codex/semantic-kernel-workflow-orchestrator`

**Reviewer:** Claude (Opus 4.8), external read-only review on a fresh clone of `AICodingServices`.
**Date:** 2026-06-22
**Branch reviewed:** `codex/semantic-kernel-workflow-orchestrator` (diff base `main`).
**Method:** Full read of the SK wiring, the deterministic policy engine, the MCP mutation gate, the governed-command reducer, the Codex hooks, and the SK design docs. `dotnet build` + full `dotnet test` executed locally. Authoritative SK / Codex / MCP behavior cross-checked against current (2025–2026) Microsoft and OpenAI documentation (citations in the appendix).

> Scope note: This is a separate code review. No watched source was modified. This document is the only artifact written into the repo.

---

## 1. Executive summary

The branch contains two very different things wearing one label.

- **Real and salvageable:** a deterministic edit-policy engine (`SessionIntentPolicyService`), an instructive tool-selection guidance model (`ToolSelectionGuidance`), a per-mutation enforcement gate (`EnsurePlannedMutationAllowed`), and a command-output reducer (`GovernedCommandOutputReducer`) wired into pre-merge build validation. This compiles and its unit tests pass.
- **Aspirational scaffolding wrapped in "Implementation Complete" language:** the Semantic Kernel "orchestrator" is a single no-LLM function dispatch; the headline SK implementation guide is written against **removed** SK APIs; and the Codex-Desktop host-integration layer models capabilities Codex does not expose ("native mounted MCP", tool-driven thread reset).

The proposal's banner — *"Implementation Complete – All tests passing"* (`docs/sk-integration/InstructiveGovernanceProposal.md`) — is **not true** on this branch as checked out: the suite is red (see §3). More importantly, the **green** tests validate only the deterministic policy substrate; nothing tests the SK orchestration or the Codex-host claims, because those are docs and scaffolding rather than working code.

---

## 2. Ground truth — what is actually wired

### 2.1 Semantic Kernel footprint

SK appears in exactly one runtime path, `InitializeCodingServices`:

- `src/AICodingServices.McpServer/Program.cs:140` — `Kernel kernel = Kernel.CreateBuilder().Build();` with **no AI/chat-completion service registered**, then `AddFromObject(plugin)` and `kernel.InvokeAsync("CodingServicesSessionStartup", "initialize_coding_services", args)`.
- The plugin (`src/AICodingServices.Workflow/CodingServicesSessionStartupWorkflow.cs:209`, function at `:231`) is one `[KernelFunction("initialize_coding_services")]` over deterministic process/probe logic.
- `Microsoft.SemanticKernel` 1.77.0 is referenced only in `src/AICodingServices.Workflow/AICodingServices.Workflow.csproj`.

With no AI service, `kernel.InvokeAsync(plugin, fn)` is behaviorally identical to calling the method directly (Microsoft's own `Kernel.InvokeAsync` docs), plus DI/telemetry overhead that this code does not consume. The governance core has **no SK in it at all** — `SessionIntentPolicyService` is plain deterministic C#.

### 2.2 Governed command reducer

`GovernedCommandOutputReducer` is real and **is** wired — but only into `PreMergeValidationService` (`src/AICodingServices.Workflow/PreMergeValidationService.cs:209`), i.e. the monitor's own `dotnet build`. It is **not** exposed as an agent-facing governed shell tool. `docs/sk-integration/GovernedCommandProof.md` itself concedes: *"The proof is still incomplete until the reducer is wired into the actual command execution path."*

---

## 3. Build & test ledger (executed locally)

**Build:** `dotnet build AICodingServices.slnx -c Debug` → **0 errors, 0 warnings.** The code compiles against SK 1.77.0. (The dead-API problem in §4/H2 is confined to a design *document*, which is not compiled.)

**Tests:** `dotnet test` per project:

| Project | Result | Failures |
|---|---|---|
| Core | PASS 11/11 | — |
| McpStdioBridge | PASS 6/6 | — |
| CodexUI | PASS 20/20 | — |
| Indexing | PASS 11/11 | — |
| Runtime | PASS 9/9 | — |
| Workflow | **FAIL 131/132** | 1 |
| Logging | **FAIL 5/6** | 1 |
| MSBuild | **FAIL 4/9** | 5 |
| Data | **FAIL 27/29** | 2 |

**Failure classification (honest):**

- **MSBuild (5):** `MSBuildLocator.RegisterInstance … assemblies were already loaded` / `Could not load Microsoft.Build, Version=15.1.0.0`. Test-host MSBuildLocator isolation failure. **Environmental, not branch logic.**
- **Data (2):** `SolutionIndexBuilderTests.RefreshProjectFilesAsync_preserves_razor_generated_reference_rows` returns an empty collection — the **known Roslyn-vs-SDK source-generator skew** (documented elsewhere as "document, don't pin"). `SyntheticEditWorkflowTokenBenchmarkTests.Synthetic_large_file_edit_context_baseline` is a fixture-content substring drift. **Environmental / fixture drift.**
- **Workflow (1):** `WorkflowEditServiceSafetyTests.PreMergeValidation_builds_blazor_scoped_css_overlay_without_absolute_scoped_css_paths` (`WorkflowEditServiceSafetyTests.cs:674`) — a nested real `dotnet build` exited 1 and *"did not emit parseable error diagnostics."* Build-environment-sensitive, but lands on the GovernedCommand/PreMergeValidation reducer path, and the reducer could not parse the failure output.
- **Logging (1):** `MonitorLogPathsTests.GetDefaultLogPath_uses_runtime_root` (`MonitorLogPathsTests.cs:17`) — expects fixed `aimonitor.ndjson`, gets a **timestamped** `aimonitor-20260622T154821-…`. `MonitorLogPaths` was changed on this branch to emit timestamped names and the test was not updated. **Genuine code-vs-test drift introduced on this branch** — directly contradicts the "all passing" banner.

**Interpretation:** Most failures are environmental; one (Logging) is real drift. None of the failures are in the SK or policy logic. Equally: **none of the passing tests validate the SK orchestration value or the Codex-host capabilities.** The suite is green where the work is narrow and real (deterministic policy, reducer-on-build, index plumbing) and silent everywhere the "SK orchestrator / Codex Desktop host" narrative lives.

---

## 4. Findings

### Semantic Kernel

**H1 — SK adds no value where it is used; the tool description oversells it.**
A bare kernel with no AI service reduces `InvokeAsync` to function dispatch. Microsoft's docs: *"While you can invoke a plugin function directly, this is not advised … if you need explicit control … use standard methods in your codebase instead of plugins."* The MCP tool advertises *"Initialize Coding Services through Semantic Kernel"* (`Program.cs:111`) over a one-function dispatcher. SK's only no-LLM value-add (the filter/telemetry pipeline) is not used.

**H2 — `docs/sk-integration/SemanticKernelImplementationGuide.md` is written against removed APIs.** It will not compile against the pinned 1.77.0:
- `[SKFunction]` / `[SKParameterName]` — removed at SK v1.0 (Dec 2023); replaced by `[KernelFunction]` + `[Description]`.
- `SequentialPlanner` / `StepwisePlanner` / `Microsoft.SemanticKernel.Planners.*` — deprecated 2024-07, **source-deleted June 2025** (SK PR #12399). Replaced by `FunctionChoiceBehavior.Auto()`.
- `ISemanticTextMemory` — officially **legacy**, superseded by `Microsoft.Extensions.VectorData` (GA May 2025).

The roadmap's "planner-assisted multi-file edits" later-slice (`SemanticKernelPolicyGovernorRoadmap.md`) plans on dead API. Note the *wired* plugin uses the correct `[KernelFunction]`/`[Description]` — so the guide is more wrong than the code, and anyone following it ships broken code.

**H3 — There is no real MCP↔SK bridge.** The idiomatic 1.77 patterns — consume MCP tools via `mcpClient.ListToolsAsync()` + `.AsKernelFunction()`, or expose SK functions via `AddMcpServer().WithTools(kernel)` — are not used. The MCP tools are plain `[McpServerTool]` methods SK never sees. "Restructured coverage of MCP inside SK" amounts to *one* startup function double-decorated as both `[KernelFunction]` and `[McpServerTool]` — a duplicated surface, not integration.

**H4 — Proposal's "Third Code Slice" (SK consumes the deterministic guidance) is not implemented.** No SK plugin wraps `SessionIntentPolicyService`. "SK as policy governor" is roadmap-only despite the "Implementation Complete" banner.

### MCP / governance

**H5 — Enforcement is adapter-only; the proposed SK plugins would bypass it.**
`EnsurePlannedMutationAllowed` (`Program.cs:1252`) lives only in the MCP tool layer. `WorkflowEditService` / `RoslynEditService` have zero policy awareness. The implementation guide's `WorkflowEditPlugin` / `RoslynEditPlugin` call those services directly — routing *around* the one place the "enforcement core" exists. This contradicts the proposal's own Guardrail #2 ("deterministic MCP policy must still decide allowed vs blocked").

**H6 — Policy is derived from agent-declared intent, never verified against ground truth.** See §5 below for the full chain. `BuildPlannedFileIntent` (`Program.cs:1411`) trusts `TargetKind` verbatim; extension inference (`InferTargetKind`, `:1432`) runs only when the field is blank. `ChangeKind`, `ExpectedShape`, `Risk`, and `DiscoveryAlreadyDone` are passed through with no cross-check. There is no `File.Exists`, no index lookup, and no symbol confirmation anywhere in the intent-building path.

**H7 — Fail-open default in the enforcement path.** `EnsurePlannedMutationAllowed` returns (allows) when `DerivedPolicy is null` (`Program.cs:1266-1269`). Harmless today, but a null-policy → allow default in a safety gate should be fail-closed.

**H8 — "Guidance gate" is overstated.** `GetToolSelectionGuidance` (`AICodingServicesTools.StatusAndSession.cs:127`) is read-only; its own description says *"MCP mutation tools still enforce policy."* The "gate" is a skill-card instruction, not a runtime requirement. Real enforcement is the per-mutator `EnsurePlannedMutationAllowed` (good) — but the docs conflate advisory guidance with the gate.

### Codex "runs inside Codex Desktop"

**H9 — "native mounted MCP" is not a Codex concept.** Codex configures every MCP server identically (`[mcp_servers.*]` in `config.toml`, stdio or streamable-HTTP). There is no native-vs-bridged distinction and no "mount." The startup workflow's `NativeMountState` / `ActivateNativeMountedServerAsync` / `TransportKind.NativeMounted` machinery models a host capability Codex does not expose.

**H10 — Tool-driven "thread reset / remount" is not a Codex capability.** A thread snapshots its MCP surface at creation; the only supported recovery is a new thread. Host-side `config/mcpServer/reload` exists on the Codex App Server but is a client API, not invokable by an in-session tool. `attemptThreadReset` targets a non-existent capability — and both controllers (`NullCodingServicesSessionHostController`, `AttachedMcpSessionHostController`) return `Unsupported`, so the entire transport-fallback path is effectively dead.

**H11 — "Codex Desktop embeds the web app in-window" — partly wrong.** A real Codex desktop app exists (Windows, Mar 2026) with an in-app browser that can show a `localhost:5000` site, but it is not a WebView shell hosting the app as its UI, and there is no MCP API to "render this app in my window." The CodexUI Blazor app is a localhost site a user opens in a browser pane.

**H12 — Session-start hook mechanism is correct but the paths are broken/non-portable.** `.codex/hooks.json` + `SessionStart` + `commandWindows` are legitimate Codex features (hooks GA May 2026). But `commandWindows` hardcodes `C:\VSCodeProjects\CodingServices` while the repo is `AICodingServices`; `codingservices-session-start.ps1` hardcodes `$repoRoot='C:\VSCodeProjects\CodingServices'` then `exit 0` if cwd doesn't match — so on this checkout the hook silently no-ops. The POSIX `command` uses `$(git rev-parse --show-toplevel)`; the Windows variant does not. The hook's final log line repeats the H9 fiction ("through mounted MCP").

---

## 5. The intent-declaration chain (focused analysis)

The intended model has three links; the code has two.

- **Intended:** plan → declare intent → **verify intent is true** → derive policy → enforce policy on edits.
- **As written:** plan → declare intent → derive policy → enforce policy on edits.

The middle link — verify the declaration against reality — does not exist. Concretely:

1. **`TargetKind` trusted verbatim** (`Program.cs:1413-1415`). `InferTargetKind` only runs when the field is blank. Declaring a `.cs` file as `Markdown` removes the C# "must use Roslyn" block and permits `TextReplace`.
2. **`ChangeKind` / `ExpectedShape` ungrounded.** Declaring `ChangeKind=AddNewFile` / `ExpectedShape=NewType` on an existing file flips to the new-file policy (`SessionIntentPolicyService.DeriveCSharpPolicy`, isNewFile branch) where `WholeFile` is preferred and `TextReplace` is an allowed fallback. The engine knows whether the file exists but never asserts it.
3. **`Risk` / `DiscoveryAlreadyDone` self-attested.** Reference-discovery only fires on `!DiscoveryAlreadyDone && IsSharedRisk(risk)` (`SessionIntentPolicyService.cs:295`, `:334`). Both are agent-reported; the index is never consulted to confirm.

So the loop validates **"did your tool choice match the policy your own declaration produced"**, not **"is your declaration true."** It is internally consistent and externally ungrounded.

**Bounding the impact:** the safety floor remains grounded — candidate C# syntax validation, the pre-merge overlay compile (GATE 1), the full `dotnet build` (GATE 2), the staged diff, and human WinMerge review all run regardless of declared intent. The intent hole degrades the **precise-edit discipline** (the project's token/diff-quality thesis) to advisory; it does not by itself permit tree corruption.

**Remediation options (cheap → strong):**
- Cross-check declared `TargetKind` against the file extension; reject or downgrade on mismatch.
- When `ChangeKind=AddNewFile`/`NewType`, assert the watched file does not already exist (the engine already distinguishes `refresh_file` vs `new_file`).
- Confirm `TargetSymbols` exist via the solution index before honoring `MethodReplacement`/`AddMethod` shapes.
- Treat `DiscoveryAlreadyDone` as a claim to be verified (the index can compute inbound references) rather than a trusted input, at least for shared-risk surfaces.

---

## 6. Salvageable vs vapor

**Salvageable (real, compiles, tested):** `SessionIntentPolicyService` + `ToolSelectionGuidance`; `EnsurePlannedMutationAllowed`; `GovernedCommandOutputReducer` on the pre-merge build path; the CodexUI staged-review surface.

**Vapor / scaffolding:** the "through Semantic Kernel" framing (a no-LLM dispatcher); `SemanticKernelImplementationGuide.md` (dead APIs); the Codex host-integration layer (`NativeMount*`, `attemptThreadReset`, transport fallback) targeting capabilities Codex does not expose.

---

## 7. Recommendations

1. **Drop the SK layer; solve agent compliance with gates instead (see §9).** SK exists to make Codex follow the workflow, but it is architecturally incapable of doing so — replace it with tool-boundary gates and Codex-native steering. Call the startup method directly and stop advertising "through Semantic Kernel."
2. **Mark `SemanticKernelImplementationGuide.md` as historical/incorrect** or rewrite against SK 1.x (`[KernelFunction]`, `FunctionChoiceBehavior.Auto()`, `Microsoft.Extensions.VectorData`, `AsKernelFunction()`).
3. **Add intent verification** (§5 remediation) — this is the highest-leverage fix for the project's actual thesis.
4. **Re-anchor the Codex integration to real features:** stdio/streamable-HTTP `[mcp_servers]` config, host-side `config/mcpServer/reload`, "open a new thread" as the reset story, and the in-app browser for the localhost UI. Delete or clearly mark the `NativeMount`/thread-reset machinery as unsupported.
5. **Fix the hardcoded hook paths** (`commandWindows`, `$repoRoot`) to derive from the repo root on both platforms.
6. **Update the stale `MonitorLogPaths` test** and stop publishing "All tests passing" until the suite is green in a clean environment; document the MSBuildLocator / Razor-gen environmental failures as known.
7. **Either enforce policy below the adapter** (in the edit services) or explicitly document that any non-MCP caller (including future SK plugins) is outside the safety boundary.

---

## 8. "Derive, don't ask" — the intent surface redesign

### 8.1 The principle

The policy engine never receives the operator's instruction — it receives the model's *paraphrase* of it. The chain is `operator intent → [LLM interprets] → declared intent → derived policy`, and the LLM hop is lossy and stochastic. The engine therefore cannot validate the declaration against the operator's intent (it has no copy). The only ground truth on the engine's side of the LLM is **physical reality**: the file extension, file existence, the solution index, and the generated candidate.

So the rule is the oldest one in the book, applied with the model as the untrusted client:

> **Do not accept as input any fact the engine can derive or verify from artifacts it already owns.**

This is not a defense against a "lying" model (the model has no intent to deceive). It is a checksum against an *unreliable narrator*: path-of-least-resistance label drift, ordinary mislabeling, and — once the watched repo contains attacker-controlled text — genuine upstream prompt injection. All three are neutralized by the same move: ground the declaration in reality instead of trusting the self-report.

### 8.2 Field-by-field disposition

Classifications:
- **DERIVE** — engine computes from artifacts; the model input is ignored (or a mismatch is rejected). It should not be an MCP parameter at all.
- **VERIFY** — model may propose; engine confirms against the index/candidate before honoring it (at declaration) or before merge (post-hoc).
- **OBSERVE** — engine tracks the truth from actual session actions; it is not a claim the model gets to assert.
- **KEEP** — genuine model/human narrative; not a safety gate, used only for ledger/justification.

| Field (today) | Today | Class | How the engine grounds it | Anchor |
|---|---|---|---|---|
| `TargetKind` | trusted unless blank | **DERIVE** | Always run `InferTargetKind(path)` from the extension; ignore the supplied value, or reject on mismatch. The extension is authoritative truth the engine already has. | `Program.cs:1413-1415`, `InferTargetKind` `:1432` |
| `ChangeKind = AddNewFile` / new-file branch | trusted | **DERIVE** | New-vs-existing is `File.Exists(watchedPath)`. The engine already distinguishes `refresh_file` from `new_file`. Set the new-file policy from reality; reject `AddNewFile` when the file exists. | `BuildPlannedFileIntent`, `DeriveCSharpPolicy` isNewFile branch |
| `TargetSymbols` | passed through verbatim | **VERIFY** | Confirm each named symbol exists in that file via `SolutionIndexQueryService.FindSymbols` before honoring symbol-shaped policy. Unknown symbol → flag/reject, don't silently nudge ordering. | `Program.cs:1427` |
| `Risk` (shared/cross-file) | trusted self-report | **DERIVE / corroborate** | Blast radius is computable: `ListReferences` / `ListCallSites` on the target symbols. Compute shared-risk from inbound references rather than trusting `Risk`. | `IsSharedRisk` `SessionIntentPolicyService.cs:339` |
| `DiscoveryAlreadyDone` | trusted bool | **OBSERVE** | This is a claim the model should not be able to assert. Track whether the session actually invoked reference/caller discovery on the symbol and set this from observed session events. | `:1429`, gate at `SessionIntentPolicyService.cs:295/:334` |
| `ExpectedShape` (MethodReplacement, AddMethod, …) | trusted | **VERIFY (post-hoc)** | Cheapest, strongest check. After the candidate is built, diff candidate vs `GetSourceMap` / symbol bounds: did it actually replace a body, add a member, etc.? Compare the *structural delta of the candidate* against the declared shape before merge. The model has already shown its hand — verify the artifact, not the forecast. | `RoslynEditService.GetSourceMap`, candidate Working file |
| `Reason` | input | **KEEP** | Genuine narrative (the "why"); feeds the ledger and fallback justification. Not a gate fact. | `AICodingServicesToolResults.cs` input record |
| `Role` | input | **KEEP** | Organizational label; not a safety gate. | input record |

Net: of the six fields that currently shape policy, **two collapse to pure DERIVE** (`TargetKind`, new-file), **two become VERIFY against the index/candidate** (`TargetSymbols`, `ExpectedShape`), **one becomes OBSERVE** (`DiscoveryAlreadyDone`), and **one becomes DERIVE/corroborate** (`Risk`). Almost the entire "declare intent" surface is questions the engine can answer itself.

### 8.3 Enforcement flow change

Two grounding points, neither of which exists today:

1. **At declaration (`BuildPlannedFileIntent` / `DerivePolicy`)** — derive `TargetKind` (extension), new-vs-existing (`File.Exists`), and shared-risk (index inbound references) from artifacts; reject impossible declarations (e.g. `AddNewFile` on an existing file, `TargetSymbols` absent from the index) instead of deriving a policy from them. All lookups, no diff.
2. **At pre-merge — verify `ExpectedShape` against what was actually done.** Two complementary checks, cheapest first:
   - **Tool-call record (have it).** The mutation tool the agent invoked already encodes the operation (`add_method` = add, `submit_symbol` on an existing symbol = replace). Every call is already logged via `RecordMonitorSessionEvent`, and `ParseOperationFamily` already maps tool → family. Declared `AddMethod` but the session shows a replace → contradiction. This is a lookup over your own event log, not a diff.
   - **Structural confirmation (syntax-only, no stored source maps).** Note on the available machinery: there is no persisted "before"/"after" source map, and none is needed. At pre-merge both versions coexist on disk — **before** = the watched file (untouched until the WinMerge save), **after** = the staged candidate — and `PreMergeValidationService` already assembles both sides for the overlay (GATE 1). So the check is two `CSharpSyntaxTree.ParseText` calls on those two file paths, enumerate member declarations, compare by signature (added / removed), and hash matched bodies (changed). This is syntax-only: no semantic model, no compilation, no `GetSourceMap` (which is the wrong tool here — it resolves to one canonical/indexed version, leaving "which version" ambiguous). DiffPlex (referenced only in CodexUI, line-level) is for *rendering* the diff to a human, not for this structural claim. A candidate that touches symbols outside the declared set, or whose delta shape contradicts the declaration, is surfaced in the staged record before WinMerge.

This also subsumes two earlier findings: make the gate **fail-closed** (H7 — a null/underived policy should block, not allow), and decide whether grounding lives only at the MCP adapter or **below it in the edit services** (H5) so that any non-MCP caller — including a future SK plugin — cannot route around it.

**Minimum viable (so this is worth it, not infinite):** Tier-1 declaration grounding (§8.2 DERIVE/VERIFY) plus the **tool-call-record** shape check close the practical hole for a small amount of code — extension compares, `File.Exists`, index lookups, and one comparison against an event log you already write. The **syntax-only member-set parse** is the strong-confirmation tier; it is the most code and the lowest marginal gain, and can be deferred until an incident or audit justifies it. None of it requires DiffPlex, stored source maps, or new persistence.

### 8.4 What this does and does not buy

It does **not** make the model honest, and it does not need to. It removes the model's ability to *unlock a weaker edit family by asserting a convenient label*, by taking those labels out of the model's hands and computing them from the file, the index, and the candidate. The residual model input (`Reason`, the goal statement) is narrative that the post-hoc candidate check can still cross-examine. The existing safety floor (syntax validation, GATE 1/GATE 2, staged diff, human WinMerge review) is unchanged — this restores the *precise-edit discipline* that the intent hole currently degrades to advisory.

### 8.5 Trust boundary — server-side adjudication, no LLM in the verdict

Every check in §8.2–§8.3 is deterministic C# executing inside the MCP server / Workflow engine over artifacts the engine already owns: the file extension, `File.Exists`, solution-index rows, the recorded tool-call event log, and two syntax trees. **The LLM is never in the adjudication path.** Its role is reduced to two unavoidable MCP touchpoints — *proposing* intent (`start_monitor_session`) and *invoking* a tool — and both are now verified server-side against ground truth. The model receives a pass/reject the same way it already receives a C# syntax-validation failure: as feedback, not as a decision it gets to make.

Stated as the boundary: **the model proposes; the server disposes.** Self-asserted facts that the engine can derive or measure are never trusted as inputs to a safety decision. This is the same discipline applied to any value crossing a process boundary you do not control — here the boundary is the lossy, non-deterministic LLM hop, and the engine sits on the side that holds the original artifacts.

---

## 9. Compliance is a gate problem, not an orchestration problem

### 9.1 The real motivation for the SK layer

The SK "orchestrator" exists for one practical reason: **Codex does not reliably follow the workflow**, and the operator has to repeatedly badger it into calling `start_monitor_session`, preferring typed edits, and not freelancing with raw shell. SK was reached for as a way to *make the workflow deterministic so the agent can't skip steps.* That is a real problem worth solving. SK is the wrong solution to it.

### 9.2 Why SK cannot solve it

SK runs **inside** the MCP server. Codex is **upstream** of the MCP server — Codex drives the conversation and decides which tools to call, in what order. SK is downstream of the very decision it is meant to influence. **You cannot compel an upstream agent from inside the tool it calls.** No `Kernel.InvokeAsync` reaches back up the transport and forces Codex to call `start_monitor_session` first. SK is pointed the wrong direction; it was never going to stop the badgering.

### 9.3 What does solve it — gate, don't guide

Make non-compliance *fail* at the tool boundary, with an error that states the required next step. This is deterministic, server-side, and — unlike SK — agent-agnostic (it works for Codex *and* Claude).

- **The seed already exists.** `EnsurePlannedMutationAllowed` already rejects a mutation when there is no planned session. That is the real compliance lever — plain C#, no SK. Extend it: every place Codex cuts a corner should hit a gate that refuses and names the correct action (e.g. "call `get_source_map` then `submit_symbol`; `replace_text_in_file` on C# is blocked").
- **Two Codex-native steering surfaces are underused** (both confirmed against OpenAI docs):
  1. **MCP server `instructions` field.** Codex reads the `instructions` returned at initialization and treats it as server-wide guidance — every session, no badgering. Put the workflow contract there. Highest leverage; it is a string, not a subsystem.
  2. **SessionStart hook context injection.** The existing `.codex/hooks/codingservices-session-start.ps1` only writes a log file nobody reads. Codex SessionStart hooks can inject context into the first turn — use it to hand Codex the workflow contract, not to log to disk.

### 9.4 The only coherent SK story (and why to skip it)

The sole architecture in which SK could enforce the sequence is inverting the roles: SK-as-*driver*, running its own LLM loop that executes the workflow deterministically while Codex hands it a goal. That means running a second model inside the server — large cost and complexity, and it rebuilds a coding agent next to the coding agent you already have. Not worth it versus gating the tools.

### 9.5 Net

Cutting SK does not lose the capability the operator actually needs. Redirect that effort to (a) more gates at the tool boundary (extend `EnsurePlannedMutationAllowed`) and (b) the MCP `instructions` field plus SessionStart context injection. This stops the workflow-skipping for Codex *and* Claude, with no LLM in the enforcement path — consistent with the §8.5 trust boundary.

---

## Appendix — authoritative sources cross-checked

Semantic Kernel:
- Planners deprecated & removed: https://learn.microsoft.com/en-us/semantic-kernel/concepts/planning ; SK PR #12399 (source deletion, 2025-06-06).
- Function calling replacement: https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/function-calling/function-choice-behaviors
- V1 attribute rename (`[SKFunction]`→`[KernelFunction]`): https://learn.microsoft.com/en-us/semantic-kernel/support/migration/v1-migration-guide
- `Kernel.InvokeAsync` == direct function invocation: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.kernel.invokeasync ; plugins guidance: https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/
- Memory legacy / VectorData: https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/memory-stores
- MCP↔SK patterns: https://devblogs.microsoft.com/semantic-kernel/integrating-model-context-protocol-tools-with-semantic-kernel-a-step-by-step-guide/ ; https://devblogs.microsoft.com/semantic-kernel/building-a-model-context-protocol-server-with-semantic-kernel/

Codex:
- MCP config & transports: https://developers.openai.com/codex/mcp ; https://developers.openai.com/codex/config-reference
- App Server (`config/mcpServer/reload`, status, tool/call): https://developers.openai.com/codex/app-server
- Thread MCP surface is a creation-time snapshot: https://github.com/openai/codex/issues/20605
- Hooks (GA, `SessionStart`, `commandWindows`): https://developers.openai.com/codex/hooks
- Desktop app + in-app browser: https://openai.com/index/introducing-the-codex-app/ ; https://developers.openai.com/codex/app/browser
