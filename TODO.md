# TODO

This is the current task queue. Keep `docs/progress.md` as the living
tracker for completed decisions and broader MVP status.

See also: [Progress](docs/progress.md) for completed work, current MVP focus,
verification baseline, and broader priorities.

## Resource Management

- [ ] Add resource type/class consistency validation or diagnostics.
  - Compare `ResourceTypeContribution.ResourceClass`, creation metadata,
    declaration metadata, and provider-projected `Resource.ResourceClass`.
  - Add focused tests for built-in providers.
- [ ] Improve generated resource details.
  - Link dependencies, parents, and child resources.
  - Add endpoint copy/open affordances.
  - Surface health status, logs, and action capability reasons.
- [ ] Define attribute conventions.
  - Stable names, non-secret rule, display behavior, and provider guidance.
  - Decide whether string-only attributes are enough for MVP.
- [ ] Align templates/import/export with the uniform `Resource` projection and
  provider-owned configuration.

## Resource Manager Stabilization

- [ ] Add dependency auto-start failure details.
- [ ] Expand sample tests for hypermedia resource actions.
- [ ] Keep OpenAPI output aligned with the domain-shaped resource projection.
