using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("wx-lightning-strike");
  // double flash pulse at the ground
  var fl = FxKit.Sys(root,"FlashPulse",M("M_Flash_Add"),0.3f); fl.transform.localPosition=new Vector3(0,4,0);
  { var m=fl.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.08f,0.14f); m.startSize=new ParticleSystem.MinMaxCurve(50,70); m.maxParticles=4; m.startColor=FxKit.Hex("#e0ecff");
    FxKit.Burst(fl,0,1,1); FxKit.Burst(fl,0.09f,1,1); FxKit.Burst(fl,0.22f,1,1); FxKit.SizeLife(fl,0,.7f,1,1); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","1:#8ab0ff"},0,1,1,0)); }
  // glare halo
  var gl = FxKit.Sys(root,"Glare",M("M_Glow_Add"),0.1f); gl.transform.localPosition=new Vector3(0,30,0);
  { var m=gl.main; m.startLifetime=0.45f; m.startSize=100; m.maxParticles=1; m.startColor=FxKit.Hex("#9ab8ff");
    FxKit.Burst(gl,0,1,1); FxKit.ColorLife(gl,FxKit.Grad(new[]{"0:#ffffff","1:#5a7cff"},0,.8f,.2f,.5f,.45f,.6f,1,0)); }
  // ground scorch glow (flat)
  var gg = FxKit.Sys(root,"GroundGlow",M("M_Glow_Add"),0.1f); gg.transform.localPosition=new Vector3(0,0.5f,0);
  { var m=gg.main; m.startLifetime=0.9f; m.startSize=36; m.maxParticles=1; m.startColor=FxKit.Hex("#c8d8ff");
    FxKit.Burst(gg,0,1,1); FxKit.R(gg).renderMode=ParticleSystemRenderMode.HorizontalBillboard; FxKit.ColorLife(gg,FxKit.Grad(new[]{"0:#ffffff","0.3:#a0b8ff","1:#ff6020"},0,1,.4f,.9f,1,0)); }
  // ground arcs crawling outward
  var ar = FxKit.Sys(root,"GroundArcs",M("M_Bolt_Add"),0.3f); ar.transform.localPosition=new Vector3(0,0.6f,0);
  { var m=ar.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.1f,0.2f); m.startSpeed=new ParticleSystem.MinMaxCurve(50,90); m.startSize=new ParticleSystem.MinMaxCurve(3.5f,6f); m.maxParticles=24; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#d0e0ff"),FxKit.Hex("#6a8cff"));
    var s=ar.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Circle; s.radius=1.5f; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(ar,0,8,8); FxKit.Burst(ar,0.09f,6,6); FxKit.Burst(ar,0.22f,6,6); FxKit.Sheet2x2(ar);
    var r=FxKit.R(ar); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0; r.lengthScale=5; }
  // molten sparks
  var sp = FxKit.Sys(root,"Sparks",M("M_Spark_Add"),0.25f); sp.transform.localPosition=new Vector3(0,1,0);
  { var m=sp.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.5f,1.4f); m.startSpeed=new ParticleSystem.MinMaxCurve(12,38); m.startSize=new ParticleSystem.MinMaxCurve(0.7f,1.3f); m.maxParticles=60; m.gravityModifier=1.5f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#e8f0ff"),FxKit.Hex("#ffd8a0"));
    var s=sp.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=60; s.radius=1; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(sp,0,40,40); FxKit.Burst(sp,0.1f,15,15); FxKit.Drag(sp,0.02f,true);
    var r=FxKit.R(sp); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.05f; r.lengthScale=1.5f;
    FxKit.ColorLife(sp,FxKit.Grad(new[]{"0:#ffffff","0.25:#c8d8ff","0.5:#ffb060","1:#ff4010"},0,1,.7f,1,1,0)); }
  // steam / dust puff
  var st = FxKit.Sys(root,"SteamPuff",M("M_Smoke_Lit"),0.3f); st.transform.localPosition=new Vector3(0,2,0);
  { var m=st.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1.4f,2.2f); m.startSpeed=new ParticleSystem.MinMaxCurve(3,9); m.startSize=new ParticleSystem.MinMaxCurve(6,10); m.maxParticles=16; m.gravityModifier=-0.08f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#c8c4c0"),FxKit.Hex("#9a9690"));
    var s=st.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Hemisphere; s.radius=4; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(st,0.03f,10,10); FxKit.Burst(st,0.2f,5,5); FxKit.Sheet2x2(st); FxKit.Spin(st,-30,30); FxKit.Drag(st,0.05f,true);
    FxKit.SizeLife(st,0,.5f,.4f,1.4f,1,2.2f); FxKit.ColorLife(st,FxKit.Grad(new[]{"0:#c8d8ff","0.2:#ffffff","1:#ffffff"},0,0,.08f,.75f,1,0)); }
  return FxKit.Stats("wx-lightning-strike");
} }
