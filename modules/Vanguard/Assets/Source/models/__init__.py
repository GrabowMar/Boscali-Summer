"""Vanguard model registry. One module per model in this folder; BuildVanguard / ValidateVanguard / RenderVanguard
derive every per-model table (atlas size, joints, build order) from here. Importing this package needs no bpy.

A model module <Name>.py defines:
    NAME     "Name": must equal the file name; becomes <Name>.fbx, <Name>_Albedo.png ... and material Skin_<Name>.
    TEX      atlas side in px (512 | 1024 | 2048); VanguardBuilder reads the real size from the PNG.
    CLASS    "munition" | "pod" | "drone" | "service": picks the LOD0 triangle budget (CLASS_TRIS).
    JOINTS   {joint mesh name: (parent joint name or None, rest pivot (x, y, z) in Blender metres or None)};
             ValidateVanguard checks them in the built .blend. Joint names are runtime API.
    DEPENDS  names of models whose .blend must be built first (e.g. a pod that previews its munition).
    build(g) builds the geometry with the BuildVanguard helper module `g` (g.mats(NAME), g.loft, g.box, g.seg ...)
             and returns the spec dict (see BuildVanguard.compose / VanguardSurface.surface_layers).
Optional: UV_SHAPE = "AABB" (UV pack shape, default "CONCAVE").
Optional spec keys owned by the model: "service_stencils" (extra flank text), "surface" (a hook
function(S) editing base colour / metallic / smoothness / height in VanguardSurface.surface_layers).

Parallel work: every tool imports all modules here, but one that fails to import (someone is mid-edit) is only skipped
with a warning (BROKEN); a build or validation that names it, or runs every model, fails with its real error.
"""
import importlib
import os
import sys

sys.dont_write_bytecode = True  # never litter Source/models with __pycache__

CLASS_TRIS = {"munition": 4000, "pod": 6000, "drone": 12000, "service": 16000}  # LOD0 triangle budgets
_HERE = os.path.dirname(os.path.abspath(__file__))


def _check(mod, stem):
    assert mod.NAME == stem, f"models/{stem}.py: NAME is {mod.NAME!r}, must equal the file name"
    assert mod.TEX in (512, 1024, 2048), f"{stem}: TEX {mod.TEX} must be 512, 1024 or 2048"
    assert mod.CLASS in CLASS_TRIS, f"{stem}: CLASS {mod.CLASS!r} must be one of {sorted(CLASS_TRIS)}"
    assert isinstance(mod.JOINTS, dict), f"{stem}: JOINTS must be a dict"
    for joint, (parent, pivot) in mod.JOINTS.items():
        assert parent is None or parent in mod.JOINTS, f"{stem}: joint {joint} has unknown parent {parent}"
        assert pivot is None or len(pivot) == 3, f"{stem}: joint {joint} pivot must be (x, y, z) or None"
    assert isinstance(mod.DEPENDS, tuple), f"{stem}: DEPENDS must be a tuple of model names"
    assert callable(mod.build), f"{stem}: build(g) missing"


BROKEN = {}  # stem -> "ExcType: message" for modules that failed to import or break the contract


def discover():
    """{name: module} for every importable models/<Name>.py: sorted by name, dependencies first.

    A module that does not import (usually someone is mid-edit on it) or whose DEPENDS are unusable is left out and
    recorded in BROKEN, so a build of another model still runs. Call require() to fail on the ones you need."""
    BROKEN.clear()
    found = {}
    for entry in sorted(os.listdir(_HERE)):
        stem, ext = os.path.splitext(entry)
        if ext != ".py" or stem.startswith("_"):
            continue
        try:
            mod = importlib.import_module(f"{__name__}.{stem}")
            _check(mod, stem)
            found[stem] = mod
        except Exception as error:  # SyntaxError, bad contract, failing import ...
            BROKEN[stem] = f"{type(error).__name__}: {error}"
            sys.modules.pop(f"{__name__}.{stem}", None)
    ordered = {}

    def visit(name, trail=()):
        assert name not in trail, "DEPENDS cycle: " + " -> ".join(trail + (name,))
        assert name in found, (f"{trail[-1] if trail else '?'} depends on {name}, which is "
                               + (f"broken ({BROKEN[name]})" if name in BROKEN else "not a model"))
        for dep in found[name].DEPENDS:
            visit(dep, trail + (name,))
        ordered.setdefault(name, found[name])

    for name in found:
        try:
            visit(name)
        except AssertionError as error:
            BROKEN[name] = str(error)
    return ordered


def require(names=None):
    """Raise with the real error if any of `names` (default: every model) failed in discover()."""
    bad = {n: why for n, why in BROKEN.items() if names is None or n in names}
    if bad:
        raise RuntimeError("broken model module(s): " + "; ".join(f"models/{n}.py: {why}" for n, why in bad.items()))
