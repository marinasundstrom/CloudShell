# CloudShell Development Workflow

This document describes the expected high-level development workflow for
making changes in CloudShell. It applies to humans and agents. Repo-local
Codex skills can guide a specific agent through feature or stabilization work,
but this document defines the shared project procedure.

CloudShell changes should land as small, coherent slices. A slice is one
reviewable unit of behavior, documentation, sample work, or stabilization that
can be verified and committed on its own.

## Workflow

1. Understand the goal and owning layer.

   Read the relevant goal, roadmap, domain, proposal, resource, or provider
   documentation before editing. Decide whether the change belongs to the
   public abstractions, Control Plane, API/client projection, provider,
   Resource Manager UI, sample, or documentation.

2. Make the change in a focused slice.

   Keep edits scoped to the layer and behavior being changed. Avoid unrelated
   refactors, formatting churn, or compatibility work that does not serve the
   current slice. When the work is larger than one coherent change, split it
   into sequential slices and commit each one separately.

3. Add or update tests when behavior changes.

   Test at the layer that owns the behavior:

   - abstraction tests for public DSL, resource model, and extension contracts
   - Control Plane tests for state, validation, authorization, and procedures
   - API/client tests for HTTP shape, errors, hypermedia, and remote mapping
   - provider tests for provider-owned projection or runtime behavior
   - sample tests for supported hosting scenarios

   Docs-only changes do not need product tests, but still need a diff check.

4. Run verification.

   Start with targeted builds or tests for the changed area. For cross-boundary
   changes, run the broader baseline from `AGENTS.md`. Before committing any
   slice, run:

   ```bash
   git diff --check
   ```

5. Update documentation when applicable.

   Update docs in the same slice when behavior, concepts, public APIs,
   resource types, samples, or MVP priorities change. Keep proposal documents
   and `docs/roadmap.md` aligned when scope or task order changes.

6. Update the changelog.

   Every landed implementation, stabilization, sample, or documentation slice
   should update `CHANGELOG.md` with a dated entry under the appropriate type
   of change.

7. Update ADR when a durable decision changes.

   Use `ADR.md` for architecture and product decisions that should outlive the
   implementation slice. Link changelog entries to ADR entries when the change
   depends on a recorded decision.

8. Commit and push the slice.

   Commit after verification passes and the worktree contains only the current
   slice. Use a concise commit message that names the result, then push the
   branch. Do not batch unrelated slices into one commit.

## Agent-Specific Guidance

Agents should follow this document as the source of truth for change procedure.
Agent-specific instructions live in [AGENTS.md](AGENTS.md), and repo-local
skills provide focused guidance for feature development and stabilization:

- `.codex/skills/cloudshell-feature-development/SKILL.md`
- `.codex/skills/cloudshell-stabilization/SKILL.md`

If a skill overlaps with this document, treat the skill as implementation
guidance for that kind of task and this document as the shared workflow. Keep
skills concise and update the project docs they reference instead of copying
large procedural sections into each skill.

## Practical Expectations

- Prefer incremental progress over broad rewrites.
- Keep the resource model domain-shaped and avoid UI-only or transport-only
  concepts when a domain concept is needed.
- Keep provider-owned runtime/configuration state behind provider contracts.
- Never project secrets into resources, logs, diagnostics, or documentation.
- Preserve samples as working product proofs; update them when public
  conventions change.
- Record intentionally deferred work in the relevant proposal or roadmap
  instead of leaving it implicit.
