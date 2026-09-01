# Bootstrap scope

Bootstrap composes the plugin; it does not implement features. `ModCompositionRoot.cs` is
the only production file that enumerates all features. Keep registration explicit and do
not restore assembly scanning or assembly-wide Harmony patching.

Touch feature folders only when the user requested their implementation, not while merely
registering or reordering modules.
