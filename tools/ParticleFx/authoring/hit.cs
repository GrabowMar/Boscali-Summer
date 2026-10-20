using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("fx-hit-sparks");
  var fl = FxKit.Sys(root,"Pop",M("M_Glow_Add"),0.05f);
  { var m=fl.main; m.startLifetime=0.09f; m.startSize=new ParticleSystem.MinMaxCurve(2.2f,3f); m.maxParticles=2; m.startColor=FxKit.Hex("#ffb060");
    FxKit.Burst(fl,0,1,1); FxKit.Burst(fl,0.05f,1,1); FxKit.SizeLife(fl,0,.6f,1,1.2f); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","1:#ff6a20"},0,.9f,1,0)); }
  var sp = FxKit.Sys(root,"Sparks",M("M_Spark_Add"),0.12f);
  { var m=sp.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.35f,0.9f); m.startSpeed=new ParticleSystem.MinMaxCurve(6,14); m.startSize=new ParticleSystem.MinMaxCurve(0.08f,0.16f); m.maxParticles=40; m.gravityModifier=1f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffb060"),FxKit.Hex("#ff7020"));
    var s=sp.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=0.2f;
    FxKit.Burst(sp,0,24,24); FxKit.Burst(sp,0.06f,10,10); FxKit.Drag(sp,0.35f,true);
    var r=FxKit.R(sp); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.04f; r.lengthScale=1.5f;
    FxKit.ColorLife(sp,FxKit.Grad(new[]{"0:#ffffff","0.3:#ffa040","1:#ff3000"},0,1,.7f,.9f,1,0));
    FxKit.Trails(sp,M("M_Spark_Add"),0.25f,0.08f,FxKit.Grad(new[]{"0:#ffa040","1:#ff3000"},0,.7f,1,0),0.1f); }
  var sm = FxKit.Sys(root,"SmokePuff",M("M_Smoke_Lit"),0.1f);
  { var m=sm.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.9f,1.4f); m.startSpeed=new ParticleSystem.MinMaxCurve(0.5f,2f); m.startSize=new ParticleSystem.MinMaxCurve(1.2f,2f); m.maxParticles=6; m.gravityModifier=-0.02f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#6a6662"),FxKit.Hex("#4a4642"));
    var s=sm.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=0.3f;
    FxKit.Burst(sm,0.02f,5,5); FxKit.Sheet2x2(sm); FxKit.Spin(sm,-40,40); FxKit.Drag(sm,1f);
    FxKit.SizeLife(sm,0,.5f,.4f,1.4f,1,2.2f); FxKit.ColorLife(sm,FxKit.Grad(new[]{"0:#ffa060","0.2:#ffffff","1:#ffffff"},0,0,.1f,.6f,1,0)); }
  return FxKit.Stats("fx-hit-sparks");
} }
