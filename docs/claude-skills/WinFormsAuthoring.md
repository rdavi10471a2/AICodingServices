# WinForms Authoring

Use when building or changing Windows Forms code in CodingServices or a watched project.

## Rule

Prefer code-first WinForms setup unless the Visual Studio designer is intentionally part of the workflow. Forms should coordinate controls; a model, presenter, service, or state object should own data and behavior when the screen does more than trivial display.

## Designer Discipline

- Prefer non-designer setup for CodingServices-style tools and generated/agent-authored UI.
- Avoid casual `.Designer.cs` and `.resx` churn when hand-authored layout is clearer and more stable.
- Use appropriate attributes so Visual Studio does not mistake helper classes, models, presenters, or code-first controls for designer/resource-backed surfaces.
- Use attributes such as `DesignerCategory`, `DesignTimeVisible`, and `ToolboxItem` when they clarify design-time intent.
- Avoid removing existing designer support from old screens unless the task is explicitly to convert that screen.
- Treat designer-generated code as owned by the designer when a screen already uses it.

## Layout Timing

- Set `SplitContainer.SplitterDistance` only after the form/control has been constructed and given real size.
- Prefer `Load`, `Shown`, or an explicit post-layout method for splitter distances and other size-dependent layout.
- Avoid relying on constructor-time control sizes for layout decisions unless the size is explicitly assigned in code first.
- Separate layout construction, state loading, and event wiring enough that each can be read independently.

## Model Behind The Form

- Use a model, presenter, or service when the form has loaded data, selection, filtering, commands, persistence, or workflow state.
- Keep event handlers short: read the UI input, call the model/service, then update the controls.
- Avoid putting SQL, file IO, indexing logic, or workflow orchestration directly in the form.
- Name model/presenter objects by the screen or workflow they represent.
- Keep state transitions testable without launching the designer when practical.

## Related

`ReadableCSharpAuthoring.md`, `RepositoryAuthoring.md`, `SQLiteDapperAuthoring.md`.
