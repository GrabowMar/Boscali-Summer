using UnityEngine; using UnityEditor; using System.Text;
public class Script { public static object Main(){
  var sb=new StringBuilder();
  foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Fx/Prefabs"})) {
    var path=AssetDatabase.GUIDToAssetPath(g); var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);
    int total=0; float end=0; var bad=new StringBuilder();
    if (go.name!=System.IO.Path.GetFileNameWithoutExtension(path)) bad.Append(" name-mismatch");
    if (go.transform.position!=Vector3.zero) bad.Append(" root-not-origin");
    foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Camera>()||t.GetComponent<Light>()||t.GetComponent<MeshRenderer>()) bad.Append(" extra:"+t.name);
    foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true)) {
      var m=p.main; total+=m.maxParticles;
      if (m.loop) bad.Append(" loop:"+p.name); if(!m.playOnAwake) bad.Append(" noPOA:"+p.name); if(m.scalingMode!=ParticleSystemScalingMode.Hierarchy) bad.Append(" scale:"+p.name);
      float life=m.startLifetime.mode==ParticleSystemCurveMode.Constant?m.startLifetime.constant:m.startLifetime.constantMax;
      end=Mathf.Max(end,m.startDelay.constantMax+m.duration+life);
      var r=p.GetComponent<ParticleSystemRenderer>();
      foreach (var mat in new[]{r.sharedMaterial, p.trails.enabled? r.trailMaterial : null}) { if (mat==null) { if (r.sharedMaterial==null) bad.Append(" nomat:"+p.name); continue; }
        var mp=AssetDatabase.GetAssetPath(mat); if(!mp.StartsWith("Assets/Fx/")) bad.Append(" extmat:"+mp);
        if(!mat.shader.name.StartsWith("Universal Render Pipeline/Particles/")) bad.Append(" shader:"+mat.shader.name);
        var tex=mat.GetTexture("_BaseMap"); if(tex==null||!AssetDatabase.GetAssetPath(tex).StartsWith("Assets/Fx/")) bad.Append(" tex:"+mat.name); }
    }
    sb.AppendLine(go.name+" systems="+go.GetComponentsInChildren<ParticleSystem>(true).Length+" maxTotal="+total+" lifetime<="+end.ToString("0.0")+(bad.Length>0?" PROBLEMS:"+bad:" OK"));
  }
  return sb.ToString();
} }
