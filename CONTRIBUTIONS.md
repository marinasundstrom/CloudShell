# CloudShell Development Workflow

This document defines the shared workflow for CloudShell changes. Repo-local
skills may provide task-specific guidance, but this document is the source of
truth for how work is performed and landed.

CloudShell changes should be delivered as small, coherent slices that can be
reviewed and verified independently.

Use [Refactoring tracker](docs/refactoring.md) for active cross-cutting
refactoring task lists. It is a living work tracker for boundary and ownership
cleanup; durable decisions still belong in `ADR.md`, and landed behavior still
belongs in `CHANGELOG.md`.

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

   Tests that launch external executables, run sample hosts, or depend on
   mutable sample project layout are marked with `Category=Integration`. Tests
   that require a live Docker daemon also carry `Category=DockerIntegration`.
   The sample-host smoke tests are assigned to the serialized
   `Sample smoke tests` xUnit collection because they bind local ports, start
   child processes, mutate sample data directories, write temporary runtime
   files, and may create fixed-name Docker containers or compose projects.
   Sample adapter tests that use recording runners, in-memory graphs, and
   unique temporary directories do not need that collection and are safe to run
   in parallel with ordinary unit tests.
   Exclude executable-backed integration tests from routine local runs with:

   ```bash
   dotnet test CloudShell.slnx --filter "Category!=Integration&Category!=DockerIntegration"
   ```

   Run Docker-dependent integration tests explicitly with:

   ```bash
   dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --filter "Category=DockerIntegration"
   ```

   If Docker-dependent tests fail before reaching CloudShell behavior with a
   message such as `Cannot connect to the Docker daemon`, treat that as a local
   host/runtime prerequisite failure rather than a product regression. Verify
   with `docker info`. Docker Desktop can leave a stuck CLI or backend process
   after the daemon crashes; if `docker info` cannot connect and a Docker
   process is blocking shutdown or restart, terminate the stuck Docker process
   and restart Docker before rerunning `Category=DockerIntegration` tests. Do
   not use Docker-backed sample tests as the commit gate while the daemon is
   unhealthy; record the skipped/blocked verification in the handoff.

5. **Update project documentation**

   Update documentation when behavior, APIs, concepts, resource types, samples,
   architecture decisions, or roadmap priorities change.

   When implementation work has landed, move or port the durable behavior and
   concrete implementation details into the relevant feature or specification
   docs. Do not leave completed behavior as the main content of a proposal.
   Proposals are working documents for incremental/open work, active design
   questions, migration trackers, and deferred decisions. If a proposal
   contains current implementation details, verify them against the code before
   moving them, then replace the proposal section with links to the feature
   docs and a focused list of remaining decisions or tasks. If the feature or
   specification docs were written as part of the implementation from the
   start, keep the proposal concise and link to those docs instead of copying
   implementation detail back into the proposal. Use
   `docs/features.md` to find or add the canonical feature/specification home
   for current behavior.

   For extensible behavior, document the parity contract that future providers,
   Resource Manager UI extensions, shell extensions, launchers, and language
   SDKs must follow. Capture the owning contracts, resource model shape,
   authoring surfaces, runtime boundaries, API/client projection, UI
   projection, security constraints, diagnostics, persistence/lifecycle
   behavior, and known gaps. If a provider or launcher intentionally does not
   support part of the feature, record that non-parity in the feature/spec docs
   or the relevant proposal so it is visible during roadmap planning.

   Review Mermaid diagrams and other architecture diagrams during the same
   documentation pass. Valid diagrams that explain implemented/current behavior
   belong in feature or specification docs. Stale diagrams should be corrected
   or replaced before being moved. Proposal diagrams should remain only when
   they explain active design work, migration options, or deferred ideas.

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
