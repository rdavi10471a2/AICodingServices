# Troubleshooting Dashboard

Use when verifying that Claude/Codex is using the intended MCP surfaces.

## Rules

- The CodexUI hub live stream is the first-class signal.
- Logs are durable receipts, not the primary live view.
- A healthy C# edit run shows CodingServices index/source-map/edit workflow traffic.
- If semantic work starts with grep while CodingServices index/source-map tools are available, treat it as an audit/correction cue, not an automatic hard failure. Correction: call `find_indexed_symbols` or `query_solution_index` for the same target and confirm results before continuing.
- If grep/text search appears after pre-merge/build diagnostics found a missing call site, treat it as an allowed diagnostic fallback. The next healthy signal is staging the missed file into the same monitor session, not continuing with ad hoc text edits.
- For high-risk watched-source edits, the real gate is CodingServices staging and decision classification.

## Healthy Signals

```text
get_monitor_status
get_workflow_status
query_solution_index / find_indexed_symbols
find_indexed_references / find_indexed_callers
get_source_map
get_symbol
submit_symbol / add_symbol / remove_symbol
stage_candidate_for_review
launch_staged_diff
record_diff_decision
```
