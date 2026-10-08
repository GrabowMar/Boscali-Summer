"""Author bounded production takes using existing native and owning-module hooks."""
import json
from pathlib import Path

REPO=Path(__file__).resolve().parents[1]
OUT=REPO/'.nomodkit/trailer-production/scenarios'
OUT.mkdir(parents=True,exist_ok=True)
CINE='BoscaliSummer.Cinematics.CinematicAutomation.Step'
CAP='TrailerCapture.Automation.Step'
WEATHER='BoscaliSummer.Modules.Weather.Runtime.WeatherAutomation.'
OPS='BoscaliSummer.Modules.Support.Runtime.OpsAutomation.'
STR='BoscaliSummer.Modules.Command.Runtime.StrAutomation.'
AIR='BoscaliSummer.Garrisons.AirborneAutomation.Step'
FIRE='BoscaliSummer.Fire.DestructionAutomation.Step'
VG='BoscaliSummer.Vanguard.VanguardAutomation.Step'

def call(method,store=None,**args):
    return dict(op='call',method=method,args=args,**({'store':store} if store else {}))
def wait(seconds): return dict(op='wait',seconds=seconds)
def record(id,seconds,hidden=False):
    return [call(CAP,id+'_start',action='start',id=id,hide_ui=hidden),wait(seconds),call(CAP,id+'_end',action='stop')]
def atmosphere(hour=9):
    return dict(op='atmosphere',time_of_day=hour,conditions=.1,cloud_height=5000,wind_speed=2,wind_heading=90,wind_turbulence=0,moon_phase=14)
def plane(type='FS-20 Vortex',altitude=1500,speed=170):
    return [dict(op='spawn',id='lead',type=type,faction='Boscali',at={'map':[0,0]},altitude=altitude,speed=speed,heading=0),
            dict(op='observe',follow='lead'),atmosphere(),wait(6)]
def write(name,ops,timeout=400):
    doc=dict(name=name,mission='Free Flight',mods=['boscalisummer'],timeout_s=timeout,
             ops=ops+[dict(op='end')],checks=[dict(check='status_ok')])
    (OUT/(name+'.json')).write_text(json.dumps(doc,indent=2),encoding='utf-8')

# Native cockpit for ownship features; no cinematic spectator bypass.
weather=[dict(op='seat',id='player',altitude=700,speed=140,heading=0,view='cockpit'),atmosphere(8),
         dict(op='fly',id='player',program=[{'level':25},{'orbit':{'bank':25,'seconds':90}}]),
         call(WEATHER+'ForceWeather','rain_state',conditions=.74,cloudHeight=2200,rain=.65,follow='player'),wait(15)]
weather+=record('cw-canopy',12)
weather+=[call(WEATHER+'Readout','canopy'),call('BoscaliSummer.Modules.Immersion.Runtime.ImmersionAutomation.Readout','body'),
          dict(op='seat',id='player',view='orbit'),wait(2)]
weather+=record('cw-wingview',12)
weather+=[call(CAP,action='panel',label='ENV'),wait(3)]+record('cw-env',8)
write('cw-weather',weather,180)

ui=[dict(op='seat',id='player',altitude=2000,speed=180,heading=0),atmosphere(10),wait(25),call(CAP,'installed_ui',action='ui_catalog')]
for tab,name in [(0,'situation'),(1,'command'),(2,'operations')]:
    ui += [call(STR+'Open',name,tab=tab),wait(3)] + record('cw-str-'+name,8)
for tab,name in [(1,'support'),(2,'space'),(3,'cyber'),(4,'sof'),(5,'board')]:
    ui += [call(OPS+'Open',name,tab=tab),wait(3)] + record('cw-ops-'+name,8)
for label,name in [('MIS','missions'),('COM','comms'),('SQD','pilot'),('EVN','events'),('RAD','radio'),('WMC','wing'),('SET','settings')]:
    ui += [call(CAP,name,action='panel',label=label),wait(3)] + record('cw-'+name,8)
write('cw-ui',ui,330)

flight=plane(altitude=900,speed=175)+[dict(op='fly',id='lead',program=[{'level':70},{'orbit':{'bank':20,'seconds':80}}]),
                                   call(WEATHER+'ForceWeather','clouds',conditions=.42,cloudHeight=2200,rain=0),wait(12),call(CINE,action='detach')]
