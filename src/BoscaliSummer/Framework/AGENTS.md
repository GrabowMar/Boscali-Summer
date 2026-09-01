# Framework scope

Framework changes affect every module, so keep them small and behavior-neutral. Work only
here and in `tests/BoscaliSummer.Tests/Framework` unless the request explicitly includes a
consumer migration.

This folder may contain feature hosting, dependency ordering, lifecycle primitives, and
narrow contracts used by at least two current modules. It must not reference a concrete
feature namespace, feature setting, or feature policy. Keep contracts read-only and use
the least game-specific identity practical for the proven seam. Changes must preserve
transactional installation and dependant-first teardown.
