using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("ops-cruise-impact");
  var fl = FxKit.Sys(root,"Flash",M("M_Flash_Add"),0.05f); fl.transform.localPosition=new Vector3(0,10,0);
  { var m=fl.main; m.startLifetime=0.25f; m.startSize=new ParticleSystem.MinMaxCurve(60,75); m.maxParticles=2; m.startColor=FxKit.Hex("#ffb060");
    FxKit.Burst(fl,0,1,1); FxKit.SizeLife(fl,0,.6f,.2f,1,1,1); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","0.3:#ffc070","1:#ff5010"},0,1,.4f,.8f,1,0)); }
  var fb = FxKit.Sys(root,"Fireball",M("M_Fire_Add"),0.15f); fb.transform.localPosition=new Vector3(0,6,0);
  { var m=fb.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.7f,1.3f); m.startSpeed=new ParticleSystem.MinMaxCurve(10,25); m.startSize=new ParticleSystem.MinMaxCurve(16,28); m.maxParticles=16;
    var s=fb.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Hemisphere; s.radius=5; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(fb,0,14,14); FxKit.Sheet2x2(fb); FxKit.Spin(fb,-60,60); FxKit.Drag(fb,0.06f,true); FxKit.SizeLife(fb,0,.5f,.3f,1.1f,1,1.5f);
    FxKit.ColorLife(fb,FxKit.Grad(new[]{"0:#ffd090","0.15:#ff8020","0.5:#b02c04","1:#200800"},0,.8f,.4f,.6f,1,0)); }
  var rg = FxKit.Sys(root,"ShockRing",M("M_Ring_Alpha"),0.05f); rg.transform.localPosition=new Vector3(0,1.5f,0);
  { var m=rg.main; m.startLifetime=0.8f; m.startSize=130; m.maxParticles=1; m.startColor=FxKit.Hex("#b8a488");
    FxKit.Burst(rg,0,1,1); FxKit.SizeLife(rg,0,.08f,.35f,.75f,1,1); FxKit.ColorLife(rg,FxKit.Grad(new[]{"0:#ffffff","1:#ffffff"},0,.9f,.5f,.5f,1,0));
    FxKit.R(rg).renderMode=ParticleSystemRenderMode.HorizontalBillboard; }
  var sk = FxKit.Sys(root,"DustSkirt",M("M_Smoke_Lit"),0.15f); sk.transform.localPosition=new Vector3(0,3,0);
  { var m=sk.main; m.startLifetime=new ParticleSystem.MinMaxCurve(3.5f,5.5f); m.startSpeed=new ParticleSystem.MinMaxCurve(45,75); m.startSize=new ParticleSystem.MinMaxCurve(12,20); m.maxParticles=45; m.gravityModifier=-0.03f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#8a7860"),FxKit.Hex("#6a5c4c"));
    var s=sk.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Circle; s.radius=4; s.radiusThickness=0; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(sk,0.02f,30,30); FxKit.Burst(sk,0.1f,12,12); FxKit.Sheet2x2(sk); FxKit.Spin(sk,-30,30); FxKit.Drag(sk,0.035f,true); FxKit.SizeLife(sk,0,.5f,.3f,1.4f,1,2.4f);
    FxKit.ColorLife(sk,FxKit.Grad(new[]{"0:#ffffff","1:#d8d0c8"},0,.9f,.5f,.6f,1,0)); }
  var cl = FxKit.Sys(root,"Clods",M("M_Debris"),0.1f); cl.transform.localPosition=new Vector3(0,2,0);
  { var m=cl.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2f,3.5f); m.startSpeed=new ParticleSystem.MinMaxCurve(25,55); m.startSize=new ParticleSystem.MinMaxCurve(1.5f,4f); m.maxParticles=40; m.gravityModifier=1.6f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#2e2620"),FxKit.Hex("#5a4a3c"));
    var s=cl.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=38; s.radius=3; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(cl,0,40,40); FxKit.Sheet2x2(cl); FxKit.Spin(cl,-400,400); FxKit.Drag(cl,0.1f);
    FxKit.ColorLife(cl,FxKit.Grad(new[]{"0:#ffffff","1:#ffffff"},0,1,.9f,1,1,0));
    FxKit.Trails(cl,M("M_Trail_Smoke"),0.45f,5f,FxKit.Grad(new[]{"0:#9a9088","1:#5a5450"},0,.75f,.6f,.45f,1,0),1.5f,.25f,1f); { var t=cl.trails; t.inheritParticleColor=false; } }
  var sp = FxKit.Sys(root,"Sparks",M("M_Spark_Add"),0.1f); sp.transform.localPosition=new Vector3(0,3,0);
  { var m=sp.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.6f,1.6f); m.startSpeed=new ParticleSystem.MinMaxCurve(35,80); m.startSize=new ParticleSystem.MinMaxCurve(1.2f,2.2f); m.maxParticles=35; m.gravityModifier=1.5f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffc080"),FxKit.Hex("#ff7020"));
    var s=sp.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=55; s.radius=2; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(sp,0,35,35); FxKit.Drag(sp,0.01f,true);
    var r=FxKit.R(sp); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.04f; r.lengthScale=1.5f;
    FxKit.ColorLife(sp,FxKit.Grad(new[]{"0:#ffe0b0","0.3:#ff9030","1:#ff3000"},0,1,.7f,1,1,0)); }
  var co = FxKit.Sys(root,"SmokeColumn",M("M_Smoke_Lit"),3.2f); co.transform.localPosition=new Vector3(0,8,0);
  { var m=co.main; m.startLifetime=new ParticleSystem.MinMaxCurve(5f,8f); m.startSpeed=new ParticleSystem.MinMaxCurve(4,8); m.startSize=new ParticleSystem.MinMaxCurve(12,20); m.maxParticles=50; m.startDelay=0.15f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#2a2622"),FxKit.Hex("#4a4440"));
    var s=co.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=12; s.radius=5; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(co,0,8,8); var e=co.emission; e.rateOverTime=FxKit.Curve(16,0,1,1,0.45f); FxKit.Sheet2x2(co); FxKit.Spin(co,-20,20); FxKit.Noise(co,4,0.04f,0.3f);
    var v=co.velocityOverLifetime; v.enabled=true; v.space=ParticleSystemSimulationSpace.Local; v.x=FxKit.Curve(1,0,0,1,0); v.z=FxKit.Curve(1,0,0,1,0); v.y=FxKit.Curve(1,0,14,0.4f,9,1,5);
    FxKit.SizeLife(co,0,.5f,.3f,1.2f,1,2.6f); FxKit.ColorLife(co,FxKit.Grad(new[]{"0:#ff9a50","0.1:#ffffff","1:#ffffff"},0,0,.08f,.9f,.6f,.7f,1,0)); }
  return FxKit.Stats("ops-cruise-impact");
} }
