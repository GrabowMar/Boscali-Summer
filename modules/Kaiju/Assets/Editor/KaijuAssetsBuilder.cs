using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// KAIJU art for kaiju.bundle (loaded by modules/Kaiju/Runtime/KaijuAssets.cs). Everything is generated from Kaiju.fbx and
/// the maps baked by BuildKaijuModel.py: import settings, URP materials, the clips, the animator and three prefabs.
/// Clips are sampled from the pose functions below: root-space Euler offsets (pitch about the creature's right axis,
/// yaw about up, roll about forward; + pitch swings a hanging limb backwards) layered on the bind pose, parents first.
/// Edit a pose, run "Boscali/Kaiju/Build assets" in the Unity MCP editor (.nomodkit/unity-mcp) and look again.
/// State names and the timing constants are mirrored in modules/Kaiju/Domain/KaijuAnim.cs.
/// </summary>
public static class KaijuAssetsBuilder
{
    public const string Src = "Assets/BoscaliSummer/Kaiju";
    public const string Gen = Src + "/Generated";
    public static readonly string[] BundleAssets = { Gen + "/KaijuBody.prefab", Gen + "/KaijuSpike.prefab", Gen + "/KaijuBlister.prefab" };
    private const float Fps = 20f;

    private static readonly (string name, float length, bool loop, Action<Pose, float> pose)[] Clips =
    {
        ("Idle", 8f, true, Idle),
        ("Walk", 6f, true, Walk),
        ("Roar", 4.5f, false, Roar),
        ("LaserCharge", 3f, false, LaserCharge),
        ("LaserFire", 2f, true, LaserFire),
        ("Spike", 3.2f, false, Spike),
        ("SwipeL", 2.6f, false, (p, t) => Swipe(p, t, -1)),
        ("SwipeR", 2.6f, false, (p, t) => Swipe(p, t, 1)),
        ("Flinch", 1.2f, false, Flinch),
        ("Death", 6f, false, Death),
    };

