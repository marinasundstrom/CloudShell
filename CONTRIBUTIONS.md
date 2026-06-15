# CloudShell Development Workflow

This document defines the shared workflow for CloudShell changes. Repo-local
skills may provide task-specific guidance, but this document is the source of
truth for how work is performed and landed.

CloudShell changes should be delivered as small, coherent slices that can be
reviewed and verified independently.

## Workflow

1. **Understand the goal and owning layer**

   Read the relevant documentation before making changes. Determine whether the
   work belongs to public abstractions, the Control Plane, API/client
   projection, providers, Resource Manager UI, samples, or documentation.

   Inspect the worktree before editing. Treat existing changes as owned by
   another contributor unless they were made in the current conversation or
   thread.

2. **Classify the slice**

   * **Implementation slice**: code, tests, samples, generated artifacts, or
     documentation directly tied to a behavior change.
   * **Documentation slice**: proposals, planning, architecture notes,
     workflow updates, roadmaps, or other standalone documentation.

   Implementation slices are verified, committed, and pushed when complete.
   Documentation slices are review-first and remain uncommitted unless
   explicitly approved for landing.

3. **Make a focused change**

   Keep edits scoped to a single behavior or concern. Avoid unrelated
   refactoring or formatting changes. Split larger efforts into multiple
   independently reviewable slices.

4. **Verify the change**

   Add or update tests when behavior changes and verify at the layer that owns
   the behavior. Run targeted validation first and broader validation when the
   change crosses boundaries.

   Before handing off or committing any slice, run:

   ```bash
   git diff --check
   ```

5. **Update project documentation**

   Update documentation when behavior, APIs, concepts, resource types, samples,
   architecture decisions, or roadmap priorities change.

   * Update `CHANGELOG.md` for landed implementation slices and approved
     documentation changes.
   * Update `ADR.md` when a durable architectural or product decision changes.

6. **Commit and push when appropriate**

   For implementation slices, commit after verification passes and stage only
   files owned by the current work. Use a concise commit message and avoid
   bundling unrelated changes.

   Documentation slices should remain uncommitted until explicitly approved for
   landing.

## Ownership and Parallel Work

Multiple contributors may work in the repository simultaneously.

Before staging or committing:

* Run `git status --short`.
* Review files before staging.
* Stage files explicitly by path.
* Exclude unrelated changes.
* Split mixed diffs or ask for guidance.
* Never revert another contributor's changes solely to clean your slice.

## Agent Guidance

Agent-specific instructions live in `AGENTS.md` and repo-local skills.

* `.codex/skills/cloudshell-feature-development/SKILL.md`
* `.codex/skills/cloudshell-stabilization/SKILL.md`

If guidance overlaps, treat this document as the authoritative workflow and
skills as task-specific implementation guidance.

## Expectations

* Prefer incremental progress over broad rewrites.
* Keep the resource model domain-driven.
* Keep provider-owned state behind provider contracts.
* Never expose secrets in resources, logs, diagnostics, or documentation.
* Keep samples functional and aligned with current conventions.
* Record deferred work in proposals or the roadmap.