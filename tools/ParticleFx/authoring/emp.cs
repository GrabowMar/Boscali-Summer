using UnityEngine;
using UnityEditor;
public class Script { public static object Main(){
  Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Fx/Materials/"+n+".mat");
  var root = FxKit.Root("ops-emp-burst");
  var C = new Vector3(0,60,0);
  // flash
  var fl = FxKit.Sys(root,"Flash",M("M_Flash_Add"),0.1f); fl.transform.localPosition=C;
  { var m=fl.main; m.startLifetime=0.4f; m.startSize=260; m.maxParticles=2; m.startColor=FxKit.Hex("#d8ecff");
    FxKit.Burst(fl,0,1,1); FxKit.SizeLife(fl,0,.4f,.2f,1,1,.8f); FxKit.ColorLife(fl,FxKit.Grad(new[]{"0:#ffffff","1:#4a7cff"},0,1,.3f,.6f,1,0)); }
  // ionised haze
  var hz = FxKit.Sys(root,"IonHaze",M("M_Glow_Add"),0.2f); hz.transform.localPosition=C;
  { var m=hz.main; m.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,1.9f); m.startSize=new ParticleSystem.MinMaxCurve(180,300); m.maxParticles=8; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#3a7cff"),FxKit.Hex("#6a5cff"));
    var s=hz.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=50; FxKit.Burst(hz,0.02f,7,7); FxKit.SizeLife(hz,0,.5f,1,1.2f);
    FxKit.ColorLife(hz,FxKit.Grad(new[]{"0:#ffffff","1:#3050ff"},0,0,.1f,.45f,.5f,.25f,1,0)); }
  // shock bubble ring (camera-facing)
  var rg = FxKit.Sys(root,"PulseRing",M("M_Ring_Add"),0.1f); rg.transform.localPosition=C;
  { var m=rg.main; m.startLifetime=0.55f; m.startSize=330; m.maxParticles=2; m.startColor=FxKit.Hex("#9cd4ff");
    FxKit.Burst(rg,0,1,1); FxKit.Burst(rg,0.1f,1,1); FxKit.SizeLife(rg,0,.05f,.5f,.8f,1,1); FxKit.ColorLife(rg,FxKit.Grad(new[]{"0:#ffffff","1:#4060ff"},0,1,.6f,.6f,1,0)); }
  // radiating arcs (stretched bolts), crackling bursts
  var ar = FxKit.Sys(root,"Arcs",M("M_Bolt_Add"),0.6f); ar.transform.localPosition=C;
  { var m=ar.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.1f,0.22f); m.startSpeed=new ParticleSystem.MinMaxCurve(150,320); m.startSize=new ParticleSystem.MinMaxCurve(16,28); m.maxParticles=70; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#a8c8ff"),FxKit.Hex("#4a6cff"));
    var s=ar.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=15;
    FxKit.Burst(ar,0,16,16); FxKit.Burst(ar,0.05f,5,7,12,0.05f);
    FxKit.Sheet2x2(ar); var r=FxKit.R(ar); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0; r.lengthScale=10; FxKit.ColorLife(ar,FxKit.Grad(new[]{"0:#ffffff","1:#6a8cff"},0,1,.5f,.4f,.6f,1,1,0)); }
  // blue-white streak sparks
  var sp = FxKit.Sys(root,"Sparks",M("M_Spark_Add"),0.1f); sp.transform.localPosition=C;
  { var m=sp.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.5f,1.3f); m.startSpeed=new ParticleSystem.MinMaxCurve(120,260); m.startSize=new ParticleSystem.MinMaxCurve(2.5f,4.5f); m.maxParticles=120; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#a8c8ff"),FxKit.Hex("#4a6cff"));
    var s=sp.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=10; FxKit.Burst(sp,0,90,90); FxKit.Burst(sp,0.12f,30,30); FxKit.Drag(sp,0.003f,true);
    var r=FxKit.R(sp); r.renderMode=ParticleSystemRenderMode.Stretch; r.velocityScale=0.12f; r.lengthScale=1;
    FxKit.ColorLife(sp,FxKit.Grad(new[]{"0:#ffffff","0.5:#8cc8ff","1:#3a50ff"},0,1,.6f,.8f,1,0)); }
  // crawling mini-arcs at random spots inside the bubble
  var ca = FxKit.Sys(root,"CrawlArcs",M("M_Bolt_Add"),1.6f); ca.transform.localPosition=C;
  { var m=ca.main; m.startLifetime=new ParticleSystem.MinMaxCurve(0.08f,0.16f); m.startSize3D=true; m.startSizeX=new ParticleSystem.MinMaxCurve(12,24); m.startSizeY=new ParticleSystem.MinMaxCurve(40,90); m.startSizeZ=1; m.maxParticles=20; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#a8c8ff"),FxKit.Hex("#4a6cff")); m.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
    var s=ca.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=130; s.radiusThickness=0.6f;
    var e=ca.emission; e.rateOverTime=FxKit.Curve(90,0,1,0.6f,.6f,1,0.1f); FxKit.Sheet2x2(ca); }
  // falling flickering motes
  var mo = FxKit.Sys(root,"Motes",M("M_Ember_Add"),1.2f); mo.transform.localPosition=C;
  { var m=mo.main; m.startLifetime=new ParticleSystem.MinMaxCurve(2.5f,5f); m.startSpeed=new ParticleSystem.MinMaxCurve(2,10); m.startSize=new ParticleSystem.MinMaxCurve(3.5f,6.5f); m.maxParticles=150; m.gravityModifier=0.15f; m.startColor=new ParticleSystem.MinMaxGradient(FxKit.Hex("#c8e8ff"),FxKit.Hex("#7a8cff"));
    var s=mo.shape; s.enabled=true; s.shapeType=ParticleSystemShapeType.Sphere; s.radius=150; s.radiusThickness=0.8f;
    FxKit.Burst(mo,0.05f,90,90); var e=mo.emission; e.rateOverTime=45; FxKit.Noise(mo,10,0.03f,0.6f); FxKit.Drag(mo,0.6f);
    FxKit.ColorLife(mo,FxKit.Grad(new[]{"0:#ffffff","0.6:#90b8ff","1:#4050ff"},0,0,.05f,1,.2f,.25f,.35f,1,.5f,.2f,.7f,.85f,1,0)); }
  return FxKit.Stats("ops-emp-burst");
} }
