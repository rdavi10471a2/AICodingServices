# Column Reconciliation TODO

This file tracks stabilization and refactor notes for `ColumnReconciliationDialog`. Keep the accepted behavior in `README.md`; keep speculative or future work here.

## Current Posture

- Keep the dialog monolithic while workflow behavior is still being validated.
- Treat the current component as a unified decision surface, not a failed split.
- Prefer small stabilization passes over component extraction until the state shape stops changing.
- Avoid DI/state-container ceremony for dialog-local state.

## Near-Term Stabilization

- Continue validating the cached `FilteredCandidates`, `CountChips`, and `RefreshViewState()` path under real filter/search/navigation use.
- Watch selection behavior when filters change; selection should remain stable unless the selected candidate is no longer visible.
- Keep `@key` on repeated candidate, chip, source, and comparison rows.
- If more state bugs appear, centralize more mutations through the existing refresh path before splitting components.

## Layout Follow-Up

The current CSS still contains:

```css
.cr-section-scroll {
    max-height: calc(90vh - 390px);
}
```

That works, but it is layout compensation. A future layout pass should remove the `vh` clamp and make the dialog height chain explicit.

Target direction:

```css
.column-reconciliation {
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 0;
}

.cr-filter-band,
.cr-footer {
    flex: 0 0 auto;
}

.cr-body,
.cr-detail-scroll,
.cr-section-scroll {
    flex: 1 1 auto;
    min-height: 0;
    overflow: auto;
}

.cr-navigation,
.cr-panel,
.cr-detail,
.cr-section {
    min-height: 0;
}
```

Before changing this, screenshot-test:

- Selector hidden.
- Selector shown.
- Business Values tab with resized text areas.
- Source Mapping Check tab with wide table content.
- Added, removed, needs review, inherited, declared, and blank filters.

Also verify Radzen dialog wrapper behavior. If needed, scope any `.rz-dialog-content` height/flex override to this dialog rather than changing all dialogs globally.

## CSS Polish

- Continue replacing repeated local colors with `--cr-*` tokens as the CSS evolves.
- Keep focus-visible states for keyboard users.
- Do not remove `::deep` textarea rules until Radzen text area sizing can be proven stable without them.
- Avoid broad layout rewrites unless they are tested against the fixed footer/internal scroll behavior.

## Future Component Split

Do not split everything at once. When behavior is stable, split by responsibility:

1. `ColumnReconciliationDialog` remains parent/orchestrator.
2. `FilterBand` owns status filter, count chips, and selector toggle.
3. `CandidateSelector` owns candidate search/list/selection rendering.
4. `SourceExplorer` owns source tree/source rows after its input contract is boring.
5. `ColumnDecisionPanel` owns selected-column tabs, business value decision, and source mapping display.

Extract `ColumnDecisionPanel` last because it carries the hardest semantics.

## Domain Contracts Before Splitting

Before extracting child components, define stable local view-model contracts:

- `ReconciliationStatus`
- `ReconciliationCandidate`
- `BusinessRow`
- `ComparisonRow`
- Count chip model
- Optional dialog-local state object if parameters start exploding

Use a plain local state/view-model object if needed. Do not introduce a service unless state must outlive a single dialog instance.

## Apply Merge Cleanup

`ApplyMerge()` is the main business-logic hotspot. Before moving it to a service, first split it into private helper methods inside the same component or code-behind:

- Apply removed candidate.
- Apply added candidate.
- Apply existing candidate parser structure.
- Apply business value choice.
- Apply inheritance result.

Only extract beyond the component after the rules are stable and testable.

## Accessibility Follow-Up

- Verify tab order after any layout/component split.
- Consider arrow-key navigation for candidate list.
- Ensure radio-card choices remain keyboard reachable and screen-reader understandable.
- Keep visible focus states.

## Later Policy Idea

If composed or IG views are eventually treated as parser-owned shape only, consider replacing `Review Merge` with `Sync View` for non-base views:

- Clear or overwrite stored column shape from current parser results.
- Let `ApplyColumnSemanticInheritance` restore business meaning.
- Do not offer business-value merge decisions for inherited/composed rows.

Leave this as a policy option until real-world testing proves it is needed.
