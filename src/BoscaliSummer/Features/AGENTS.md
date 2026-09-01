# Feature source scope

Choose exactly one immediate feature folder as the working scope. Read that folder's
`AGENTS.md` before its implementation. Do not inspect sibling feature implementations.

A feature folder owns all of its configuration, patches, runtime logic, networking,
presentation, and assets. It may depend on `Framework` and `Infrastructure`; it may not
import a sibling feature implementation. Cross-feature behavior goes through a narrow
`Framework/Contracts` interface and an explicit feature dependency only when startup truly
requires the provider.

When adding a feature, create its folder and local `AGENTS.md`, add one `IModFeature`,
register it in `Bootstrap/ModCompositionRoot.cs`, and add a matching test folder. Do not
create empty placeholder subfolders.
