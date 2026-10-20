using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("fx-fuel-fireball");
  var fl = FxKit.Sys(root,"Flash",M("M_Flash_Add"),0.1f); fl.transform.localPosition=new Vector3(0,12,0);
  { var m=fl.main; m.startLifetime=0.35f; m.startSize=90; m.maxParticles=1; m.startColor=FxKit.Hex("#ffd8a0");
    FxKit.Burst(fl,0,1,1); FxKit.SizeLife(fl,0,.5f,.3f,1,1,1); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","1:#ff7a20"},0,1,.4f,.7f,1,0)); }
  // rolling fireball
  var fb = FxKit.Sys(root,"Fireball",M("M_Fire_Add"),0.5f); fb.transform.localPosition=new Vector3(0,12,0);
  { var m=fb.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2.2f,3.4f); m.startSpeed=new ParticleSystem.MinMaxCurve(8,20); m.startSize=new ParticleSystem.MinMaxCurve(26,42); m.maxParticles=45;
    var s=fb.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=12;
    FxKit.Burst(fb,0,20,20); FxKit.Burst(fb,0.15f,10,10); FxKit.Burst(fb,0.35f,8,8); FxKit.Burst(fb,0.6f,6,6); FxKit.Sheet2x2(fb); FxKit.Spin(fb,-60,60); FxKit.Drag(fb,0.03f,true); FxKit.Noise(fb,6,0.05f,0.5f);
    var v=fb.velocityOverLifetime; v.enabled=true; v.space=ParticleSystemSimulationSpace.Local; v.x=FxKit.Curve(1,0,0,1,0); v.z=FxKit.Curve(1,0,0,1,0); v.y=FxKit.Curve(1,0,8,0.4f,30,1,36);
    FxKit.SizeLife(fb,0,.55f,.3f,1.1f,1,1.8f);
    FxKit.ColorLife(fb,FxKit.Grad(new[]{"0:#ffd090","0.1:#ff8020","0.35:#d03a06","0.65:#5a1204","1:#200800"},0,.6f,.4f,.5f,.8f,.3f,1,0)); }
  // black smoke rising out of it
  var sm = FxKit.Sys(root,"BlackSmoke",M("M_Smoke_Lit"),3.0f); sm.transform.localPosition=new Vector3(0,20,0);
  { var m=sm.main; m.startLifetime=new ParticleSystem.MinMaxCurve(5.5f,8.5f); m.startSpeed=new ParticleSystem.MinMaxCurve(6,14); m.startSize=new ParticleSystem.MinMaxCurve(22,34); m.maxParticles=60; m.startDelay=0.35f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#1c1814"),FxKit.Hex("#34302c"));
    var s=sm.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=14;
    FxKit.Burst(sm,0,10,10); var e=sm.emission; e.rateOverTime=FxKit.Curve(18,0,1,1,0.5f); FxKit.Sheet2x2(sm); FxKit.Spin(sm,-20,20); FxKit.Noise(sm,5,0.03f,0.3f);
    var v=sm.velocityOverLifetime; v.enabled=true; v.space=ParticleSystemSimulationSpace.Local; v.x=FxKit.Curve(1,0,0,1,0); v.z=FxKit.Curve(1,0,0,1,0); v.y=FxKit.Curve(1,0,30,0.4f,20,1,12);
    FxKit.SizeLife(sm,0,.5f,.3f,1.2f,1,2.6f); FxKit.ColorLife(sm,FxKit.Grad(new[]{"0:#ff9a50","0.12:#ffffff","1:#ffffff"},0,0,.1f,.95f,.6f,.75f,1,0)); }
  // burning droplets
  var dr = FxKit.Sys(root,"Droplets",M("M_Spark_Add"),0.3f); dr.transform.localPosition=new Vector3(0,10,0);
  { var m=dr.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1.8f,3.2f); m.startSpeed=new ParticleSystem.MinMaxCurve(30,60); m.startSize=new ParticleSystem.MinMaxCurve(2.5f,4.5f); m.maxParticles=45; m.gravityModifier=1.4f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffd890"),FxKit.Hex("#ff8a30"));
    var s=dr.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=65; s.radius=8; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(dr,0.02f,30,30); FxKit.Burst(dr,0.2f,12,12); FxKit.Drag(dr,0.05f);
    var r=FxKit.R(dr); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.03f; r.lengthScale=1.2f;
    FxKit.ColorLife(dr,FxKit.Grad(new[]{"0:#ffffff","0.3:#ffa040","1:#ff3a00"},0,1,.75f,1,1,0));
    FxKit.Trails(dr,M("M_Fire_Add"),0.35f,5f,FxKit.Grad(new[]{"0:#ff9a40","0.5:#a03008","1:#301008"},0,.9f,1,0),2f,.6f,1f); }
  // rising embers
  var em = FxKit.Sys(root,"Embers",M("M_Ember_Add"),1.5f); em.transform.localPosition=new Vector3(0,20,0);
  { var m=em.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2.5f,5f); m.startSpeed=new ParticleSystem.MinMaxCurve(5,15); m.startSize=new ParticleSystem.MinMaxCurve(1.5f,3f); m.maxParticles=60; m.gravityModifier=-0.08f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffc070"),FxKit.Hex("#ff6010"));
    var s=em.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=25;
    FxKit.Burst(em,0.1f,25,25); var e=em.emission; e.rateOverTime=25; FxKit.Noise(em,8,0.05f,0.5f);
    FxKit.ColorLife(em,FxKit.Grad(new[]{"0:#ffffff","0.5:#ff9030","1:#ff2000"},0,1,.2f,.5f,.4f,1,.6f,.4f,1,0)); }
  return FxKit.Stats("fx-fuel-fireball");
} }
