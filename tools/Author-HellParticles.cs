// Editor-only fixture for Compile-HellParticles.ps1; no authoring framework enters the bundle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Rql;
using Rql.Execution;
using com.IvanMurzak.Unity.MCP.Editor.Rql.Particles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AuthorHellParticles
{
    private static readonly List<string> Queries = new List<string>();
    private static readonly List<JsonNode> Results = new List<JsonNode>();

    public static void Run()
    {
        try
        {
            var root = new GameObject("HellPortalParticles");
            string[] names = { "Smoke", "Ash", "Cinders", "Arcs" };
            int[] caps = { 96, 120, 32, 12 };
            var simulation = new JsonArray();
            var systems = new List<ParticleSystem>();
            foreach (string name in names)
            {
                var child = new GameObject(name);
                child.transform.SetParent(root.transform, false);
                ParticleSystem ps = child.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = (uint)(7100 + systems.Count);
                systems.Add(ps);
            }
            var engine = new RqlEngine();
            engine.Mutations.Enabled = true;
            engine.AddAdapter(new HtmlColorAdapter());
            engine.AddModules(new[] { typeof(ParticleSystemModule) });
            engine.AddSource("particles", () => systems);
            engine.AddFunction("hell.fade", _ =>
            {
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.85f, .15f), new GradientAlphaKey(.65f, .65f), new GradientAlphaKey(0f, 1f) });
                return gradient;
            });
            foreach (string name in names)
                Mutate(engine, name, "main.loop = true, main.playOnAwake = false, main.duration = 12, main.simulationSpace = \"Local\", " +
                    "main.cullingMode = \"Automatic\", emission.enabled = true, emission.rateOverDistance = 0, shape.enabled = true, " +
                    "shape.shapeType = \"Circle\", shape.radius = 145, shape.radiusThickness = 0.35, shape.rotation.x = -90, " +
                    "colorOverLifetime.enabled = true, colorOverLifetime.color = hell.fade(), collision.enabled = false, " +
                    "trails.enabled = false, lights.enabled = false, subEmitters.enabled = false");

            Mutate(engine, "Smoke", "main.maxParticles = 96, main.startLifetime = [7,12], main.startSpeed = [1.2,3], " +
                "main.startSize = [20,38], main.startColor = \"#29262952\", main.gravityModifier = 0, emission.rateOverTime = 8");
            Mutate(engine, "Ash", "main.maxParticles = 120, main.startLifetime = [7,12], main.startSpeed = [0.2,0.6], " +
                "main.startSize = [0.3,0.8], main.startColor = \"#77716a7f\", main.gravityModifier = 0.012, emission.rateOverTime = 9");
            Mutate(engine, "Cinders", "main.maxParticles = 32, main.startLifetime = [1.5,3], main.startSpeed = [2,5], " +
                "main.startSize = [0.35,0.9], main.startColor = \"#913520a0\", main.gravityModifier = 0.07, emission.rateOverTime = 6");
            Mutate(engine, "Arcs", "main.maxParticles = 12, main.startLifetime = [0.12,0.28], main.startSpeed = [12,25], " +
                "main.startSize = [0.5,1], main.startColor = \"#64304050\", main.gravityModifier = 0, emission.rateOverTime = 2");
            systems[1].transform.localPosition = Vector3.up * 12f;
            foreach (ParticleSystem ps in systems)
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = null; // runtime supplies the game's shared smoke/spark materials
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.maxParticleSize = .12f;
                renderer.renderMode = ps.name == "Arcs" || ps.name == "Cinders"
                    ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
                renderer.velocityScale = ps.name == "Arcs" ? .06f : .025f;
                renderer.lengthScale = ps.name == "Arcs" ? 7f : 2f;
            }
            for (int i = 0; i < systems.Count; i++)
            {
                ParticleSystem ps = systems[i];
                Require(ps.main.maxParticles == caps[i], ps.name + " cap");
                Require(ps.main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants, ps.name + " lifetime coercion");
                Require(ps.main.startColor.mode == ParticleSystemGradientMode.Color, ps.name + " color coercion");
                Require(ps.colorOverLifetime.color.mode == ParticleSystemGradientMode.Gradient, ps.name + " gradient coercion");
                Require(Mathf.Approximately(ps.shape.radius, 145f), ps.name + " radius");
                Require(ps.shape.shapeType == ParticleSystemShapeType.Circle && Mathf.Approximately(ps.shape.rotation.x, -90f), ps.name + " ground-plane shape");
                Require(ps.main.simulationSpace == ParticleSystemSimulationSpace.Local && !ps.main.playOnAwake, ps.name + " local simulation and explicit start");
                Require(!ps.collision.enabled && !ps.lights.enabled && !ps.trails.enabled && !ps.subEmitters.enabled, ps.name + " disabled expensive modules");
                Query(engine, "particles | where name == \"" + ps.name + "\" | select name, main.maxParticles, main.startLifetime, main.startSpeed, main.startSize, main.startColor, emission.rateOverTime, shape.radius");
                Query(engine, "particles | where name == \"" + ps.name + "\" | call Simulate(15)", true);
                Require(ps.particleCount <= caps[i], ps.name + " simulation cap");
                simulation.Add(new JsonObject { ["emitter"] = ps.name, ["seconds"] = 15, ["particles"] = ps.particleCount, ["cap"] = caps[i] });
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            Require(systems.Sum(ps => ps.main.maxParticles) == 260, "total live cap");
            Require(root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "native prefab components only");
            const string path = "Assets/HellParticles/HellPortalParticles.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var build = new AssetBundleBuild { assetBundleName = "hell-particles.bundle", assetNames = new[] { path } };
            Require(BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) != null, "asset bundle compilation");
            Require(new FileInfo("BundleOutput/hell-particles.bundle").Length < 256 * 1024, "bundle size");
            AssetBundle bundle = AssetBundle.LoadFromFile(Path.GetFullPath("BundleOutput/hell-particles.bundle"));
            Require(bundle != null, "bundle reload");
            GameObject packed = bundle.LoadAsset<GameObject>(path);
            Require(packed != null && packed.GetComponentsInChildren<ParticleSystem>(true).Length == 4, "four exported native emitters");
            Require(packed.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "no authoring framework in bundle");
            Require(packed.GetComponentsInChildren<ParticleSystem>(true).Sum(ps => ps.main.maxParticles) == 260, "exported total cap");
            bundle.Unload(true);
            var evidence = new JsonObject { ["source"] = "https://github.com/IvanMurzak/Unity-AI-ParticleSystem", ["commit"] = "b51172a9cac5f7e01cb790c4ab91347d8e821319",
                ["module"] = typeof(ParticleSystemModule).FullName, ["transport"] = "direct RqlEngine; no MCP server", ["baseRadius"] = 145, ["totalCap"] = 260,
                ["native_simulation"] = simulation };
            var calls = new JsonArray();
            for (int i = 0; i < Queries.Count; i++) calls.Add(new JsonObject { ["query"] = Queries[i], ["result"] = Results[i] });
            evidence["calls"] = calls;
            File.WriteAllText("rql-authoring.json", evidence.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText("build-result.txt", "PASS hell-particles.bundle: actual pinned ParticleSystemModule RQL mutations + readbacks; four native emitters; caps 96/120/32/12=260; 145m radius; no framework asset dependencies");
            UnityEngine.Object.DestroyImmediate(root);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("build-result.txt", "FAIL " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void Mutate(RqlEngine engine, string name, string assignments) =>
        Query(engine, "particles | where name == \"" + name + "\" | set " + assignments, true);

    private static void Query(RqlEngine engine, string text, bool mutate = false)
    {
        QueryResult result = engine.Execute(text, options: new QueryOptions { AllowMutation = mutate });
        string json = result.ToJson();
        Queries.Add(text);
        Results.Add(JsonNode.Parse(json));
        if (!result.Ok) throw new Exception("RQL failed: " + text + "\n" + json);
        if (!mutate) Require(result.Rows != null && result.Rows.Count == 1, "one RQL parameter readback per emitter");
    }

    private static void Require(bool valid, string detail)
    {
        if (!valid) throw new Exception("Particle authoring assertion: " + detail);
    }

    // Color coercion is normally provided by the full Unity-MCP core. This fixture supplies only its hex format.
    private sealed class HtmlColorAdapter : RqlTypeAdapter<Color>
    {
        public override AdapterOutcome TryCoerce(object value, Type target, RqlKernel kernel, out object result, out string reason)
        {
            if (value is string hex && ColorUtility.TryParseHtmlString(hex, out Color color))
            {
                result = color;
                reason = null;
                return AdapterOutcome.Ok;
            }
            result = null;
            reason = "expected #RRGGBB or #RRGGBBAA";
            return AdapterOutcome.Failed;
        }

        protected override JsonNode Encode(Color value, EncodeContext context) => JsonValue.Create("#" + ColorUtility.ToHtmlStringRGBA(value));
    }
}
