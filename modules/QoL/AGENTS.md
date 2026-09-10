# Quality of life module

Owns local HUD/camera conveniences, observation marks, settings and patches. Must work
without Progression or Support, and must not send network messages or issue gameplay orders.

Borrow native camera textures; never render, release, or take ownership of them. Camera
framing must yield to native controls and restore modified state on transitions/teardown.
Persist observation coordinates in GlobalPosition space, with bounded lifetime and
ownship/faction/scene invalidation. A ray miss or a saturated buffer is not a usable mark.
Exclude ownship parts, but never skip intervening terrain. Marks are observations, not laser
locks or faction tracking. Use only faction-tracking timestamps for contact age.

Expose the current mark and the HUD toggle through narrow Framework contracts; Support owns
selection, confirmation, cost, authority and execution. Preserve the legacy `Avionics.*`
config keys when moving their ownership here. All hotkeys respect text entry and pause.
