---
name: cloudshell-feature-development
description: Use when adding a new CloudShell product feature, API capability, resource model concept, extension point, provider behavior, sample, or UI workflow. Guides Codex to design against CloudShell's domain boundaries and update tests/docs.
---

# CloudShell Feature Development

Use this workflow for new product behavior, new API/domain concepts, new
extension points, provider capabilities, samples, or UI workflows.

## Required context

Read these first:

- `docs/system-design-guidelines.md`
- `docs/domain-model.md`
- `docs/progress.md`
- `TODO.md`

Then inspect the relevant implementation and tests before editing.

## Workflow

1. Identify the owning layer:
   - domain abstraction
   - Control Plane service
   - HTTP/OpenAPI contract
   - remote client adapter
   - shell UI
   - provider/extension
   - sample
2. Keep resource concepts domain-shaped. Do not introduce UI terminology into
   resource/domain contracts.
3. Prefer hypermedia affordances on projected artifacts when an API response
   exposes an operation that can be taken on that artifact.
4. Add focused tests at the owning layer. Add contract tests when an API shape
   or remote adapter changes.
5. Update docs when a feature changes system concepts, hosting guidance, API
   shape, or MVP progress.
6. Update `TODO.md` when the feature changes the current task queue.
7. Run the relevant narrow tests first, then the verification baseline from
   `docs/progress.md` for cross-boundary changes.

## Output expectations

Keep changes scoped. Mention any intentionally deferred behavior in
`docs/progress.md` and `TODO.md` instead of leaving it implicit.
