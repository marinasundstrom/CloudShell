---
name: cloudshell-stabilization
description: Use when stabilizing CloudShell behavior, closing MVP gaps, hardening validation, fixing resource manager state behavior, improving tests, or making samples reliable.
---

# CloudShell Stabilization

Use this workflow for MVP hardening, regression fixes, validation gaps, state
coverage, sample reliability, and API/client contract stability.

## Required context

Read these first:

- `docs/progress.md`
- `docs/system-design-guidelines.md`
- `docs/domain-model.md`

Then inspect the current code and tests around the failing or weak behavior.

## Workflow

1. Reproduce or characterize the behavior gap.
2. Identify the owning layer and add or update tests there:
   - Control Plane service tests for internal resource behavior
   - client/API contract tests for HTTP shape, auth, error responses, and
     remote mapping
   - sample tests for hosted scenarios
   - abstraction tests for extension DSL and public contracts
3. Fix the smallest implementation surface that owns the behavior.
4. Prefer stable validation messages and ProblemDetails over leaked runtime
   exception details.
5. Update `docs/progress.md` when the stabilized behavior changes MVP status,
   next priorities, or verification expectations.
6. Run the verification baseline from `docs/progress.md` before committing
   cross-boundary stabilization work.

## Stabilization priorities

Focus first on:

- Resource Manager behavior across resource states.
- Resource action and capability signaling.
- Registration, dependency, group, and parent/child validation.
- Split-hosting and combined-hosting sample viability.
- API/OpenAPI compatibility with the intended domain projection.
