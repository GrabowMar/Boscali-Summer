using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("fx-collapse-dust");
  // rolling ground dust
  var gr = FxKit.Sys(root,"GroundRoll",M("M_Smoke_Lit"),0.6f); gr.transform.localPosition=new Vector3(0,5,0);
  { var m=gr.main; m.startLifetime=new ParticleSystem.MinMaxCurve(5f,8f); m.startSpeed=new ParticleSystem.MinMaxCurve(22,38); m.startSize=new ParticleSystem.MinMaxCurve(14,22); m.maxParticles=60; m.gravityModifier=-0.01f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#7a6c5a"),FxKit.Hex("#5e5446"));
    var s=gr.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Circle; s.radius=10; s.radiusThickness=0.3f; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(gr,0,24,24); FxKit.Burst(gr,0.25f,18,18); FxKit.Burst(gr,0.5f,14,14); FxKit.Sheet2x2(gr); FxKit.Spin(gr,-25,25); FxKit.Drag(gr,0.05f,true); FxKit.Noise(gr,3,0.04f,0.3f);
    FxKit.SizeLife(gr,0,.45f,.25f,1.3f,1,2.6f); FxKit.ColorLife(gr,FxKit.Grad(new[]{"0:#ffffff","1:#e0d8d0"},0,0,.06f,.95f,.6f,.7f,1,0)); }
  // rising plume
  var pl = FxKit.Sys(root,"Plume",M("M_Smoke_Lit"),1.2f); pl.transform.localPosition=new Vector3(0,8,0);
  { var m=pl.main; m.startLifetime=new ParticleSystem.MinMaxCurve(5.5f,9f); m.startSpeed=new ParticleSystem.MinMaxCurve(10,22); m.startSize=new ParticleSystem.MinMaxCurve(18,28); m.maxParticles=40; m.gravityModifier=-0.02f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#a09484"),FxKit.Hex("#7c7264"));
    var s=pl.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=18; s.radius=12; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(pl,0.05f,12,12); var e=pl.emission; e.rateOverTime=22; FxKit.Sheet2x2(pl); FxKit.Spin(pl,-15,15); FxKit.Drag(pl,0.02f,true); FxKit.Noise(pl,4,0.03f,0.2f);
    FxKit.SizeLife(pl,0,.5f,.3f,1.2f,1,2.4f); FxKit.ColorLife(pl,FxKit.Grad(new[]{"0:#ffffff","1:#d8d0c8"},0,0,.08f,.9f,.6f,.6f,1,0)); }
  // falling chunks
  var ch = FxKit.Sys(root,"Chunks",M("M_Debris"),0.6f); ch.transform.localPosition=new Vector3(0,25,0);
  { var m=ch.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1.6f,2.6f); m.startSpeed=new ParticleSystem.MinMaxCurve(4,14); m.startSize=new ParticleSystem.MinMaxCurve(2.5f,6f); m.maxParticles=45; m.gravityModifier=1.4f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#3a342e"),FxKit.Hex("#6a6054"));
    var s=ch.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Box; s.scale=new Vector3(30,18,30);
    FxKit.Burst(ch,0,25,25); FxKit.Burst(ch,0.2f,12,12); FxKit.Burst(ch,0.4f,8,8); FxKit.Sheet2x2(ch); FxKit.Spin(ch,-300,300);
    FxKit.ColorLife(ch,FxKit.Grad(new[]{"0:#ffffff","1:#ffffff"},0,1,.9f,1,1,0));
    FxKit.Trails(ch,M("M_Trail_Smoke"),0.35f,2.5f,FxKit.Grad(new[]{"0:#a89c8c","1:#8c8070"},0,.6f,1,0),1f,.3f,1f); { var t=ch.trails; t.inheritParticleColor=false; } }
  // fine debris haze falling (curtain)
  var cu = FxKit.Sys(root,"Curtain",M("M_Smoke_Lit"),0.4f); cu.transform.localPosition=new Vector3(0,20,0);
  { var m=cu.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2.5f,4f); m.startSpeed=new ParticleSystem.MinMaxCurve(2,6); m.startSize=new ParticleSystem.MinMaxCurve(12,20); m.maxParticles=20; m.gravityModifier=0.25f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#9a8e7e"),FxKit.Hex("#7a7064"));
    var s=cu.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Box; s.scale=new Vector3(26,16,26);
    FxKit.Burst(cu,0,12,12); FxKit.Burst(cu,0.3f,8,8); FxKit.Sheet2x2(cu); FxKit.Spin(cu,-20,20); FxKit.Drag(cu,0.3f);
    FxKit.SizeLife(cu,0,.6f,1,1.8f); FxKit.ColorLife(cu,FxKit.Grad(new[]{"0:#ffffff","1:#e0d8d0"},0,0,.15f,.8f,1,0)); }
  return FxKit.Stats("fx-collapse-dust");
} }
