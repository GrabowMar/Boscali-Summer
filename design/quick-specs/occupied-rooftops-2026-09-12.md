# Quick Design Spec: Visible occupied rooftops

Type: Addition to UrbanCombat. Date: 2026-09-12.
Method: Game Studios Quick Design structure, Ponytail reuse of native weapons/networking,
UI/UX Pro Max identification through shape as well as colour. No system GDD/index exists;
current UrbanCombat code and module invariants are the baseline. No conflicting roof spec found.

## Problem and design delta

Ordinary occupation hides every renderer of an infantry-bunker proxy inside the shell.
The old flag/mast component is used only on assault perimeter emplacements, not ordinary
occupied buildings. Empty-looking roofs provide no visible warning of an armed position.

Replace that proxy with one visible native emplacement on a supported roof. Roles rotate
MG, AT-145, 23 mm AA across a zone's occupied buildings. Keep vanilla crew, targeting,
weapons and main hitbox; deactivate only the native dugout and grass-blocker children.
Attach three short walls of sandbags and a double-sided faction-coloured flag. These are
local decoration, not armour or independently controlled infantry. Civilian meshes stay
unchanged. Invalid roofs remain unoccupied; ground encampments remain the assault fallback.

## Verified nomodkit evidence

- Live bridge: Unity 2022.3.62f2, GameWorld; 1,272 MapBuilding instances. residential_1b
  has MeshFilter, MeshRenderer and MeshCollider with a separate destruction prefab.
- MapBuilding decompilation: civilian shells support dependent objects; destruction removes
  the shell. Native networked defenses must still use server destruction.
- Spawner decompilation: SpawnBuilding sets transform, HQ, unique name and network start
  position/rotation before ServerObjectManager.Spawn. Existing OnStartClient hook can
  reconstruct cosmetic children for observers and late joiners.
- resources.assets GameObject inventory and native definition log confirm Emplacement1_MG,
  Emplacement1_ATGM, Emplacement1_23mm and Emplacement1_MANPADS.
- MG prefab hierarchy inspection through nomodkit's UnityPy runtime: separate dugout
  (MeshCollider/LOD child) and GrassBlocker_Proxy children; the weapon root has its own
  BoxCollider. Traverse/barrel/crew are separate from the dugout.
- No game assets bundled. Temporary native mesh extraction was only for a local preview;
  original texture and in-game lighting fidelity are not claimed by that preview.

## Rules and budgets

- One combat emplacement per occupied shell; six occupied shells per zone; 96 total.
- BuildingsPerZone controls automatic occupation. Fortification adds one eligible shell
  synchronously, preserving existing defenders and rejecting failed spawns.
- Forty-nine candidate centres per shell; nine rendered-mesh probes verify the same shell, near-horizontal
  surface, height above building centre and at most 0.25 m height spread. At most 128 shells
  per zone pass. Footprint includes cover/flag clearance; no AABB-only roof placement.
- 36 rounded sandbags, pole and flag/hoist: four renderers, fewer than 3,000 vertices per
  defense. No cosmetic colliders, lights or per-frame network traffic.
- Server checks shell/defense lifecycle once per second. Destruction, inactivation or
  changed defense faction clears occupancy and removes the native defense.
- Clients build visuals from native replicated name/HQ/transform; flag colour refreshes
  at 2 Hz. Scene teardown releases owned meshes/materials. Matching builds needed.

## Acceptance

- [x] Release build, pure/architecture tests, PatchProbe and nomod assembly verification.
- [x] Real Unity physics: rotated flat roof accepted; small roof, neighbouring roof support,
  steep roof and overhead obstruction rejected.
- [x] Production mesh checks: idempotent setup, terrain pieces hidden, no cosmetic colliders,
  finite normals, bounded geometry and immediate cleanup visibility.
- [x] Front/back Unity renders inspected with locally extracted native MG geometry.
- [ ] In-game: occupied vs empty at 100/500/1500 m, day/night, MG/AT/AA firing, native LOD,
  both factions; listen host, remote client, late join, capture, collapse and scene reload.

No deployment or game restart performed. Run the Unity fixture with
`tests/BoscaliSummer.Tests/Features/UrbanCombat/Run-RooftopUnityCheck.ps1`;
results and PNGs are written to the reported temporary project directory.

## Follow-up: hidden placements on commercial props

Live garrison inspection confirmed the selected MG already existed with a rooftop name.
Asset inspection identified commercial_2a/2c/3b using box collision proxies. The revised
solver temporarily cooks rendered meshes (default options, no vertex-array reads), searches
a 7x7 grid, and chooses the highest fitting patch. It does not blindly parent a weapon
at an AABB height. Native network roots retain their replicated global position; temporary
query children are always deactivated and removed. No game assets are distributed.
Unity-Technologies' physics-3d-collision skill informed the layer/transform/collider diagnosis:
https://github.com/Unity-Technologies/skills/tree/main/skills/physics-3d-collision

Passed Unity regression fixtures: short collision proxy, disabled renderer, non-readable
mesh, tower above a podium, plus prior obstruction/slope/size checks. Local native mesh
fixtures passed for commercial_2a, commercial_2c, commercial_3b and midrise_1d.
Game-session and multiplayer acceptance still pending; this follow-up was not deployed.

## Player-build regression correction

Player.log confirmed CollisionMeshData failures for non-accessible native meshes, rejecting every roof. The previous Editor non-readable fixture was insufficient: Editor permits cooking that fails in the shipped player. Placement now checks isReadable before cooking, reuses existing cooked mesh colliders, and uses a height-corrected native box footprint for non-readable box-backed props. The box fallback approximates complex roofs and needs in-game visual acceptance. The Unity fixture can now build a standalone player via RooftopUnityCheck.BuildPlayer. Earlier exact rendered-mesh claims above describe the superseded implementation.

Validation: Unity 2022.3.62f3 standalone Windows player exited 0 with 2,363 assertions and no CollisionMeshData errors; Editor passed 2,367 including four local imported building fixtures. Release build, pure/architecture tests, PatchProbe and nomod verification passed (54 patches, 64 reflection lookups). Nuclear Option deployment and visual/multiplayer acceptance remain pending.
