# Infrastructure scope

Infrastructure contains adapters to shared external systems such as Nuclear Option APIs,
Unity/Mirage seams, diagnostics, and cached reflection. It must not contain feature-owned
messages, gameplay decisions, feature settings, or presentation policy.

Keep compatibility probes bounded and cached. A missing optional capability disables the
owning module or action without repeated reflection or scene-wide fallback scans.
