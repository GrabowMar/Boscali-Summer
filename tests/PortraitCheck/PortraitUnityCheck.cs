#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Checks the embedded portrait atlas and production loader/cache in an offline Unity project.</summary>
public static class PortraitUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Assembly Mod = Assembly.Load("BoscaliSummer");
    private static readonly Type Portrait = Mod.GetType("BoscaliSummer.Modules.Wing.Runtime.PilotPortrait", true);
    private static readonly Type Generator = Mod.GetType("BoscaliSummer.Modules.Wing.Domain.PilotPortraitGenerator", true);
    private static readonly Type Role = Mod.GetType("BoscaliSummer.Core.Contracts.PortraitRole", true);
    private static readonly Type SelectionType = Mod.GetType("BoscaliSummer.Modules.Wing.Domain.PortraitSelection", true);
    private static readonly Type Body = Mod.GetType("BoscaliSummer.Modules.Wing.Domain.PortraitBody", true);
    private static int checks;

    public static void Run() => Execute(false);
    public static void RunSqd() => Execute(true);

    // The existing SQD fixture calls this only in its scratch-project copy.
    public static Sprite Pilot() => Identity("M. Fontaine|DAYMAN", 0, 0);

    private static void Execute(bool sqd)
    {
        try
        {
            Verify();
            File.WriteAllText("portrait-result.txt", "PASS: " + checks +
                " embedded-atlas, sprite/cache, role fallback, preview reuse and reset assertions; offline Unity only.");
            if (sqd) PresentationUnityCheck.RunSqdOnly();
            else EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("portrait-result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Verify()
    {
        checks = 0;
        using (Stream resource = Mod.GetManifestResourceStream("WingCommand.PilotLayers.png"))
        {
            Check(resource != null && resource.Length > 0, "production DLL embeds portrait PNG");
            using (var hash = SHA256.Create())
                Check(BitConverter.ToString(hash.ComputeHash(resource)).Replace("-", "") ==
                    File.ReadAllText("portrait-atlas-sha256.txt").Trim(), "embedded PNG matches final source atlas");
        }

        Invoke(Portrait, "Reset");
        int width = (int)Generator.GetField("Width", All).GetRawConstantValue();
        int height = (int)Generator.GetField("Height", All).GetRawConstantValue();
        var sheet = new Texture2D(width * 4, height * 2, TextureFormat.RGBA32, false);
        int priorCount = CacheCount;
        for (int faction = 0; faction < 2; faction++)
        for (int role = 0; role < 4; role++)
        {
            Sprite sprite = Identity("M. Fontaine|DAYMAN", role, faction);
            Check(sprite != null, "role " + role + "/faction " + faction + " loads embedded atlas");
            Check(sprite.texture.width == width && sprite.texture.height == height,
                "production portrait has expected dimensions");
            Check(ReferenceEquals(sprite, Identity("M. Fontaine|DAYMAN", role, faction)),
                "repeated identity/role/faction borrows cached sprite");
            Color32[] pixels = Read(sprite.texture, width, height);
            Check(pixels[(height / 2) * width + width / 2].a > 0, "portrait contains rendered pixels");
            sheet.SetPixels32(role * width, (1 - faction) * height, width, height, pixels);
        }
        sheet.Apply(false, false);
        File.WriteAllBytes("portrait-runtime-roles.png", sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        Check(CacheCount == priorCount + 8, "eight role/faction uniforms retain separate cached selections");

        int accessories = (int)Generator.GetField("AccessoryCount", All).GetRawConstantValue();
        var gearSheet = new Texture2D(width * accessories, height * 4, TextureFormat.RGBA32, false);
        for (int body = 0; body < 2; body++)
        for (int faction = 0; faction < 2; faction++)
        {
            Sprite bare = null;
            Color32[] barePixels = null;
            for (int accessory = 0; accessory < accessories; accessory++)
            {
                object look = Selection(body, body == 0 ? 0 : 4, body == 0 ? 1 : 5, faction == 0 ? 0 : 2, accessory);
                Sprite gear = (Sprite)Invoke(Portrait, "ForSelection", look);
                Check(gear != null && ReferenceEquals(gear, Invoke(Portrait, "ForSelection", look)),
                    "equipment selection borrows a stable distinct cached sprite");
                Color32[] pixels = Read(gear.texture, width, height);
                if (accessory == 0) { bare = gear; barePixels = pixels; }
                else Check(!ReferenceEquals(bare, gear) && !SamePixels(barePixels, pixels),
                    "equipment " + accessory + " visibly differs from the same bare identity");
                if (accessory == 5 || accessory == 6)
                {
                    Sprite otherHair = (Sprite)Invoke(Portrait, "ForSelection",
                        Selection(body, body == 0 ? 0 : 4, 8, faction == 0 ? 0 : 2, accessory));
                    Check(SamePixels(pixels, Read(otherHair.texture, width, height)),
                        "helmet suppresses incompatible hair sprites");
                }
                gearSheet.SetPixels32(accessory * width, (3 - body * 2 - faction) * height, width, height, pixels);
            }
        }
        gearSheet.Apply(false, false);
        File.WriteAllBytes("portrait-runtime-gear.png", gearSheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(gearSheet);

        Type squad = Mod.GetType("BoscaliSummer.Modules.Wing.Runtime.WingSquad", true);
        Sprite pilot = Personnel(squad, 0, -1);
        Check(ReferenceEquals(pilot, Personnel(squad, 999, -1)), "unknown role falls back to pilot");
        Check(ReferenceEquals(pilot, Personnel(squad, -1, -1)), "negative role falls back to pilot");
        Check(ReferenceEquals(pilot, Personnel(squad, 0, 999)), "unknown faction falls back to identity faction");

        int beforePreview = CacheCount;
        object selection = Invoke(Generator, "Select", "M. Fontaine|DAYMAN", Enum.ToObject(Role, 0), 0);
        Sprite preview = (Sprite)Invoke(Portrait, "Preview", selection);
        Check(preview != null && preview.texture.isReadable, "studio preview creates readable portrait texture");
        Texture2D previewTexture = preview.texture;
        for (int role = 1; role < 4; role++)
        {
            selection = Invoke(Generator, "Select", "M. Fontaine|DAYMAN", Enum.ToObject(Role, role), 1);
            Sprite updated = (Sprite)Invoke(Portrait, "Preview", selection);
            Check(ReferenceEquals(preview, updated) && ReferenceEquals(previewTexture, updated.texture),
                "studio changes reuse one sprite and texture");
        }
        Check(CacheCount == beforePreview, "preview changes do not grow borrowed portrait cache");
        Color32[] previousGear = null;
        for (int accessory = 0; accessory < accessories; accessory++)
        {
            Sprite updated = (Sprite)Invoke(Portrait, "Preview", Selection(1, 4, 5, 0, accessory));
            Check(ReferenceEquals(preview, updated) && ReferenceEquals(previewTexture, updated.texture),
                "equipment studio preview reuses one sprite and texture");
            Color32[] pixels = updated.texture.GetPixels32();
            if (previousGear != null) Check(!SamePixels(previousGear, pixels), "equipment preview redraws visible gear");
            previousGear = pixels;
        }
        Check(CacheCount == beforePreview, "equipment previews do not grow borrowed portrait cache");
        int backgrounds = (int)Generator.GetField("BackdropCount", All).GetRawConstantValue();
        Color32[] previousBackground = null;
        for (int backdrop = 0; backdrop < backgrounds; backdrop++)
        {
            Sprite updated = (Sprite)Invoke(Portrait, "Preview", Selection(1, 4, 5, 0, 0, backdrop));
            Check(ReferenceEquals(preview, updated) && ReferenceEquals(previewTexture, updated.texture),
                "background studio preview reuses one sprite and texture");
            Color32[] pixels = updated.texture.GetPixels32();
            if (previousBackground != null) Check(!SamePixels(previousBackground, pixels), "background preview redraws its scene");
            previousBackground = pixels;
        }
        Check(CacheCount == beforePreview, "background previews do not grow borrowed portrait cache");

        int beforeSelectionKeys = KeyCount(squad, "selectionKeys");
        int faces = (int)Generator.GetField("FacesPerBody", All).GetRawConstantValue();
        int hairs = (int)Generator.GetField("HairCount", All).GetRawConstantValue();
        int uniforms = (int)Generator.GetField("UniformCount", All).GetRawConstantValue();
        // Exercise more looks than the saved-selection ceiling through the SQD preview service.
        for (int look = 0; look < 160; look++)
        {
            int rest = look / 2;
            int face = rest % faces; rest /= faces;
            int hair = rest % hairs; rest /= hairs;
            int uniform = rest % uniforms; rest /= uniforms;
            Sprite updated = (Sprite)Invoke(squad, "PortraitForSelection", look % 2,
                face, hair, uniform, 0, rest, true);
            Check(ReferenceEquals(preview, updated) && ReferenceEquals(previewTexture, updated.texture),
                "SQD preview look " + look + " reuses its sprite/texture past the saved-selection ceiling");
        }
        Check(CacheCount == beforePreview, "SQD preview browsing does not grow borrowed portrait cache");
        Check(KeyCount(squad, "selectionKeys") == beforeSelectionKeys,
            "SQD preview browsing does not consume saved-selection keys");

        Sprite retained = Identity("M. Fontaine|DAYMAN", 0, 0);
        int ceiling = (int)Portrait.GetField("MaxCachedPortraits", All).GetRawConstantValue();
        bool refused = false;
        for (int look = 0; look < 400; look++)
        {
            int rest = look / 2;
            int face = rest % faces; rest /= faces;
            int hair = rest % hairs; rest /= hairs;
            Sprite requested = (Sprite)Invoke(Portrait, "ForSelection", Selection(look % 2, face, hair, 0, rest % accessories));
            refused |= requested == null;
            Check(CacheCount <= ceiling, "portrait cache never exceeds its hard ceiling");
        }
        Check(refused && CacheCount == ceiling, "portrait cache refuses new selections when full");
        Check(ReferenceEquals(retained, Identity("M. Fontaine|DAYMAN", 0, 0)) && retained.texture != null,
            "cache overflow preserves borrowed sprites and textures");

        Invoke(Portrait, "Reset");
        Check(CacheCount == 0, "reset clears cached portraits");
        Check(Portrait.GetField("previewTexture", All).GetValue(null) == null &&
              Portrait.GetField("previewSprite", All).GetValue(null) == null &&
              Portrait.GetField("layers", All).GetValue(null) == null,
            "reset clears preview and atlas references");
        Check(Pilot() != null, "reset permits reloading embedded atlas");
    }

    private static int CacheCount => ((IDictionary)Portrait.GetField("portraits", All).GetValue(null)).Count;

    private static int KeyCount(Type type, string field)
    {
        object keys = type.GetField(field, All).GetValue(null);
        return (int)keys.GetType().GetProperty("Count").GetValue(keys);
    }

    private static Sprite Identity(string identity, int role, int faction) =>
        (Sprite)Invoke(Portrait, "ForIdentity", identity, Enum.ToObject(Role, role), faction);

    private static object Selection(int body, int face, int hair, int uniform, int accessory, int backdrop = 0) =>
        Activator.CreateInstance(SelectionType, Enum.ToObject(Body, body), face, hair, uniform, accessory, backdrop);

    private static bool SamePixels(Color32[] first, Color32[] second)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++)
            if (first[i].r != second[i].r || first[i].g != second[i].g || first[i].b != second[i].b || first[i].a != second[i].a)
                return false;
        return true;
    }

    private static Sprite Personnel(Type squad, int role, int faction) =>
        (Sprite)Invoke(squad, "PersonnelPortrait", "M. Fontaine", "DAYMAN", Enum.ToObject(Role, role), faction);

    private static object Invoke(Type type, string name, params object[] arguments)
    {
        foreach (MethodInfo method in type.GetMethods(All))
            if (method.Name == name && method.GetParameters().Length == arguments.Length)
                return method.Invoke(null, arguments);
        throw new MissingMethodException(type.FullName, name);
    }

    private static Color32[] Read(Texture source, int width, int height)
    {
        RenderTexture prior = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            copy.Apply(false, false);
            return copy.GetPixels32();
        }
        finally
        {
            RenderTexture.active = prior;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
