# CloudShell Development Workflow

This document describes the expected high-level development workflow for
making changes in CloudShell. It applies to humans and agents. Repo-local
Codex skills can guide a specific agent through feature or stabilization work,
but this document defines the shared project procedure.

CloudShell changes should land as small, coherent slices. A slice is one
reviewable unit of feature behavior, stabilization, sample work, or
documentation that can be verified on its own. Implementation slices and pure
documentation slices have different commit expectations for agents, described
below.

## Workflow

1. Understand the goal and owning layer.

   Read the relevant goal, roadmap, domain, proposal, resource, or provider
   documentation before editing. Decide whether the change belongs to the
   public abstractions, Control Plane, API/client projection, provider,
   Resource Manager UI, sample, or documentation.

   Before editing, inspect the worktree. Treat existing changes as owned by
   another person, agent, or thread unless you made them in the current
   conversation. Do not stage, amend, revert, or commit changes you do not own.

2. Classify the slice.

   Use an implementation slice for product behavior, code, tests, samples,
   generated contracts, or documentation that directly explains the same
   implementation change. Implementation slices are expected to be verified,
   committed, and pushed when complete.

   Use a pure documentation slice for proposals, planning, workflow updates,
   architecture notes, resource docs, roadmap changes, or other text-only work
   that is not paired with a code/sample/test implementation. Pure
   documentation changes authored by agents are review-first: leave them
   uncommitted and unpushed unless the user explicitly asks to land that exact
   reviewed documentation slice.

3. Make the change in a focused slice.

   Keep edits scoped to the layer and behavior being changed. Avoid unrelated
   refactors, formatting churn, or compatibility work that does not serve the
   current slice. When the work is larger than one coherent change, split it
   into sequential slices and commit each one separately. Larger feature slices
   or roadmap targets should still land through smaller sub-slices when each
   sub-slice is contained enough to verify and review on its own.

4. Add or update tests when behavior changes.

   Test at the layer that owns the behavior:

   - abstraction tests for public DSL, resource model, and extension contracts
   - Control Plane tests for state, validation, authorization, and procedures
   - API/client tests for HTTP shape, errors, hypermedia, and remote mapping
   - provider tests for provider-owned projection or runtime behavior
   - sample tests for supported hosting scenarios

   Testing also includes running the software when that is the most direct way
   to verify behavior. Depending on the change, this can mean exercising a Web
   API endpoint, starting a sample host, or walking through a user scenario in
   the Resource Manager UI. When a change affects the UI, prefer verifying the
   running UX so the implemented behavior, layout, and interaction work as
   intended.

   Docs-only changes do not need product tests, but still need a diff check.

5. Run verification.

   Start with targeted builds or tests for the changed area. For cross-boundary
   changes, run the broader baseline from `AGENTS.md`. Before committing any
   slice, run:

   ```bash
   git diff --check
   ```

   For pure documentation slices, run `git diff --check` before handing the
   work back for review.

6. Update documentation when applicable.

   Update docs in the same slice when behavior, concepts, public APIs,
   resource types, samples, or MVP priorities change. Keep proposal documents
   and `docs/roadmap.md` aligned when scope or task order changes.

7. Update the changelog.

   Every landed implementation, stabilization, sample, or reviewed
   documentation slice should update `CHANGELOG.md` with a dated entry under
   the appropriate type of change. A pure documentation draft that is being
   left uncommitted for review does not need a changelog entry until it is
   accepted for landing.

8. Update ADR when a durable decision changes.

   Use `ADR.md` for architecture and product decisions that should outlive the
   implementation slice. Link changelog entries to ADR entries when the change
   depends on a recorded decision.

9. Commit and push the slice when appropriate.

   For implementation slices, commit after verification passes and the staged
   changes contain only files owned by the current conversation or thread. Use
   a concise commit message that names the result, then push the branch. Do not
   batch unrelated slices into one commit.

   For pure documentation slices authored by agents, do not commit or push
   automatically. This is the only slice type that has a blanket
   no-automatic-commit rule. Leave the reviewed diff in the worktree and state
   that it is intentionally uncommitted. Commit only when the user explicitly
   asks to land that documentation slice after review.

## Ownership and Parallel Work

CloudShell work may happen in parallel across several chats, agents, or local
threads. Each contributor is responsible for committing only the changes owned
by their current work.

Before staging or committing:

- Run `git status --short`.
- Inspect any modified files you plan to stage.
- Stage files explicitly by path instead of using broad staging commands.
- Exclude unrelated changes, even when they look useful or are already present
  in the worktree.
- If a file contains both current-thread changes and unrelated edits, split the
  diff or ask for direction before committing.
- Never revert another contributor's changes just to make your slice clean.

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
