---
name: cloudshell-stabilization
description: Use when stabilizing CloudShell behavior, closing MVP gaps, hardening validation, fixing resource manager state behavior, improving tests, or making samples reliable.
---

# CloudShell Stabilization

Use this workflow for MVP hardening, regression fixes, validation gaps, state
coverage, sample reliability, and API/client contract stability.

## Required context

Read these first:

- `docs/goal.md`
- `CONTRIBUTIONS.md`
- `docs/features.md`
- `ADR.md`
- `CHANGELOG.md`
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
7. Update `ADR.md` when stabilization changes a durable product or
   architecture decision. Update `CHANGELOG.md` when stabilized behavior lands
   or verification expectations change.
8. Treat `docs/roadmap.md` as authoritative for milestone scope and the
   current task queue, and `docs/proposals/README.md` as authoritative for
   proposal status. Keep those files, `ADR.md`, `CHANGELOG.md`, and the
   relevant proposal documents in sync when stabilization completes proposal
   work, changes proposal order, changes MVP scope, or creates new remaining
   tasks.
9. When stabilization completes or verifies implemented behavior that was
   described in a proposal, move or port the durable behavior and concrete
   details into the relevant feature or specification docs. If those docs
   already describe the implementation, link to them from the proposal and
   remove duplicated landed detail. Keep proposals focused on remaining
   stabilization work, open decisions, migration tasks, and deferred ideas.
   Verify proposal claims against the current code before converting them into
   feature/spec documentation.
10. When stabilization affects provider, extension, launcher, or language SDK
   parity, update the feature/spec docs with the required behavior and any
   intentional non-parity. Include contracts, authoring surfaces,
   API/client/UI projection, diagnostics, security, persistence/lifecycle
   behavior, and known gaps so future integrations can stay aligned.
11. Review proposal Mermaid diagrams when stabilizing or documenting completed
   behavior. Move valid current diagrams into feature/spec docs, update stale
   diagrams before moving them, and leave proposal diagrams only for active
   stabilization options, migration choices, or deferred ideas.
12. Follow `CONTRIBUTIONS.md` for verification, changelog, ADR, commit, and
   push expectations. Run the verification baseline from `AGENTS.md` before
   committing cross-boundary stabilization work. Commit only files owned by the
   current chat or thread. Leave pure documentation changes uncommitted and
   unpushed unless the user explicitly asks to land the reviewed documentation
   slice.

## Stabilization priorities

Focus first on:

- Resource Manager behavior across resource states.
- Resource action and capability signaling.
- Registration, dependency, group, and parent/child validation.
- Split-hosting and combined-hosting sample viability.
- API/OpenAPI compatibility with the intended domain projection.
