"""Articulation controls on the editable model. Unity receives the neutral mesh pivots."""
from contextlib import contextmanager
from math import radians

import bpy


PREVIEW_CONTROLS = ('Landing gear', 'Cargo hold', 'Flaps', 'Fan phase', 'Nose lift', 'Fighter bay')


def drive(root, obj, axis, key, expression, property_name='rotation_euler'):
    driver = obj.driver_add(property_name, axis).driver
    for variable in list(driver.variables):
        driver.variables.remove(variable)
    variable = driver.variables.new()
    variable.name = 'control'
    variable.targets[0].id = root
    variable.targets[0].data_path = '["' + key + '"]'
    driver.expression = expression


def fighter_bay_control(root, nodes):
    root['Fighter bay'] = 0.0
    root.id_properties_ui('Fighter bay').update(min=0, max=1, description='Open the belly doors, then lower both empty trapezes')
    door_travel = 'min(1, control/.42)'
    arm_travel = 'max(0, min(1, (control-.42)/.58))'
    swing = f'{radians(90)}*{arm_travel}'
    for station in 'FA':
        arm = nodes['TrapezeArm_' + station]
        yoke = nodes['TrapezeYoke_' + station]
        reach = (yoke.location - arm.location).length
        for side, sign in (('L', 1), ('R', -1)):
            drive(root, nodes[f'FighterDoor_{station}_{side}'], 1, 'Fighter bay', f'{radians(sign*100)}*{door_travel}')
        drive(root, arm, 0, 'Fighter bay', swing)
        drive(root, yoke, 1, 'Fighter bay', f'{yoke.location.y!r}+{reach!r}*(1-cos({swing}))', 'location')
        drive(root, yoke, 2, 'Fighter bay', f'{yoke.location.z!r}-{reach!r}*sin({swing})', 'location')


@contextmanager
def neutral_pose(root):
    preview = {name: root[name] for name in PREVIEW_CONTROLS if name in root}
    if not preview:
        yield
        return
    gear = []
    for obj in root.children_recursive:
        if not obj.name.startswith('Gear_') or obj.animation_data is None:
            continue
        for curve in obj.animation_data.drivers:
            if curve.data_path == 'rotation_euler' and any(
                target.id == root and target.data_path == '["Landing gear"]'
                for var in curve.driver.variables for target in var.targets
            ):
                gear.append((obj, curve, curve.mute, obj.rotation_euler[curve.array_index]))

    def refresh():
        root.update_tag()
        scene = bpy.context.scene
        scene.frame_set(scene.frame_current, subframe=scene.frame_subframe)
        bpy.context.view_layer.update()

    try:
        for key in preview:
            root[key] = 0.0
        # Preview zero closes the doors and folds the legs. The game needs its
        # motor's zero rotation on the legs, which is the extended pose.
        for obj, curve, _, _ in gear:
            curve.mute = True
            obj.rotation_euler[curve.array_index] = 0.0
            obj.update_tag(refresh={'OBJECT'})
        refresh()
        yield
    finally:
        for key, value in preview.items():
            root[key] = value
        for obj, curve, muted, angle in gear:
            obj.rotation_euler[curve.array_index] = angle
            curve.mute = muted
            obj.update_tag(refresh={'OBJECT'})
        refresh()


def controls(root,nodes):
    for key,description in {
        "Landing gear":"0 stowed, 1 on the ground",
        "Cargo hold":"0 closed, 1 full cargo opening",
        "Flaps":"Approach flap extension",
        "Fan phase":"One complete fan revolution",
        "Nose lift":"0 sealed, 1 auxiliary lift inlets fully open",
    }.items():
        root[key]=0.0
        root.id_properties_ui(key).update(min=0,max=1,description=description)
    for name,obj in nodes.items():
        side=-1 if name.endswith("_L") else 1
        if name.startswith("Gear_"):
            end=-90 if name.endswith("_Aft") else 90
            drive(root,obj,0,"Landing gear",f"{radians(end)}*(1-max(0,min(1,(control-.2)/.8)))")
        elif name.startswith("GearDoor_"):
            drive(root,obj,1,"Landing gear",f"{radians(-side*90)}*min(1,5*control)")
        elif name=="Ramp" or name.startswith(("AftDoor_","BayDoor_")):
            if name=="Ramp":
                angle,axis,t=22.6,0,"min(1,max(0,(control-.48)/.52))"
            elif name.startswith("AftDoor_"):
                angle,axis,t=-side*95,1,"min(1,control/.55)"
            else:
                angle,axis,t=-side*60,1,"control"
            drive(root,obj,axis,"Cargo hold",f"{radians(angle)}*({t})**2*(3-2*({t}))")
        elif name.startswith(("Flap_Inner_","Flap_Outer_")):
            axis=nodes["Axis_"+name].location.normalized()
            obj.rotation_mode="AXIS_ANGLE"
            obj.rotation_axis_angle=(0,*axis)
            drive(root,obj,0,"Flaps",f"{radians(25 if 'Inner' in name else 20)}*control","rotation_axis_angle")
        elif name.startswith("Fan_Pod_"):
            drive(root,obj,1,"Fan phase","-6.283185307*control")
        elif name.startswith(("Detail_AssistLidInner_","Detail_AssistLidOuter_")):
            end = -68*side if "Inner" in name else 68*side
            drive(root,obj,1,"Nose lift",f"{radians(end)}*control")
        elif name.startswith("Detail_AssistFan_"):
            drive(root,obj,2,"Fan phase",f"{-side*6.283185307}*control")
        elif name.startswith("Railgun"):
            obj.hide_render=True
            obj.hide_set(True)
    fighter_bay_control(root, nodes)
