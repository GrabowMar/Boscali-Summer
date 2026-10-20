using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("fx-flare-burst");
  var fl = FxKit.Sys(root,"Flash",M("M_Flash_Add"),0.05f);
  { var m=fl.main; m.startLifetime=0.18f; m.startSize=14; m.maxParticles=1; m.startColor=FxKit.Hex("#fffaf0");
    FxKit.Burst(fl,0,1,1); FxKit.SizeLife(fl,0,.4f,.25f,1,1,.7f); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","1:#fff0c0"},0,1,1,0)); }
  var co = FxKit.Sys(root,"Core",M("M_Flash_Add"),1.0f);
  { var m=co.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.08f,0.16f); m.startSize=new ParticleSystem.MinMaxCurve(4f,7f); m.maxParticles=8; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffffff"),FxKit.Hex("#fff0b0"));
    var e=co.emission; e.rateOverTime=FxKit.Curve(40,0,1,0.7f,.8f,1,0); FxKit.ColorLife(co,FxKit.Grad(new[]{"0:#ffffff","1:#ffe8a0"},0,1,1,0)); }
  var sp = FxKit.Sys(root,"Sparkle",M("M_Spark_Add"),0.6f);
  { var m=sp.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.5f,1.3f); m.startSpeed=new ParticleSystem.MinMaxCurve(5,16); m.startSize=new ParticleSystem.MinMaxCurve(0.4f,0.8f); m.maxParticles=80; m.gravityModifier=0.6f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffffff"),FxKit.Hex("#fff0c0"));
    var s=sp.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=0.5f;
    FxKit.Burst(sp,0,30,30); var e=sp.emission; e.rateOverTime=FxKit.Curve(70,0,1,1,0.2f); FxKit.Drag(sp,2f);
    var r=FxKit.R(sp); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.05f; r.lengthScale=1.2f;
    FxKit.ColorLife(sp,FxKit.Grad(new[]{"0:#ffffff","0.6:#fff4d0","1:#ffb050"},0,1,.2f,.4f,.35f,1,.5f,.3f,.65f,1,1,0)); }
  var sm = FxKit.Sys(root,"Smoke",M("M_Smoke_Lit"),0.8f);
  { var m=sm.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1f,1.4f); m.startSpeed=new ParticleSystem.MinMaxCurve(0.5f,2f); m.startSize=new ParticleSystem.MinMaxCurve(2f,3.5f); m.maxParticles=12;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#e8e8e8"),FxKit.Hex("#c8c8c8"));
    var s=sm.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=0.5f;
    var e=sm.emission; e.rateOverTime=12; FxKit.Sheet2x2(sm); FxKit.Spin(sm,-30,30); FxKit.Drag(sm,1f);
    FxKit.SizeLife(sm,0,.5f,1,2.2f); FxKit.ColorLife(sm,FxKit.Grad(new[]{"0:#ffffff","1:#ffffff"},0,0,.15f,.45f,1,0)); }
  return FxKit.Stats("fx-flare-burst");
} }
