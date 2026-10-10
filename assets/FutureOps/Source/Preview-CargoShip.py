"""Close inspection views of the authored ship. Blender --python ... -- <pack>."""
import bpy,sys,math
from pathlib import Path
from mathutils import Vector
pack=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(pack/'Models/CargoShip.blend'))
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=32
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX'; prefs.get_devices()
for device in prefs.devices: device.use=device.type=='OPTIX'
if any(d.use for d in prefs.devices): sc.cycles.device='GPU'
sc.render.resolution_x=1600; sc.render.resolution_y=1000; sc.render.resolution_percentage=100
world=bpy.data.worlds.new('Inspection environment'); world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.19,.24,.30,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.7; sc.world=world
bpy.ops.object.light_add(type='AREA',location=(-40,-45,65)); sun=bpy.context.object
sun.data.energy=500000; sun.data.size=60; sun.rotation_euler=(Vector((0,0,5))-sun.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(); cam=bpy.context.object; cam.data.type='ORTHO'; sc.camera=cam
views={'BridgeDetail':((0,-44,15),(1,-1,.65),30),
       'BowDetail':((0,49,4),(1,1,.7),32),
       'StarboardProfile':((0,0,8),(1,.05,.16),143),
       'PropulsionDetail':((0,-57,-3),(1,-1,-.1),22)}
for name,(target,direction,scale) in views.items():
    target=Vector(target); cam.location=target+Vector(direction).normalized()*200
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=scale
    sc.render.filepath=str(pack/'Previews'/(name+'.png')); bpy.ops.render.render(write_still=True)
