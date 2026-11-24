"""Articulation controls on the editable model. Unity receives the neutral mesh pivots."""
from math import radians


def controls(root,nodes):
    for key,description in {
        "Landing gear":"0 stowed, 1 on the ground",
        "Cargo hold":"0 closed, 1 full cargo opening",
        "Flaps":"Approach flap extension",
        "Fan phase":"One complete fan revolution",
    }.items():
        root[key]=0.0
        root.id_properties_ui(key).update(min=0,max=1,description=description)
    def drive(obj,axis,key,expression,property_name="rotation_euler"):
        curve=obj.driver_add(property_name,axis)
        driver=curve.driver
        variable=driver.variables.new()
        variable.name="control"
        variable.targets[0].id=root
        variable.targets[0].data_path='["'+key+'"]'
        driver.expression=expression
    for name,obj in nodes.items():
        side=-1 if name.endswith("_L") else 1
        if name.startswith("Gear_"):
            end=-90 if name.endswith("_Aft") else 90
            drive(obj,0,"Landing gear",f"{radians(end)}*(1-max(0,min(1,(control-.2)/.8)))")
        elif name.startswith("GearDoor_"):
            drive(obj,1,"Landing gear",f"{radians(-side*90)}*min(1,5*control)")
        elif name=="Ramp" or name.startswith(("AftDoor_","BayDoor_")):
            if name=="Ramp":
                angle,axis,t=22.6,0,"min(1,max(0,(control-.48)/.52))"
            elif name.startswith("AftDoor_"):
                angle,axis,t=-side*95,1,"min(1,control/.55)"
            else:
                angle,axis,t=-side*60,1,"control"
            drive(obj,axis,"Cargo hold",f"{radians(angle)}*({t})**2*(3-2*({t}))")
        elif name.startswith(("Flap_Inner_","Flap_Outer_")):
            axis=nodes["Axis_"+name].location.normalized()
            obj.rotation_mode="AXIS_ANGLE"
            obj.rotation_axis_angle=(0,*axis)
            drive(obj,0,"Flaps",f"{radians(25 if 'Inner' in name else 20)}*control","rotation_axis_angle")
        elif name.startswith("Fan_Pod_"):
            drive(obj,1,"Fan phase","-6.283185307*control")
        elif name.startswith("Railgun"):
            obj.hide_render=True
            obj.hide_set(True)
