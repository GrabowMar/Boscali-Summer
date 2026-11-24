"""Check the shipped BSMP in a fresh Blender scene.

Run with blender -b --factory-startup --python-exit-code 1 --python verify_rig.py.
All transforms are in metres. The Blender reconstruction of a Unity local rotation
uses the opposite angle, because the (x,z,y) handoff changes handedness.
"""
from math import cos, hypot, radians, sin
from pathlib import Path
import re
import sys

import bpy
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from bsmp import read_model


def vertices(obj):
    positions = np.empty((len(obj.data.vertices), 3), dtype=np.float32)
    obj.data.vertices.foreach_get('co', positions.ravel())
    return positions


def position(obj):
    return np.array(obj.matrix_world.translation)


def clip_polygon(poly, bound, sign):
    result = []
    for a,b in zip(poly, poly[1:]+poly[:1]):
        inside_a, inside_b = sign*(a[1]-bound) >= 0, sign*(b[1]-bound) >= 0
        if inside_a:
            result.append(a)
        if inside_a != inside_b:
            result.append(a + (b-a)*((bound-a[1])/(b[1]-a[1])))
    return result


def projected_radius(polygon):
    # Minimum radial distance of an entire convex polygon, including its interior.
    edges = [(p[[0,2]], q[[0,2]]) for p,q in zip(polygon,polygon[1:]+polygon[:1])]
    crosses = [p[0]*q[1]-p[1]*q[0] for p,q in edges]
    if min(crosses) > 0 or max(crosses) < 0:
        return 0.
    distance = float('inf')
    for a,b in edges:
        d = b-a
        t = min(1.,max(0.,-np.dot(a,d)/max(np.dot(d,d),1e-12)))
        closest = a + t*d
        distance = min(distance,hypot(*closest))
    return distance


def rotor_clearance(fan, fixed):
    # A complete 360° swept cylinder bounds every rotating triangle. Clip the actual
    # static triangles to its axial interval, then measure their nearest radial point.
    p = vertices(fan)
    radius = np.hypot(p[:,0],p[:,2]).max()
    low, high = p[:,1].min(), p[:,1].max()
    nearest = float('inf')
    for obj in fixed:
        fixed_points = vertices(obj) + position(obj) - position(fan)
        indices = np.empty(len(obj.data.loops),dtype=np.int32)
        obj.data.loops.foreach_get('vertex_index',indices)
        triangles = fixed_points[indices.reshape(-1,3)]
        axial = triangles[:,:,1]
        candidates = triangles[(axial.max(1)>=low) & (axial.min(1)<=high)]
        for triangle in candidates:
            clip = clip_polygon(list(triangle),low,+1)
            clip = clip_polygon(clip,high,-1)
            if len(clip) >= 3:
                nearest = min(nearest,projected_radius(clip))
    if not np.isfinite(nearest):
        raise ValueError(f'No fixed inlet faces around {fan.name}')
    return nearest-radius


def turned(obj, axis, degrees):
    p = vertices(obj).copy()
    marker = next((c for c in obj.children if c.name=='Axis_'+obj.name),None)
    if marker is not None and axis=='X':
        rotation=Matrix.Rotation(radians(degrees),3,marker.location.normalized())
        return p @ np.array(rotation).T
    i,j = {'X':(1,2),'Y':(2,0),'Z':(0,1)}[axis]
    c,s = cos(radians(degrees)), sin(radians(degrees))
    p[:,[i,j]] = np.column_stack((c*p[:,i]-s*p[:,j], s*p[:,i]+c*p[:,j]))
    return p


def tree(obj, axis, degrees, offset=None):
    p = turned(obj,axis,degrees) + (position(obj) if offset is None else offset)
    faces = np.empty(len(obj.data.loops),dtype=np.int32)
    obj.data.loops.foreach_get('vertex_index',faces)
    return BVHTree.FromPolygons(p.tolist(),faces.reshape(-1,3).tolist(),all_triangles=True)


