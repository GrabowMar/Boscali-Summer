"""Independent Blender mesh audit of the authored pressure casing and runtime export."""
import bpy
import gzip
import json
import math
import struct
import sys
from pathlib import Path

root=Path(__file__).resolve().parents[1]
sys.path.append("C:/Users/marci/.codex/skills/blender-expert/scripts")
import bx_audit

bpy.ops.wm.open_mainfile(filepath=str(root/"Source"/"SA74N.blend"))
report=bx_audit.audit(bpy.data.objects["Main pressure casing"])
result={"casing":report,"casing_verdict":bx_audit.verdict(report,deforming=False)}
result["accepted_source_topology"]=["The flat rear endcap is an n-gon in editable source and triangulated in the runtime export.","The rounded nose terminates in one 96-edge triangle-fan pole on this rigid nondeforming asset."]
result["geometry_checks_passed"]=all(report[key]==0 for key in ["non_manifold_edges","loose_verts","loose_edges","zero_area_faces","duplicate_verts","flipped_faces","self_intersections"]) and not report["boundary_loops"]
assert result["geometry_checks_passed"],result
with gzip.open(root/"sa74n.mesh.gz","rb") as f:
    assert struct.unpack("<iii",f.read(12))==(0x34374153,1,5)
result["export_header_valid"]=True
out=root.parents[2]/"reports"/"sa74n"/"art"/"mesh-audit.json"
out.write_text(json.dumps(result,indent=2))
print("RESULT",json.dumps({"report":str(out),"casing_verdict":result["casing_verdict"]}))
