# Session Validation

Use for coupled multi-file C# edits.

## Rule

Compose and stage every coupled file in the same monitor session before the first review launch. The session plan is the declared dependency boundary: include changed declarations, dependent callers/consumers, generated companions, and project/config files whose accepted bytes must validate together.

Any symbol edit starts with blast-radius discovery. If the agent will change, remove, rename, move, or change the signature/visibility of a symbol, it must query references/callers/relationships and cross-check before composing the candidate. A terminal full overlay build runs before the final planned decision completes, but that build is a guardrail for missed impact, not permission to skip dependency discovery.

## Flow

```text
start_monitor_session(filesPlanned: [...]) with every planned watched file in the coupled edit
compose Working candidate A with sessionId on every mutation call
stage_candidate_for_review for file A with sessionId
compose Working candidate B with sessionId on every mutation call
stage_candidate_for_review for file B with sessionId
launch_staged_diff for file A; planned launch checks staged overlay readiness
launch_staged_diff for file B before recording decisions
record decision for file A; early accepted decisions may return deferred indexRefresh
record decision for file B; terminal accepted-overlay build must pass before final accept/index refresh
check each accepted decision's indexRefresh status before relying on solution-index queries
```

## Do Not

- Do not review file A before composing and staging coupled file B.
- Do not treat a clean single-file validation result as enough when another staged file is required for the feature to compile.
- Do not rely on the terminal overlay build to discover ordinary consumers; plan known dependents before review.
- Do not modify a symbol first and then look for blast radius after the fact. Discover the likely impact before composing the Working candidates.
- Do not let empty reference results shrink the session by themselves; cross-check before deciding a change is single-file.
- Do not continue to later diffs if an earlier staged item is blocked by validation or review-gate state.
- Do not run manual index refresh tools after each accepted file in a coupled chain; early planned accepts can defer index refresh until the terminal accepted decision refreshes the accepted planned set.

## Unblock

- Stage a corrected candidate for the same blocked file.
- If diagnostics identify a missed consumer/call site, add that file to the same monitor session and stage it before retrying review.
- If the agent believes overlay validation is wrong or noisy, explain the evidence and ask the operator before overriding.
- Or explicitly force-review the blocked item through the Host UI after operator approval.
- Or abandon the chain and start a new monitor session for unrelated work.
