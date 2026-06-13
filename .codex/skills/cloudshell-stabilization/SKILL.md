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
- `TODO.md`
- `docs/system-design-guidelines.md`
- `docs/domain-model.md`
- `docs/artifact-implementation-guidelines.md`
- relevant files under `docs/proposals/` when the stabilization changes
  proposal status, scope, or remaining tasks

Then inspect the current code and tests around the failing or weak behavior.

## Workflow

1. Reproduce or characterize the behavior gap.
2. Identify the owning layer and add or update tests there:
   - Control Plane service tests for internal resource behavior
   - client/API contract tests for HTTP shape, auth, error responses, and
     remote mapping
   - sample tests for hosted scenarios
   - abstraction tests for extension DSL and public contracts
3. For resource-type stabilization, check the full implementation chain in
   `docs/artifact-implementation-guidelines.md`: contribution, provider,
   projected shape, Control Plane behavior, authoring surfaces, API/client
   projection, shell UI, tests, samples, and docs.
4. Fix the smallest implementation surface that owns the behavior.
5. Prefer stable validation messages and ProblemDetails over leaked runtime
   exception details.
6. Prefer result objects or diagnostics for expected domain validation
   outcomes; reserve exceptions for programmer errors or boundary adapters that
   must translate invalid commands into API errors.
7. Update `docs/progress.md` when the stabilized behavior changes MVP status,
   next priorities, or verification expectations.
8. Treat `docs/roadmap.md` as authoritative for milestone scope and
   `docs/proposals/README.md` as authoritative for proposal status. Keep those
   files, `docs/progress.md`, `TODO.md`, and the relevant proposal documents in
   sync when stabilization completes proposal work, changes proposal order,
   changes MVP scope, or creates new remaining tasks.
9. Run the verification baseline from `docs/progress.md` before committing
   cross-boundary stabilization work.

## Stabilization priorities

Focus first on:

- Resource Manager behavior across resource states.
- Resource action and capability signaling.
- Registration, dependency, group, and parent/child validation.
- Split-hosting and combined-hosting sample viability.
- API/OpenAPI compatibility with the intended domain projection.