def cockpit_clearance(nodes, nose_max_y, check):
    # A ray through each furniture vertex must first meet exterior skin, glazing
    # or a windscreen frame. Explicitly reject vertices outside all three outlines.
    walls = [tree(nodes[name],'X',0) for name in
             ('Fuselage_Cockpit','Glass_Canopy','Detail_WindscreenFrames')]
    furnishings = [obj for obj in nodes.values() if obj.type == 'MESH' and
                   (obj.name.startswith('FlightDeck_') or obj.name == 'Int_AvionicsBezels')]
    check('Int_AvionicsBezels' in {obj.name for obj in furnishings}, 'Missing cockpit display bezels')
    check(any(obj.name.startswith('FlightDeck_') for obj in furnishings), 'Missing cockpit furniture')
    ray_y = nose_max_y + 5.
    nearest = float('inf')
    for obj in furnishings:
        closest = float('inf')
        missing = 0
        for x,y,z in np.unique(vertices(obj) + position(obj),axis=0):
            origin = (x,ray_y,z)
            hits = [wall.ray_cast(origin,(0,-1,0))[0] for wall in walls]
            outside = [point.y for point in hits if point is not None]
            if outside:
                closest = min(closest,max(outside)-y)
            else:
                missing += 1
        check(missing == 0,f'{obj.name}: {missing} vertices outside the nose silhouette')
        check(closest >= -.0001,f'{obj.name}: protrudes {-closest:.4f} m through the cockpit exterior')
        nearest = min(nearest,closest)
    print(f'Cockpit exterior clearance: {nearest:.4f} m, {len(furnishings)} interior meshes.')


