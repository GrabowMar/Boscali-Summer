using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  FxKit.MakeTextures();
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("ops-rod-impact");

  // 1 flash core
  var fl = FxKit.Sys(root,"FlashCore",M("M_Flash_Add"),0.1f);
  { var m=fl.main; m.startLifetime=0.45f; m.startSize=new ParticleSystem.MinMaxCurve(150,180); m.maxParticles=3; m.startColor=FxKit.Hex("#fff6e8");
    FxKit.Burst(fl,0,2,2); FxKit.SizeLife(fl,0,.5f,.15f,1,1,1.1f);
    FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","0.3:#cfe0ff","1:#ffb070"},0,1,.25f,.8f,1,0)); fl.transform.localPosition=new Vector3(0,20,0); }
  // 1b glare
  var gl = FxKit.Sys(root,"Glare",M("M_Glow_Add"),0.1f);
  { var m=gl.main; m.startLifetime=0.7f; m.startSize=420; m.maxParticles=1; m.startColor=FxKit.Hex("#bcd2ff");
    FxKit.Burst(gl,0,1,1); FxKit.SizeLife(gl,0,.6f,1,1); FxKit.ColorLife(gl,FxKit.Grad(new[]{"0:#ffffff","1:#8aa8ff"},0,.7f,1,0)); gl.transform.localPosition=new Vector3(0,30,0); }
  // 2 lingering white-hot crater glow
  var cg = FxKit.Sys(root,"CoreGlow",M("M_Fire_Add"),0.3f);
  { var m=cg.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,2.2f); m.startSize=new ParticleSystem.MinMaxCurve(40,70); m.maxParticles=10; m.startSpeed=new ParticleSystem.MinMaxCurve(4,12);
    var s=cg.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Hemisphere; s.radius=12; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(cg,0,8,8); FxKit.Sheet2x2(cg); FxKit.Spin(cg,-20,20); FxKit.SizeLife(cg,0,.6f,1,1.3f);
    FxKit.ColorLife(cg,FxKit.Grad(new[]{"0:#ffffff","0.25:#ffd27a","0.6:#ff6a1a","1:#5a1a08"},0,1,.5f,.8f,1,0)); }
  // 3 debris chunks with smoke trails
  var db = FxKit.Sys(root,"Debris",M("M_Debris"),0.15f);
  { var m=db.main; m.startLifetime=new ParticleSystem.MinMaxCurve(3.5f,6f); m.startSpeed=new ParticleSystem.MinMaxCurve(45,95); m.startSize=new ParticleSystem.MinMaxCurve(4,10); m.gravityModifier=2f; m.maxParticles=60;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#2a2420"),FxKit.Hex("#5a4c40"));
    var s=db.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=50; s.radius=8; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(db,0,45,45); FxKit.Burst(db,0.08f,15,15); FxKit.Sheet2x2(db); FxKit.Spin(db,-400,400); FxKit.Drag(db,0.08f);
    FxKit.ColorLife(db,FxKit.Grad(new[]{"0:#ffffff","1:#ffffff"},0,1,.85f,1,1,0));
    FxKit.Trails(db,M("M_Trail_Smoke"),0.4f,16f,FxKit.Grad(new[]{"0:#b0aaa2","1:#6a6662"},0,.85f,.6f,.5f,1,0),3f,.25f,1f); { var tr0=db.trails; tr0.inheritParticleColor=false; } }
  // 4 hot debris: stretched white-orange streaks with ember trails
  var hd = FxKit.Sys(root,"HotDebris",M("M_Spark_Add"),0.15f);
  { var m=hd.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2.5f,4.5f); m.startSpeed=new ParticleSystem.MinMaxCurve(60,140); m.startSize=new ParticleSystem.MinMaxCurve(4,7); m.gravityModifier=2f; m.maxParticles=50;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffd090"),FxKit.Hex("#ff7020"));
    var s=hd.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Cone; s.angle=55; s.radius=8; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(hd,0,50,50); FxKit.Drag(hd,0.1f);
    var r=FxKit.R(hd); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.04f; r.lengthScale=1.5f;
    FxKit.ColorLife(hd,FxKit.Grad(new[]{"0:#ffffff","0.15:#ffa040","0.6:#ff5a10","1:#c02000"},0,1,.7f,1,1,0));
    FxKit.Trails(hd,M("M_Spark_Add"),0.3f,3.5f,FxKit.Grad(new[]{"0:#ff9a40","1:#d02000"},0,.9f,1,0),2f); }
  // 5 ground dust skirt
  var sk = FxKit.Sys(root,"DustSkirt",M("M_Smoke_Lit"),0.2f);
  { var m=sk.main; m.startLifetime=new ParticleSystem.MinMaxCurve(3.5f,5.5f); m.startSpeed=new ParticleSystem.MinMaxCurve(110,170); m.startSize=new ParticleSystem.MinMaxCurve(45,70); m.maxParticles=70; m.gravityModifier=-0.05f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#8a7860"),FxKit.Hex("#6a5c4c"));
    var s=sk.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Circle; s.radius=15; s.radiusThickness=0; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(sk,0.02f,50,50); FxKit.Burst(sk,0.12f,20,20); FxKit.Sheet2x2(sk); FxKit.Spin(sk,-30,30); FxKit.Drag(sk,0.012f,true); FxKit.SizeLife(sk,0,.5f,.3f,1.4f,1,2.2f);
    FxKit.ColorLife(sk,FxKit.Grad(new[]{"0:#ffffff","1:#d8d0c8"},0,.9f,.5f,.6f,1,0)); sk.transform.localPosition=new Vector3(0,6,0); }
  // 6 shock ring on the ground
  var rg = FxKit.Sys(root,"ShockRing",M("M_Ring_Add"),0.1f);
  { var m=rg.main; m.startLifetime=0.9f; m.startSize=60; m.maxParticles=2; m.startColor=FxKit.Hex("#e8dcc8");
    FxKit.Burst(rg,0,1,1); FxKit.Burst(rg,0.06f,1,1); FxKit.SizeLife(rg,0,.1f,.4f,.75f,1,1); var sl=rg.sizeOverLifetime; sl.sizeMultiplier=9;
    FxKit.ColorLife(rg,FxKit.Grad(new[]{"0:#ffffff","1:#c8b090"},0,1,.5f,.5f,1,0)); FxKit.R(rg).renderMode=ParticleSystemRenderMode.HorizontalBillboard; FxKit.R(rg).maxParticleSize=50; rg.transform.localPosition=new Vector3(0,3,0); }
  // 7 lingering embers
  var em = FxKit.Sys(root,"Embers",M("M_Ember_Add"),1.5f);
  { var m=em.main; m.startLifetime=new ParticleSystem.MinMaxCurve(4f,8f); m.startSpeed=new ParticleSystem.MinMaxCurve(4,18); m.startSize=new ParticleSystem.MinMaxCurve(3,6); m.maxParticles=120; m.gravityModifier=-0.03f; m.startDelay=0.2f;
    m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#ffb060"),FxKit.Hex("#ff5010"));
    var s=em.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Hemisphere; s.radius=70; s.rotation=new Vector3(-90,0,0);
    FxKit.Burst(em,0,60,60); var e=em.emission; e.rateOverTime=40; FxKit.Noise(em,12,0.02f,0.5f); FxKit.Drag(em,0.3f);
    FxKit.ColorLife(em,FxKit.Grad(new[]{"0:#ffffff","0.5:#ffa040","1:#ff2000"},0,1,.1f,.6f,.3f,1,.5f,.3f,.7f,.9f,1,0)); }
  return FxKit.Stats("ops-rod-impact");
} }
