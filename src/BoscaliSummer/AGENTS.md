# Production source scope

Select one child area before editing. Avoid repository-wide searches from this directory.

- `Features/<Feature>` owns player-facing behavior.
- `Framework` owns feature hosting, lifecycle primitives, and proven shared contracts.
- `Infrastructure` owns shared external-system adapters, not feature policy.
- `Bootstrap` and `Configuration` compose modules; they must not absorb module behavior.
- `Core` is for pure helpers with at least two real consumers. Feature-specific helpers
  belong under their feature even when they are pure C#.

Source moves must retain wire-sensitive namespaces and update the module-boundary test,
the patch probe, or embedded-resource paths when applicable.
