# Governed Command Proof Evidence

This note records the first proof pass for the governed command output reducer. The goal is not to prove the architecture by assertion; it is to measure whether noisy command/log output can be reduced before entering model context while preserving actionable signal and full-output recovery.

## Measured Inputs

Measurements were taken from real local runtime files on 2026-06-13. The AIMonitor case used a bounded tail from the 7.2 MB NDJSON log to avoid turning proof collection into another context spill.

| Case | Source | Raw chars | Visible chars | Reduction | Lines | Warnings |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| codexui-startup | `runtime/codexui-startup.out.log` | 26,659 | 2,015 | 92.4% | 207 | `OutputTruncated` |
| aimonitor-ndjson-tail | `runtime/logs/aimonitor.ndjson` tail | 2,067,339 | 20,977 | 99.0% | 2,000 | `OutputTruncated` |
| runtime-working-read | monitor Working candidate path | 221 | 221 | 0.0% | 12 | `RuntimeWorkingRead` |

## Retained Signal

The startup log retained the important operational facts without needing the full log in context: the app was listening on `http://localhost:5000`, startup completed, and the log contained repeated unhandled JSON exceptions from ASP.NET Core request handling.

The runtime-working case proves a separate safety signal: even when output is tiny and should not be truncated, the command target itself should warn that the agent is reading monitor-owned Working state instead of watched source truth.

## What This Proves

The current reducer policy can remove most repeated log bulk before model context:

- 92.4% reduction on a normal app startup log.
- 99.0% reduction on a bounded high-volume telemetry/log slice.
- Explicit warning when a command targets `runtime/.../working`.

The proof is still incomplete until the reducer is wired into the actual command execution path. Today, proving it required ad hoc shell measurement, which is itself evidence that command routing needs a first-class governed surface.

## Next Gate

The next implementation step should wire governed command execution so every shell/build/search/read command records:

- raw output bytes and visible output bytes;
- reduction percentage;
- command kind and output mode;
- warning codes;
- full-output artifact path under `runtime/tool-logs` when output is reduced.

That telemetry should appear in the app so future proof is visible from normal workflow instead of requiring temporary scripts or manual log slicing.