for id,rig,args in [('cw-flight-ingress','follow',dict(x=-24,y=8,z=-38,fov=42)),
                    ('cw-flight-wide','orbit',dict(radius=90,height=30,**{'from':-155,'to':-65},fov=48)),
                    ('cw-flight-close','orbit',dict(radius=35,height=5,**{'from':-100,'to':-30},fov=38))]:
    flight += [call(CAP,id+'_shot',action='shot',actor='lead',id=id,rig=rig,duration=13,**args)]+record(id,12,True)+[call(CINE,action='stop')]
write('cw-flight',flight,160)

halo=plane('VL-49 Tarantula',3000,90)+[call(AIR,'halo_catalog',action='catalog'),call(AIR,'halo_release',action='halo',ahead=4000),wait(4),
          call(AIR,action='watch',child='Jumper_3',distance=9,height=2)]+record('cw-halo-exit',9,True)
halo += [wait(20),call(AIR,action='watch',child='Jumper_1',distance=45,height=12)]+record('cw-halo-glide',9,True)
halo += [wait(110),call(AIR,action='watch',child='Jumper_1',distance=24,height=4)]+record('cw-halo-canopy',12,True)+[call(AIR,'halo_status',action='status')]
write('cw-halo',halo,280)

rope=plane('SAH-46 Ibis',600,40)+[call(AIR,'hover',action='hover',height=25,over='building'),wait(2),call(AIR,'insertion',action='rope',surface='roof'),wait(2),
        call(AIR,action='watch',op='BoscaliSummer.RopeInsertion',child='Trooper_1',distance=12,height=2)]+record('cw-fast-rope',8,True)
rope += [call(AIR,action='watch',op='BoscaliSummer.RopeInsertion',child='Trooper_2',distance=16,height=6)]+record('cw-roof',8,True)
rope += [wait(16),call(AIR,'exfil_hover',action='hover',height=30),wait(2),call(AIR,'exfil',action='exfil'),wait(7),
         call(AIR,action='watch',op='BoscaliSummer.RopeExtraction',child='Trooper_1',distance=14,height=2)]+record('cw-spies',10,True)+[call(AIR,'rope_status',action='status')]
write('cw-rope',rope,190)

fire=plane(altitude=2500)+[wait(8),call(FIRE,'building',action='select',mesh='commercial_2a')]+record('cw-building-intact',6,True)
fire += [call(CAP,'breach_record',action='start',id='cw-building-breach',hide_ui=True),call(FIRE,'impact',action='hit',power=4),wait(6),call(CAP,'breach_end',action='stop')]
fire += [call(FIRE,action='breachview',distance=22)]+record('cw-building-detail',8,True)
fire += [call(FIRE,action='frame',distance=1.3),call(CAP,'collapse_record',action='start',id='cw-building-collapse',hide_ui=True),
         call(FIRE,'collapse',action='destroy'),wait(10),call(CAP,'collapse_end',action='stop'),call(FIRE,'final_ruin',action='status')]
write('cw-destruction',fire,140)

weapons=plane(altitude=3000,speed=210)+[dict(op='spawn',id='bandit',type='FS-20 Vortex',faction='Primeva',at={'map':[0,60000]},altitude=3000,speed=190,heading=180),
         call(VG,'catalog',action='catalog'),dict(op='fly',id='lead',program=[{'level':180}]),call(CINE,action='detach')]
for key,name in [('VG_Remora','remora'),('VG_MaldX','mald'),('VG_HawcX','hawc')]:
    id='cw-'+name
    weapons += [call(CAP,id+'_shot',action='shot',actor='lead',id=id,rig='follow',duration=14,x=-30,y=6,z=-22,fov=55),
                call(CAP,id+'_record',action='start',id=id,hide_ui=True),call(VG,name+'_launch',action='launch',key=key),wait(12),
                call(CAP,id+'_end',action='stop'),call(CINE,action='stop')]
weapons += [call(VG,'models',action='showcase'),call(VG,action='view',angle='front')]+record('cw-vanguard-models',10,True)
weapons += [call(VG,action='view',angle='close')]+record('cw-vanguard-detail',8,True)
write('cw-weapons',weapons,240)

if __name__=='__main__':
    from nomodkit.sim.scenario import load_scenario
    for path in OUT.glob('*.json'):
        s=load_scenario(path)
        print(s.name,len(s.ops),'ops')