def verify(nodes):
    errors = []
    def check(test, description):
        if not test:
            errors.append(description)

    materials = set()
    minimum, maximum = np.full(3,np.inf), np.full(3,-np.inf)
    triangles = 0
    for obj in nodes.values():
        if obj.type != 'MESH':
            continue
        mesh = obj.data
        p = vertices(obj) + position(obj)
        minimum = np.minimum(minimum,p.min(0))
        maximum = np.maximum(maximum,p.max(0))
        triangles += len(mesh.polygons)
        material_indices = np.empty(len(mesh.polygons),np.int32)
        mesh.polygons.foreach_get('material_index',material_indices)
        check(0 <= material_indices.min() <= material_indices.max() < len(mesh.materials),
              'Material indices: '+obj.name)
        check(np.isfinite(p).all(),'Coordinates: '+obj.name)
        corner_indices = np.empty(len(mesh.loops),np.int32)
        mesh.loops.foreach_get('vertex_index',corner_indices)
        check(0 <= corner_indices.min() <= corner_indices.max() < len(mesh.vertices),
              'Vertex indices: '+obj.name)
        normals = np.empty((len(mesh.loops),3),np.float32)
        mesh.corner_normals.foreach_get('vector',normals.ravel())
        check(np.isfinite(normals).all(),'Normals: '+obj.name)
        uv = np.empty((len(mesh.loops),2),np.float32)
        mesh.uv_layers.active.uv.foreach_get('vector',uv.ravel())
        check(np.isfinite(uv).all(),'UVs: '+obj.name)
        materials.update(mesh.materials)
        if obj.name.startswith('Detail_Livery_'):
            label = obj.name.removeprefix('Detail_Livery_')
            check(label == obj.parent.name or label in (obj.parent.name+'_L',obj.parent.name+'_R'),
                  'Livery damage owner: '+obj.name)
    dims = maximum-minimum
    template = (HERE.parent/'MegaplaneTemplate.cs').read_text()
    # Definition dimensions are derived from the BSMP bounds at load. Compare to
    # the carrier's art design envelope independently to catch a unit/axis error.
    for actual, nominal, tolerance, label in zip(dims,(342,236,50),(7,8,5),('span','length','height')):
        check(abs(actual-nominal) < tolerance, f'{label} {actual:.2f} outside design envelope')
    for mat in materials:
        check(len(mat['bsmp_values']) == 6 and np.isfinite(mat['bsmp_values']).all(),
              'Material values: '+mat.name)
    for name in re.findall(r'\{ "\w+", "(\w+)" \}',template.partition('Mapping =')[2].partition('};')[0]):
        check(name in nodes, 'Missing damage island '+name)
    print(f'Runtime geometry: {len(nodes)} nodes, {triangles:,} triangles, {len(materials)} materials; '
          f'{dims[0]:.2f} × {dims[1]:.2f} × {dims[2]:.2f} m')

    cockpit_clearance(nodes,maximum[1],check)
    clearances = []
    for side in 'LR':
        for i, wing in enumerate(('Root','Inner','Mid','Outer'),1):
            key = f'{side}{i}'
            pod, fan = nodes['Pod_'+key], nodes['Fan_Pod_'+key]
            nozzle, fittings = nodes['Pod_'+key+'_Nozzle'], nodes[f'Pods_Nacelle{i}_{side}']
            check(pod.parent == nodes[f'Wing_{wing}_{side}'] and
                  all(obj.parent == pod for obj in (fan,nozzle,fittings)), 'Engine damage hierarchy '+key)
            gap = rotor_clearance(fan, (pod,nozzle,fittings))
            clearances.append(f'{key} {gap:.3f}')
            check(gap > .005, f'Fan {key} swept envelope intersects fixed art ({gap:.3f} m)')
    print('Guaranteed 360-degree fan clearance in metres: '+', '.join(clearances))

    poses = 0
    # Match the direction and full angular travel of the flight surfaces, access
    # doors, lift-bank vanes, and gear. Verify the most distant vertex travels
    # on the correct hinge circle and check the opening direction where defined.
    specs = [('Aileron_',2,'X',12),('Flap_Inner_',2,'X',25),('Flap_Outer_',2,'X',20),
             ('Spoiler_',14,'X',-45),('Rudder_',2,'Z',10),('Elevator',1,'X',10),
             ('LiftDoor_',16,'X',70),('LiftVane_',48,'X',-60),
             ('Gear_',14,'X',90),('GearDoor_',14,'Y',90),
             ('FighterDoor_',4,'Y',100),('BayDoor_',2,'Y',60),('AftDoor_',2,'Y',95),
             ('Ramp',1,'X',22.6)]
    for prefix,expected,axis,angle in specs:
        parts = [o for k,o in nodes.items() if
                 (k.startswith(prefix) if prefix.endswith('_') else k==prefix)]
        check(len(parts)==expected, f'{prefix} count {len(parts)}, expected {expected}')
        for obj in parts:
            before = vertices(obj)
            sign = -1 if axis=='Y' and obj.name.endswith('_R') else 1
            if prefix=='Gear_' and obj.name.endswith('_Aft'):
                sign=-1
            final = turned(obj,axis,angle*sign)
            if prefix=='Gear_':
                check(final[:,2].min()>before[:,2].min()+3, 'Gear failed to stow '+obj.name)
            elif prefix in ('GearDoor_','FighterDoor_','BayDoor_','AftDoor_','Ramp'):
                check(final[:,2].mean()<before[:,2].mean()-.1, 'Door did not open down '+obj.name)
            if prefix in ('Aileron_','Flap_Inner_','Flap_Outer_','Spoiler_','Rudder_','Elevator'):
                # The rotating mesh must reach aft of its pivot, and the hinge must run
                # along its leading portion. An origin at the mesh centroid would fail.
                marker=next((c for c in obj.children if c.name=='Axis_'+obj.name),None)
                aft=before[:,1].copy()
                if marker is not None:
                    a=np.array(marker.location)
                    check(abs(np.linalg.norm(a)-1.) < .002 and a[0]>.6, 'Unit hinge axis '+obj.name)
                    aft -= before[:,0]*a[1]/a[0]
                elif prefix in ('Aileron_','Flap_Inner_','Flap_Outer_','Spoiler_'):
                    errors.append('Missing local hinge axis: '+obj.name)
                check(np.quantile(aft,.85) < 1.5 and aft.min() < -1., 'Surface pivot '+obj.name)
                far = before[:,1].argmin()
                travel = np.linalg.norm(final[far]-before[far])
                check(travel > .2,'Control has negligible trailing-edge travel: '+obj.name)
            for frac in (.25,.50,.75,1.):
                m = turned(obj,axis,angle*sign*frac)
                check(np.allclose(np.linalg.norm(m,axis=1), np.linalg.norm(before,axis=1),
                                  rtol=2e-5,atol=2e-5), 'Hinge radius '+obj.name)
                poses += 1
    print(f'Kinematic samples across {len(specs)} actuator families: {poses}')

    for name, moving in nodes.items():
        if name.startswith('Aileron_'):
            angles=range(-12,13,2)
        elif name.startswith('Flap_'):
            stop=25 if 'Inner_' in name else 20
            angles=(*range(0,stop+1,2),stop)
        elif name.startswith('Spoiler_'):
            angles=(*range(0,-54,-2),-53)
        else:
            continue
        fixed=tree(moving.parent,'X',0)
        for angle in angles:
            if tree(moving,'X',angle).overlap(fixed):
                errors.append(f'{name} intersects fixed wing at {angle}°')
                break
    for name, axis in (('Rudder_L','Z'),('Rudder_R','Z'),('Elevator','X')):
        moving=nodes[name]
        fixed=tree(moving.parent,'X',0)
        for angle in range(-10,11,2):
            check(not tree(moving,axis,angle).overlap(fixed),
                  f'{name} intersects fixed tail at {angle}°')

    # The carrier first opens its doors and then pivots each leg. Test the opposing
    # triangles, including the intermediate travel angles and the closed/stowed endpoint.
    for side in 'LR':
        for tag in ('N', 'M1','M2','M3','M4','M5','M6'):
            leg = nodes[f'Gear_Nose_{side}' if tag=='N' else f'Gear_Main_{side}{tag[1:]}']
            if tag=='N':
                doors=[nodes['GearDoor_N_L'],nodes['GearDoor_N_R']]
                well=nodes['GearWell_N']
            else:
                i=int(tag[1:])
                doors=[nodes[f'GearDoor_M{j}_{side}'] for j in range(max(1,i-1),min(6,i+1)+1)]
                well=nodes['GearWell_'+side]
            housing=[(o.name,tree(o,'X',0)) for o in (well,leg.parent)]
            poses=[(a,90) for a in range(0,91,5)]+[(90,a) for a in range(0,90,5)]
            for door_angle,leg_angle in poses:
                gear=tree(leg,'X',leg_angle)
                leaves=[(o.name,tree(o,'Y',(-1 if o.name.endswith('_R') else 1)*door_angle)) for o in doors]
                for name,other in housing+leaves:
                    if gear.overlap(other):
                        errors.append(f'{leg.name} touches {name}, door {door_angle}°, leg {leg_angle}°')
                        break
                else:
                    continue
                break
    for slot in 'FA':
        arm, yoke = nodes['TrapezeArm_'+slot],nodes['TrapezeYoke_'+slot]
        check(arm.parent==yoke.parent and nodes['FighterSlot_'+slot].parent==yoke,
              'Trapeze ownership '+slot)
        reach = np.linalg.norm(position(arm)-position(yoke))
        check(np.allclose(position(yoke)-position(arm), (0,-reach,0),atol=.1), 'Yoke pivot '+slot)
        left,right = nodes[f'FighterDoor_{slot}_L'],nodes[f'FighterDoor_{slot}_R']
        for angle in (25,50,75,100):
            if tree(left,'Y',angle).overlap(tree(right,'Y',-angle)):
                errors.append(f'Fighter bay {slot} opposing leaves intersect at {angle}°')
        open_left,open_right = tree(left,'Y',100),tree(right,'Y',-100)
        for angle in (0,30,60,90):
            swing = radians(angle)
            at = position(arm) + np.array((0,-reach*cos(swing),-reach*sin(swing)))
            for moving in (tree(arm,'X',angle),tree(yoke,'X',0,at)):
                check(not moving.overlap(open_left) and not moving.overlap(open_right),
                      f'Trapeze {slot} touches open door at {angle}°')
    fittings=[(n,tree(o,'X',0)) for n,o in nodes.items()
              if n.startswith('Detail_Bay_') and o.type=='MESH']
    def ease(t):
        t=max(0.,min(1.,t))
        return t*t*(3-2*t)
    for step in range(0,101,4):
        t=step/100
        leaves=[('Ramp',tree(nodes['Ramp'],'X',22.6*ease((t-.48)/.52)))]
        for stem,angle in (('AftDoor_',95*ease(t/.55)),('BayDoor_',60*ease(t))):
            leaves.extend((stem+s,tree(nodes[stem+s],'Y',sign*angle)) for s,sign in (('L',1),('R',-1)))
        for i,(label,moving) in enumerate(leaves):
            for other,fixed in fittings+leaves[i+1:]:
                check(not moving.overlap(fixed),
                      f'{label} intersects {other} at cargo opening {t:.2f}')
    if errors:
        raise ValueError('\n'.join(errors))
    print('Wing and tail controls, gear, fighter doors, trapezes and cargo closures clear.')


if __name__ == '__main__':
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = HERE.parent/'Assets/megaplane.mesh.gz'
    art = read_model(path)
    bpy.context.view_layer.update()
    verify({obj.name:obj for obj in art})
