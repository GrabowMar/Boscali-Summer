"""Assembly contact and moving-envelope review of the GLAIVE-2A generator (models/Glaive2A.py),
before expensive baking.

blender -b --factory-startup --python-exit-code 1 -P ValidateGlaiveAssembly.py -- <evidence>
Contact probes must lie inside both named solids. BVH overlap candidates are conservatively
rejected for moving panels/gun versus the hull and booms; intentional bearing contacts
are tested separately. This is source geometry evidence, not live physics acceptance.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import BuildVanguard as g


def tree(obj):
    mesh = obj.data
    return BVHTree.FromPolygons([obj.matrix_world @ v.co for v in mesh.vertices],
                               [list(p.vertices) for p in mesh.polygons])


def inside(obj, point, tolerance=0.0002):
    point = Vector(point)
    hit, normal, index, distance = tree(obj).find_nearest(point)
    return hit is not None and normal.dot(point-hit) <= tolerance


def main(directory):
    os.makedirs(directory, exist_ok=True)
    g.clear_scene()
    g.models.require(['Glaive2A'])
    spec = g.MODELS['Glaive2A'].build(g)
    joints = {}
    for segment in g.SEGMENTS:
        joint = bpy.data.objects.new(segment['name'], None)
        bpy.context.collection.objects.link(joint)
        joint.location = segment['pivot']
        bpy.context.view_layer.update()
        for part in segment['objs']:
            part.parent = joint
            part.matrix_parent_inverse = joint.matrix_world.inverted()
        joints[segment['name']] = joint
    for segment in g.SEGMENTS:
        if segment['parent']:
            joint = joints[segment['name']]
            joint.parent = joints[segment['parent']]
            joint.matrix_parent_inverse = joint.parent.matrix_world.inverted()
    bpy.context.view_layer.update()
    parts = bpy.data.objects
    contacts = [
        ('FlightKeel','GunBodyMount',(0,.13,-.060)),
        ('GunBodyMount','GunRace',(0,.13,-.103)),
        ('GunRace','RaceSeal',(0,.13,-.134)),
        ('PitchAxle','GunReceiver',(0,.13,-.26)),
        ('GunReceiver','GunJacket',(.01,.223,-.26)),
        ('GunReceiver','FeedBridge',(.040,.208,-.255)),
        ('FeedBridge','FeedBox',(.096,.285,-.255)),
        ('GunJacket','BarrelCollar',(.0165,.26,-.26)),
        ('GunJacket','BarrelCollar.001',(.0165,.425,-.26)),
        ('GunJacket','GunMuzzle',(.016,.62,-.26)),
        ('GunJacket','BoreRecess',(.0038,.587,-.26)),
        ('FeedBox','FeedCover',(.096,.315,-.231)),
        ('PitchAxle','BearingCap',(-.100,.13,-.26)),
        ('PitchAxle','BearingCap.001',(.100,.13,-.26)),
        ('FlightKeel','SensorBezel',(0,.88,-.033)),
        ('SensorBezel','EoWindow',(0,.90,-.0619)),
        ('FlightKeel','PusherHub',(0,-1.01,.060)),
        ('PusherHub','Spinner',(0,-1.125,.055)),
        ('FlightKeel','LaunchAssistExit',(0,-.95,-.031)),
        ('FlightKeel','AvionicsCover',(0,.55,.143)),
        ('FlightKeel','AvionicsCover',(0,.68,.120)),
        ('FlightKeel','AvionicsCover',(0,.77,.118)),
    ]
    for suffix, side, arm, bearing in [('R',1,'GimbalArm.001','PitchBearing.001'),
                                      ('L',-1,'GimbalArm','PitchBearing')]:
        contacts += [
            ('GunRace',arm,(side*.076,.13,-.128)),
            (arm,bearing,(side*.082,.13,-.248)),
            ('PitchAxle',bearing,(side*.082,.13,-.26)),
            ('TailPlane','TailSocket'+suffix,(side*.43,-1.80,.078)),
            ('TailFin'+suffix,'TailSocket'+suffix,(side*.43,-1.85,.074)),
            ('BoomCarryThrough','TailBoom'+suffix,(side*.43,.04,.055)),
            ('FlightKeel','FixedWingRoot'+suffix,(side*.11,.15,.125)),
            ('FixedWingRoot'+suffix,'FixedHingeBearing'+suffix,(side*.239,-.151,.1725)),
            ('FixedHingeBearing'+suffix,'MovingHinge'+suffix,(side*.255,-.16,.173)),
            ('MovingHinge'+suffix,'WingPanel'+suffix,(side*.26,-.153,.175)),
        ]
    rows=[]
    for a,b,point in contacts:
        row={'a':a,'b':b,'probe':point,'connected':inside(parts[a],point) and inside(parts[b],point)}
        row['probe_signed_distances']=[tree(parts[name]).find_nearest(Vector(point))[1].dot(Vector(point)-tree(parts[name]).find_nearest(Vector(point))[0]) for name in (a,b)]
        row['bounds']=[[[min((parts[name].matrix_world@v.co)[axis] for v in parts[name].data.vertices) for axis in range(3)], [max((parts[name].matrix_world@v.co)[axis] for v in parts[name].data.vertices) for axis in range(3)]] for name in (a,b)]
        rows.append(row)
    frame=parts['SensorBezel']; lens=parts['EoWindow']
    bounds=lambda obj,axis: (min((obj.matrix_world@v.co)[axis] for v in obj.data.vertices),max((obj.matrix_world@v.co)[axis] for v in obj.data.vertices))
    rows.append({'a':'EoWindow','b':'SensorBezel','connected':all(bounds(frame,axis)[0]<=bounds(lens,axis)[0] and bounds(lens,axis)[1]<=bounds(frame,axis)[1] for axis in (0,1)),'check':'lens footprint contained by bezel'})
    for name in ('PusherBlade','PusherBlade.001'):
        blade=parts[name]
        rows.append({'a':name,'b':'Spinner','connected':all(inside(parts['Spinner'],blade.matrix_world@v.co)
                                                         for v in list(blade.data.vertices)[:12])})
    obstacles=[parts[name] for name in ('FlightKeel','TailBoomR','TailBoomL','TailFinR','TailFinL','TailPlane')]
    poses=[]
    for angle in range(0,91,10):
        joints['WingL'].rotation_euler.z=math.radians(angle)
        joints['WingR'].rotation_euler.z=-math.radians(angle)
        bpy.context.view_layer.update()
        wing_trees=[tree(parts['WingPanel'+side]) for side in ('L','R')]
        hits=[wing.name+'/'+obstacle.name for wing in (parts['WingPanelL'],parts['WingPanelR'])
              for obstacle in obstacles if tree(wing).overlap(tree(obstacle))]
        if wing_trees[0].overlap(wing_trees[1]): hits.append('WingL/WingR')
        poses.append({'wing_fold_degrees':angle,'unwanted_overlap_candidates':hits})
    spec['pose'](stow=False)
    for yaw in range(-110,111,22):
        for pitch in range(0,81,10):
            joints['GunYaw'].rotation_euler.z=math.radians(yaw)
            joints['GunPitch'].rotation_euler.x=-math.radians(pitch)
            bpy.context.view_layer.update()
            moving=[parts[name] for name in ('GunReceiver','GunJacket','GunMuzzle','FeedBox','FeedCover','FeedBridge')]
            hits=[part.name+'/'+obstacle.name for part in moving for obstacle in obstacles+[parts[n] for n in ('GimbalArm','GimbalArm.001','PitchBearing','PitchBearing.001','GunRace','RaceSeal','GunBodyMount')]
                  if tree(part).overlap(tree(obstacle))]
            poses.append({'gun_yaw_degrees':yaw,'gun_pitch_degrees':pitch,'unwanted_overlap_candidates':hits})
    spec['pose'](stow=False)
    for angle in range(0,360,15):
        joints['Propeller'].rotation_euler.y=math.radians(angle)
        bpy.context.view_layer.update()
        moving=[parts['PusherBlade'],parts['PusherBlade.001']]
        hits=[part.name+'/'+obstacle.name for part in moving for obstacle in obstacles
              if tree(part).overlap(tree(obstacle))]
        poses.append({'propeller_degrees':angle,'unwanted_overlap_candidates':hits})
    errors=[row for row in rows if not row['connected']]+[row for row in poses if row['unwanted_overlap_candidates']]
    result={'result':'FAIL' if errors else 'PASS','contacts':rows,'poses':poses,'errors':errors,
            'scope':'Generator solids and sampled motion envelopes; imported rig checked separately in Unity.'}
    with open(os.path.join(directory,'assembly-validation.json'),'w',encoding='utf-8') as file:
        json.dump(result,file,indent=2)
    # Orthographic review includes transport and maximum depression, not just a flattering hero angle.
    spec['pose'](stow=True)
    g.preview(os.path.join(directory,'Glaive2A_Transport.png'),(1,.8,.4),res=(1400,900))
    spec['pose'](stow=False)
    g.preview(os.path.join(directory,'Glaive2A_Assembly.png'),(1,.8,.35),res=(1400,900))
    g.preview(os.path.join(directory,'Glaive2A_Underside.png'),(1,.65,-.7),res=(1400,900))
    joints['GunYaw'].rotation_euler.z=math.radians(110)
    joints['GunPitch'].rotation_euler.x=-math.radians(80)
    bpy.context.view_layer.update()
    g.preview(os.path.join(directory,'Glaive2A_GunLimit.png'),(1,.5,-.4),res=(1400,900))
    print('GLAIVE_ASSEMBLY '+json.dumps({'result':result['result'],'contacts':len(rows),'poses':len(poses),'errors':errors}),flush=True)
    if errors: raise RuntimeError('GLAIVE assembly failed; see assembly-validation.json')


if __name__=='__main__':
    main(os.path.abspath(sys.argv[sys.argv.index('--')+1]))
