"""Explicitly disabled design stubs; these do not install or run contracts."""
import json,sys
from pathlib import Path
pack=Path(sys.argv[1]).resolve()

def use(model,anchor='GroundOrigin',purpose='objective scenery'):
    return {'pack':'MissionAssets','key':'BS_Mission_'+model,'anchor':anchor,'purpose':purpose}

missions=[
 {'id':'cut_the_signal','title':'Cut the Signal','kind':'ElectronicWarfare','modules':['DynamicOperations','Support','Intel'],
  'assets':[use('RadioRelay','Uplink','enemy relay'),use('MobileJammer','Emitter','confirmed jammer')],
  'briefing':'Identify the jammer protecting a relay site, suppress it and verify the emitter has stopped.',
  'phases':[('Locate','Observe an enemy contact already known to the faction.'),('Suppress','Disable the native jammer unit or complete an authorized CYBER action.'),('Confirm','Host confirms native emission loss for 15 continuous seconds.')],
  'native_binding':'Jammer contact must bind to a native Unit; the truck model alone cannot emit or jam.',
  'proposed_follow_on':'quiet_horizon'},
 {'id':'quiet_horizon','title':'Quiet Horizon','kind':'Recon','modules':['DynamicOperations','Support','Intel'],
  'assets':[use('CoastalSensorPost','Sensor'),use('SarSatellite','RadarOrigin','OPS sensor presentation'),use('RadioRelay','Uplink','downlink station')],
  'briefing':'Use a radar pass to identify the coastal sensor site, then obtain positive visual confirmation.',
  'phases':[('Task','Host accepts a funded native SPACE observation request.'),('Observe','Faction threat picture records a known contact.'),('Confirm','Complete the existing Recon observation rule.')],
  'native_binding':'Orbital model is presentation; detection, visibility and authorization remain owned by SPACE/Intel.'},
 {'id':'broken_wing','title':'Broken Wing','kind':'Rescue','modules':['DynamicOperations','UrbanCombat','Support'],
  'assets':[use('RescueCrashSite','Survivor'),use('ForwardAidStation','Dropoff'),use('EvacuationLandingZone','LandingPoint')],
  'briefing':'Find the downed crew, secure the landing area and deliver them to the aid station.',
  'phases':[('Locate','Observe a living friendly PilotDismounted.'),('Extract','Native rescue/transport service confirms pickup.'),('Deliver','Native service confirms survivor arrival at the friendly dropoff.')],
  'native_binding':'Bind the survivor to a real pilot; scenery proximity cannot manufacture rescue success.'},
 {'id':'fuse_box','title':'Fuse Box','kind':'RepairCover','modules':['DynamicOperations','TheaterOps','Support'],
  'assets':[use('PowerSubstation','Breaker'),use('RepairStation','RepairBay')],
  'briefing':'Protect engineers restoring a damaged power node.',
  'phases':[('Escort','A real friendly Repairer reaches the bound damaged Building.'),('Protect','Keep the engineer and building alive while native repair advances.'),('Restore','Native building repair state crosses the configured completion threshold.')],
  'native_binding':'Requires a native repairable Building and Repairer. No invisible repair timer replaces native repair.'},
 {'id':'sealed_manifest','title':'Sealed Manifest','kind':'BattlefieldSurvey','modules':['DynamicOperations','Intel','Support'],
  'assets':[use('SmugglerCache','Inspect'),use('DroneGroundStation','DataTerminal')],
  'briefing':'Inspect a hidden cache and recover its shipment manifest for the faction intelligence picture.',
  'phases':[('Discover','Faction observation confirms the cache area.'),('Inspect','Authorized SOF interaction completes at the Inspect anchor.'),('Report','Host records the operation outcome and awards the accepted reward once.')],
  'native_binding':'Inspection/recovery is a future host service adapter; this stub does not add a new operation enum or packet.'},
 {'id':'logistics_window','title':'Logistics Window','kind':'SupplyEscort','modules':['DynamicOperations','TheaterOps'],
  'assets':[use('AmmunitionDepot','CargoPickup'),use('RepairStation','Service'),use('RadioRelay','Interact')],
  'briefing':'Cover a resupply convoy during its exposed depot-to-repair-station leg.',
  'phases':[('Rendezvous','Bind to a real friendly GroundVehicle with Rearmer capacity.'),('Escort','Existing SupplyEscort rules track the convoy; native/player orders remain authoritative.'),('Arrive','Native convoy reaches its destination with usable capacity.')],
  'native_binding':'No spawned static truck counts as a functioning convoy.'},
 {'id':'last_light','title':'Last Light','kind':'Defend','modules':['DynamicOperations','UrbanCombat','Support'],
  'assets':[use('EvacuationLandingZone','Extraction'),use('ForwardAidStation','Triage'),use('BridgeDemolitionKit','Control')],
  'briefing':'Hold a threatened evacuation site until its final transport can depart.',
  'phases':[('Secure','Bind to an existing friendly active objective.'),('Hold','Native objective remains held while evacuation service works.'),('Depart','Host receives the native transport departure/completion observation.')],
  'native_binding':'Demolition kit is optional set dressing; do not create automatic charges or duplicate native objectives.'},
 {'id':'lost_packet','title':'Lost Packet','kind':'SortieReport','modules':['DynamicOperations','Support','Intel'],
  'assets':[use('DroneGroundStation','DataTerminal'),use('OpticalSatellite','NadirSensor','imager presentation'),use('RelaySatellite','Downlink','link presentation')],
  'briefing':'Restore an observation link and return a confirmed sortie report from the affected area.',
  'phases':[('Re-establish','Existing Support authorization validates the chosen uplink.'),('Collect','Native observation/recording captures evidence of a faction-known target.'),('Report','Existing SortieReport completion rule accepts the evidence.')],
  'native_binding':'Satellite visuals grant neither sight nor support effects by themselves.'},
 {'id':'orbital_handshake','title':'Orbital Handshake','kind':'Recon','modules':['Support','DynamicOperations'],
  'assets':[use('OrbitalServiceTug','CapturePort','service-operation presentation'),use('RelaySatellite','Downlink','relay presentation'),use('RadioRelay','Uplink','ground authorization site')],
  'briefing':'Recover control of a relay by securing its ground uplink and completing an authorized servicing task.',
  'phases':[('Ground link','Observe and secure the bound native ground objective.'),('Authorize','Host validates ownership, faction, cost and current SPACE state.'),('Service','Future servicing adapter confirms capture/alignment; presentation does not decide success.')],
  'native_binding':'New orbital servicing gameplay is deliberately unimplemented; this is a design stub.'}
]
for m in missions:
    m['schema_version']=1; m['status']='design_stub'; m['enabled']=False; m['automatic_registration']=False
    m['phases']=[{'id':str(i+1),'label':label,'completion_observation':text} for i,(label,text) in enumerate(m['phases'])]
    m['authority']='host'; m['reward_policy']='existing accepted reward; one completion award'
    m['visibility']='faction-known contacts only; no hidden enemy coordinates'
    m['limits']={'board_cards':3,'accepted_per_faction':2,'poll_hz':1,'max_scene_assets':12,'max_tracked_units':8,'max_chain_depth':2}
    m['reset']='cancel on scene generation change; release presentation objects'
folder=pack/'MissionStubs'; folder.mkdir(exist_ok=True)
for mission in missions: (folder/(mission['id']+'.json')).write_text(json.dumps(mission,indent=2))
(folder/'index.json').write_text(json.dumps({'schema_version':1,'status':'design_stubs_not_runtime','missions':[m['id'] for m in missions]},indent=2))
print('WROTE',len(missions),'disabled mission design stubs')