    [MenuItem("Boscali/Kaiju/Build assets")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(Src, "Generated");
        Model(Src + "/Kaiju.fbx", true);
        Model(Src + "/KaijuSpike.fbx", false);
        Model(Src + "/KaijuBlister.fbx", false);
        Texture(Src + "/Kaiju_Albedo.png", false, true);
        Texture(Src + "/Kaiju_Emission.png", false, true);
        Texture(Src + "/Kaiju_Normal.png", true, false);

        var hot = new Color(1f, 0.34f, 0.06f);
        var mats = new Dictionary<string, Material>
        {
            ["Body"] = Mat("Kaiju_Body", Color.white, 0.22f, Tex("Kaiju_Albedo"), Tex("Kaiju_Normal"), Tex("Kaiju_Emission"), hot * 3f),
            ["Claws"] = Mat("Kaiju_Claws", new Color(0.07f, 0.06f, 0.05f), 0.5f),
            ["Teeth"] = Mat("Kaiju_Teeth", new Color(0.70f, 0.64f, 0.50f), 0.4f),
            ["Gums"] = Mat("Kaiju_Gums", new Color(0.20f, 0.03f, 0.02f), 0.6f, null, null, null, hot * 0.4f),
            ["EyeInner"] = Mat("Kaiju_EyeInner", new Color(0.02f, 0.01f, 0.01f), 0.9f),
            ["EyeOuter"] = Mat("Kaiju_EyeOuter", new Color(0.9f, 0.5f, 0.1f), 0.9f, null, null, null, new Color(1f, 0.55f, 0.1f) * 6f),
        };
        Material spikeMat = Mat("Kaiju_Spike", new Color(0.09f, 0.075f, 0.06f), 0.5f, null, null, null, hot * 1.5f);
        Material blisterMat = Mat("Kaiju_Blister", new Color(0.25f, 0.05f, 0.02f), 0.7f, null, null, null, hot * 4f);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "/Kaiju.fbx");
        var body = new GameObject("KaijuBody");
        GameObject rig = UnityEngine.Object.Instantiate(model, body.transform, false);
        rig.name = "Model";
        var pose = new Pose(rig.transform);
        AnimatorController controller = Controller(pose);
        var animator = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.avatar = AvatarOf(Src + "/Kaiju.fbx");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // hardpoints ride the bones even off screen
        foreach (SkinnedMeshRenderer smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterial = mats[smr.name];
            smr.updateWhenOffscreen = false;
            Bounds b = smr.localBounds;
            smr.localBounds = new Bounds(b.center, b.size * 1.8f + Vector3.one * 2000f); // cm (rig scale 0.01): poses reach far
        }
        Save(body, "KaijuBody");
        Save(Prop("KaijuSpike", Src + "/KaijuSpike.fbx", spikeMat, Quaternion.Euler(0f, 180f, 0f)), "KaijuSpike");
        Save(Prop("KaijuBlister", Src + "/KaijuBlister.fbx", blisterMat, Quaternion.identity), "KaijuBlister");
        AssetDatabase.SaveAssets();
        Debug.Log("[Kaiju] assets built: " + Clips.Length + " clips");
    }

    /// <summary>Batch entry for tools/Compile-KaijuAssets.ps1: build, then kaiju.bundle into BundleOutput/.</summary>
    public static void BuildBundle()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Build();
            BuildBundleTo("BundleOutput");
            File.WriteAllText("result.txt", "PASS kaiju.bundle: " + string.Join(", ", BundleAssets));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL " + error);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildBundleTo(string folder)
    {
        Directory.CreateDirectory(folder);
        var build = new AssetBundleBuild { assetBundleName = "kaiju.bundle", assetNames = BundleAssets };
        if (BuildPipeline.BuildAssetBundles(folder, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) == null) throw new Exception("kaiju.bundle build failed");
    }

    // ---------------------------------------------------------------- poses

    private static readonly string[] Spine = { "Spine1_M", "Spine2_M", "Chest_M" };
    private static readonly string[] Neck = { "Neck0_M", "Neck1_M", "Neck2_M", "Neck3_M", "Neck4_M", "Neck5_M" };
    private static readonly string[] Tail = { "Tail0_M", "Tail1_M", "Tail2_M", "Tail3_M", "Tail4_M", "Tail5_M", "joint1_M", "joint2_M", "joint3_M" };
    private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };

    private static float Sin(float cycles) => Mathf.Sin(cycles * 2f * Mathf.PI);
    private static float Cos(float cycles) => Mathf.Cos(cycles * 2f * Mathf.PI);
    private static float Ramp(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
    private static float Env(float t, float a, float b, float c, float d) => Ramp(t, a, b) * (1f - Ramp(t, c, d));

    private static void Chain(Pose p, string[] bones, float pitch, float yaw = 0f, float roll = 0f)
    {
        foreach (string bone in bones) p.R(bone, pitch / bones.Length, yaw / bones.Length, roll / bones.Length);
    }

    /// <summary>Tail wave: yaw travels down the tail, growing towards the tip.</summary>
    private static void TailWave(Pose p, float phase, float amplitude, float lift = 0f)
    {
        for (int i = 0; i < Tail.Length; i++)
            p.R(Tail[i], lift / Tail.Length, Sin(phase - i * 0.08f) * amplitude * (0.4f + i * 0.15f));
    }

    private static void Claws(Pose p, int side, float spread)
    {
        for (int i = 0; i < Fingers.Length; i++)
        {
            float fan = (i - 1.5f) * 6f * spread;
            for (int j = 1; j <= 3; j++) p.Side(Fingers[i] + "Finger" + j, side, -12f * spread, fan / j);
        }
    }

    private static void Breathe(Pose p, float t, float depth = 1f)
    {
        float b = Sin(t / 4f) * depth;
        Chain(p, Spine, -1.5f * b);
        p.Side("Shoulder", 1, 0f, 0f, 1.2f * b);
        p.Side("Shoulder", -1, 0f, 0f, 1.2f * b);
        p.R("Jaw_M", 2f + 1.5f * b);
    }

    private static void Idle(Pose p, float t)
    {
        Breathe(p, t);
        Chain(p, Neck, 3f * Sin(t / 5.3f), 9f * Sin(t / 8f));
        p.R("Head_M", 2f * Sin(t / 4f + 0.2f), 4f * Sin(t / 8f - 0.1f));
        TailWave(p, t / 8f, 3f);
        p.Move(new Vector3(0f, -0.15f * (1f + Sin(t / 4f)), 0f));
    }

    /// <summary>Lateral-sequence walk; one full cycle per clip. Swing 35 %, stance 65 %.</summary>
    private static void Walk(Pose p, float t)
    {
        float ph = t / 6f;
        Leg(p, ph, 0.00f, -1, false);
        Leg(p, ph, 0.25f, -1, true);
        Leg(p, ph, 0.50f, 1, false);
        Leg(p, ph, 0.75f, 1, true);
        p.Move(new Vector3(0.25f * Sin(ph), -0.6f * (1f + Cos(2f * ph)) * 0.5f, 0f));
        p.R("Root_M", 1.2f * Sin(2f * ph), 2f * Sin(ph), 2.5f * Sin(ph));
        Chain(p, Spine, 1f * Cos(2f * ph), -3f * Sin(ph), -1.5f * Sin(ph));
        Chain(p, Neck, -2f * Cos(2f * ph + 0.1f), -2f * Sin(ph));
        p.R("Head_M", 0f, 3f * Sin(ph - 0.1f));
        p.R("Jaw_M", 3f);
        TailWave(p, ph, 5f);
    }

    private static void Leg(Pose p, float ph, float offset, int side, bool front)
    {
        float lp = Mathf.Repeat(ph + offset, 1f);
        bool swing = lp < 0.35f;
        float s = swing ? lp / 0.35f : (lp - 0.35f) / 0.65f;
        float a = front ? 16f : 14f;
        float ang = swing ? a * Mathf.Cos(Mathf.PI * s) : -a + 2f * a * s;
        float lift = swing ? Mathf.Sin(Mathf.PI * s) : 0f;
        if (front)
        {
            p.Side("Shoulder", side, ang);
            p.Side("Elbow", side, -28f * lift);
            p.Side("Wrist", side, 30f * lift - 0.6f * ang);
            Claws(p, side, 0.6f * lift);
        }
        else
        {
            p.Side("Hip", side, ang);
            p.Side("Knee", side, 34f * lift);
            p.Side("Ankle", side, -22f * lift + 0.4f * ang);
        }
    }

    private static void Roar(Pose p, float t)
    {
        float e = Env(t, 0.2f, 1.0f, 3.2f, 4.4f);
        float shake = Env(t, 1.0f, 1.3f, 3.0f, 3.3f) * Sin(t * 6f);
        Breathe(p, t, 0.5f);
        p.Move(new Vector3(0f, 1.2f * e, -1f * e));
        p.R("Root_M", -7f * e);
        Chain(p, Spine, -9f * e);
        Chain(p, Neck, -38f * e, 4f * shake);
        p.R("Head_M", -14f * e, 3f * shake);
        p.R("Jaw_M", 42f * e);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Side("Shoulder", side, -12f * e, 10f * e, 14f * e);
            p.Side("Elbow", side, -10f * e);
            Claws(p, side, e);
        }
        TailWave(p, t / 3f, 4f, -12f * e);
    }

    private static void LaserCharge(Pose p, float t)
    {
        float draw = Env(t, 0.0f, 1.2f, 1.6f, 2.6f);
        float aim = Ramp(t, 1.6f, 2.8f);
        float tremble = Ramp(t, 0.6f, 2.0f) * Sin(t * 8f);
        p.Move(new Vector3(0f, -1.8f * (draw + aim), 0.5f * draw));
        Chain(p, Spine, 6f * draw + 4f * aim);
        Chain(p, Neck, 18f * draw - 6f * aim, 0.6f * tremble);
        p.R("Head_M", -10f * draw + 4f * aim);
        p.R("Jaw_M", 12f * draw + 34f * aim);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Side("Shoulder", side, -6f * aim, 6f * aim, 10f * (draw + aim) * 0.5f);
            p.Side("Elbow", side, 10f * aim);
            Claws(p, side, aim);
        }
        TailWave(p, t / 4f, 2f, -14f * (draw + aim) * 0.5f);
    }

    private static void LaserFire(Pose p, float t)
    {
        float tremble = Sin(t * 5f);
        p.Move(new Vector3(0f, -1.8f + 0.1f * tremble, 0f));
        Chain(p, Spine, 4f);
        Chain(p, Neck, -6f, 0.5f * tremble);
        p.R("Head_M", 4f, 0f, 0.4f * tremble);
        p.R("Jaw_M", 48f + 2f * Sin(t * 3f));
        for (int side = -1; side <= 1; side += 2)
        {
            p.Side("Shoulder", side, -6f, 6f, 6f);
            p.Side("Elbow", side, 10f);
            Claws(p, side, 1f);
        }
        TailWave(p, t / 2f, 2f, -14f);
    }

    /// <summary>Crouch, snap the back up and fling the dorsal spikes at ~1.55 s (KaijuAnim.SpikeRelease).</summary>
    private static void Spike(Pose p, float t)
    {
        float crouch = Env(t, 0.0f, 1.2f, 1.25f, 1.55f);
        float snap = Env(t, 1.25f, 1.55f, 1.9f, 3.1f);
        p.Move(new Vector3(0f, -2.5f * crouch + 1.2f * snap, 0f));
        p.R("Root_M", 4f * crouch - 6f * snap);
        Chain(p, Spine, 12f * crouch - 16f * snap);
        Chain(p, Neck, 14f * crouch - 26f * snap, 3f * snap * Sin(t * 5f));
        p.R("Head_M", 6f * crouch - 8f * snap);
        p.R("Jaw_M", 6f * crouch + 30f * snap);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Side("Shoulder", side, 4f * crouch, 0f, 8f * snap);
            p.Side("Elbow", side, 18f * crouch);
            p.Side("Hip", side, 0f, 0f, 0f);
            p.Side("Knee", side, 10f * crouch);
            Claws(p, side, snap);
        }
        TailWave(p, t / 3f, 3f + 5f * snap, 8f * crouch - 18f * snap);
    }

    /// <summary>Rear up, raise the arm (side +1 = right), rake it across and down; hit window 0.95-1.35 s.</summary>
    private static void Swipe(Pose p, float t, int side)
    {
        float wind = Env(t, 0.0f, 0.85f, 0.95f, 1.3f);
        float strike = Env(t, 0.95f, 1.3f, 1.5f, 2.5f);
        float rear = Env(t, 0.0f, 0.8f, 1.4f, 2.5f);
        p.Move(new Vector3(0f, 3f * rear, -1.5f * rear));
        p.R("Root_M", -10f * rear, 6f * side * wind - 10f * side * strike, -3f * side * wind);
        Chain(p, Spine, -8f * rear, 8f * side * wind - 14f * side * strike, 4f * side * wind);
        Chain(p, Neck, -6f * rear, -10f * side * strike);
        p.R("Jaw_M", 10f + 22f * strike);
        p.Side("Shoulder", side, -95f * wind - 20f * strike, 25f * wind - 35f * strike, 20f * wind);
        p.Side("Elbow", side, -40f * wind + 10f * strike);
        p.Side("Wrist", side, -15f * wind + 25f * strike);
        Claws(p, side, wind + strike);
        p.Side("Shoulder", -side, 8f * rear, 0f, 6f * rear);
        p.Side("Hip", 1, -6f * rear);
        p.Side("Hip", -1, -6f * rear);
        TailWave(p, t / 2.6f, 4f, 10f * rear);
    }

    private static void Flinch(Pose p, float t)
    {
        float hit = Env(t, 0.0f, 0.12f, 0.25f, 1.15f);
        p.Move(new Vector3(0f, -0.8f * hit, -0.8f * hit));
        Chain(p, Spine, 4f * hit, 0f, 5f * hit);
        Chain(p, Neck, -14f * hit, 10f * hit * Cos(t * 3f));
        p.R("Head_M", -6f * hit, 6f * hit, 10f * hit);
        p.R("Jaw_M", 26f * hit);
        TailWave(p, t, 8f * hit);
    }

    /// <summary>Death throes, front legs buckle, chest and head hit the ground, back legs fold. Holds the last frame.</summary>
    private static void Death(Pose p, float t)
    {
        float throe = Env(t, 0.0f, 0.6f, 1.2f, 2.0f);
        float fall = Ramp(t, 1.4f, 3.4f);
        float settle = Ramp(t, 3.0f, 5.6f);
        p.Move(new Vector3(0f, 1.5f * throe - 14f * fall - 6f * settle, 2f * fall));
        p.R("Root_M", -8f * throe + 14f * fall - 4f * settle, 0f, 6f * fall);
        Chain(p, Spine, 10f * fall, 0f, 4f * fall);
        Chain(p, Neck, -34f * throe + 46f * fall, 8f * throe * Sin(t * 4f), -14f * settle);
        p.R("Head_M", -10f * throe + 18f * fall, 0f, -10f * settle);
        p.R("Jaw_M", 40f * throe + 10f * fall + 14f * settle);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Side("Shoulder", side, -20f * fall, 15f * fall, 20f * fall);
            p.Side("Elbow", side, 55f * fall);
            p.Side("Wrist", side, -30f * fall);
            p.Side("Hip", side, -20f * settle, 0f, 10f * settle);
            p.Side("Knee", side, 40f * settle);
            Claws(p, side, 1f - fall);
        }
        TailWave(p, t / 6f, 6f * (1f - settle), 6f * settle);
    }

    // ---------------------------------------------------------------- generation

    private static AnimatorController Controller(Pose pose)
    {
        string path = Gen + "/Kaiju.controller";
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (var c in Clips)
        {
            AnimationClip clip = Record(pose, c.name, c.length, c.loop, c.pose);
            string clipPath = Gen + "/Kaiju_" + c.name + ".anim";
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);
            AnimatorState state = machine.AddState(c.name);
            state.motion = clip;
            if (c.name == "Idle") machine.defaultState = state;
        }
        return controller;
    }

    private static AnimationClip Record(Pose pose, string name, float length, bool loop, Action<Pose, float> fn)
    {
        var touched = new HashSet<Transform>();
        for (int i = 0; i <= 48; i++)
        {
            pose.Clear();
            fn(pose, length * i / 48f);
            touched.UnionWith(pose.Touched);
        }
        Transform rootBone = pose.Bone("Root_M");
        touched.Add(rootBone);
        var rot = new Dictionary<Transform, AnimationCurve[]>();
        foreach (Transform t in touched) rot[t] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var pos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var last = new Dictionary<Transform, Quaternion>();
        int frames = Mathf.CeilToInt(length * Fps);
        for (int f = 0; f <= frames; f++)
        {
            float time = Mathf.Min(f / Fps, length);
            pose.Clear();
            fn(pose, time);
            pose.Apply();
            foreach (var kv in rot)
            {
                Quaternion q = kv.Key.localRotation;
                if (last.TryGetValue(kv.Key, out Quaternion prev) && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                last[kv.Key] = q;
                kv.Value[0].AddKey(time, q.x);
                kv.Value[1].AddKey(time, q.y);
                kv.Value[2].AddKey(time, q.z);
                kv.Value[3].AddKey(time, q.w);
            }
            Vector3 lp = rootBone.localPosition;
            pos[0].AddKey(time, lp.x);
            pos[1].AddKey(time, lp.y);
            pos[2].AddKey(time, lp.z);
        }
        pose.Clear();
        pose.Apply();
        var clip = new AnimationClip { name = "Kaiju_" + name, frameRate = Fps };
        string[] axes = { "x", "y", "z", "w" };
        foreach (var kv in rot)
        {
            string path = AnimationUtility.CalculateTransformPath(kv.Key, pose.Root);
            for (int i = 0; i < 4; i++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axes[i]), Smooth(kv.Value[i]));
        }
        string rootPath = AnimationUtility.CalculateTransformPath(rootBone, pose.Root);
        for (int i = 0; i < 3; i++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(rootPath, typeof(Transform), "m_LocalPosition." + axes[i]), Smooth(pos[i]));
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static AnimationCurve Smooth(AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    private static void Model(string path, bool rig)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.animationType = rig ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        if (rig)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
        }
        importer.SaveAndReimport();
    }

    private static void Texture(string path, bool normal, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "/" + name + ".png");

    private static Avatar AvatarOf(string fbx)
    {
        foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (o is Avatar avatar) return avatar;
        return null;
    }

    private static Material Mat(string name, Color color, float smoothness, Texture2D albedo = null, Texture2D normal = null,
        Texture2D emissionMap = null, Color? emission = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        string path = Gen + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        m.SetTexture("_BaseMap", albedo);
        m.SetTexture("_BumpMap", normal);
        if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        if (emission.HasValue)
        {
            m.SetColor("_EmissionColor", emission.Value);
            m.SetTexture("_EmissionMap", emissionMap);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        return m;
    }

    private static GameObject Prop(string name, string fbx, Material material, Quaternion rotation)
    {
        var root = new GameObject(name);
        GameObject mesh = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), root.transform, false);
        mesh.name = "Mesh";
        mesh.transform.localRotation = rotation;
        foreach (Renderer r in mesh.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
        return root;
    }

    private static void Save(GameObject go, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(go, Gen + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(go);
    }

    /// <summary>Bind pose plus root-space offsets; see the class summary.</summary>
    public sealed class Pose
    {
        public readonly Transform Root;
        private readonly List<Transform> order = new List<Transform>();
        private readonly Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, Quaternion> bindRot = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> bindPos = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> offset = new Dictionary<Transform, Vector3>();
        private Vector3 move;

        public Pose(Transform root)
        {
            Root = root;
            Collect(root);
        }

        public IEnumerable<Transform> Touched => offset.Keys;
        public Transform Bone(string name) => byName[name];

        public void Clear()
        {
            offset.Clear();
            move = Vector3.zero;
        }

        public void R(string bone, float pitch, float yaw = 0f, float roll = 0f)
        {
            Transform t = byName[bone];
            offset.TryGetValue(t, out Vector3 v);
            offset[t] = v + new Vector3(pitch, yaw, roll);
        }

        /// <summary>Limb bone by side (+1 right, -1 left); yaw and roll mirror.</summary>
        public void Side(string bone, int side, float pitch, float yaw = 0f, float roll = 0f) =>
            R(bone + (side > 0 ? "_R" : "_L"), pitch, yaw * side, roll * side);

        /// <summary>Moves the whole body (Root_M) in metres, root space.</summary>
        public void Move(Vector3 metres) => move += metres;

        public void Apply()
        {
            foreach (Transform t in order)
            {
                t.localRotation = bindRot[t];
                t.localPosition = bindPos[t];
            }
            Transform rootBone = byName["Root_M"];
            rootBone.position += Root.TransformVector(move);
            foreach (Transform t in order)
                if (offset.TryGetValue(t, out Vector3 e))
                    t.rotation = Root.rotation * Quaternion.Euler(e) * Quaternion.Inverse(Root.rotation) * t.rotation;
        }

        private void Collect(Transform t)
        {
            if (t != Root && t.GetComponent<Renderer>() == null)
            {
                order.Add(t);
                byName[t.name] = t;
                bindRot[t] = t.localRotation;
                bindPos[t] = t.localPosition;
            }
            foreach (Transform child in t) Collect(child);
        }
    }
}
