using System.Collections.Generic;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Vanilla-style names and icons for every VANGUARD store, written at runtime because the bundle cannot be rebuilt. Names follow
    /// the game's own pattern ("AAM-29 Scythe": designation + title-case name; short name = designation; mounts add " xN"). Icons
    /// are the embedded <c>Assets/Models/*_Icon.png</c>: thin white lines on black, side view, nose left, 512x256 like
    /// <c>weaponIcon_AAM1</c>. Display only, the same on every peer; jsonKeys never change. Run from <see cref="VanguardPricing"/>.
    /// </summary>
    internal static class VanguardLabels
    {
        private struct Label
        {
            public string Name, Short, Icon;
            public Label(string name, string shortName, string icon) { Name = name; Short = shortName; Icon = icon; }
        }

        // WeaponInfo asset name -> label. Icon null keeps the baked sprite.
        private static readonly Dictionary<string, Label> Labels = new Dictionary<string, Label>
        {
            ["WI_VG_MaldX"] = new Label("ADM-160X Bait", "ADM-160X", "MaldX"),
            ["WI_VG_MaldJ"] = new Label("ADM-160J Shadow", "ADM-160J", "MaldJ"),
            ["WI_VG_Remora"] = new Label("XQ-58V Remora", "XQ-58V", "Remora"),
            ["WI_VG_RemoraGun"] = new Label("20mm Drone Cannon", "GUN 20MM", null),
            ["WI_VG_Manta"] = new Label("XQ-90 Manta", "XQ-90", "Manta"),
            ["WI_VG_HawcX"] = new Label("HGV-20 Hawc", "HGV-20", "HawcX"),
            ["WI_VG_AegisDart"] = new Label("AIM-14 Aegis", "AIM-14", "AegisPod"),
            ["WI_VG_Lance"] = new Label("RG-12 Lance Railgun", "RG-12", "Lance"),
            ["WI_VG_Glaive2A"] = new Label("GLV-2A Sentinel", "GLV-2A", "Glaive2A"),
            ["WI_VG_Glaive2S"] = new Label("GLV-2S Spectre", "GLV-2S", "Glaive2S"),
            ["WI_VG_GlaiveCannon_A"] = new Label("20mm Turret Cannon", "GUN 20MM TUR", "Glaive2A"),
            ["WI_VG_GlaiveCannon_S"] = new Label("30mm HE Cannon", "GUN 30MM HE", "Glaive2S"),
            ["WI_VG_Orca"] = new Label("AGT-80 Orca", "AGT-80", "Orca"),
            ["WI_VG_AleX"] = new Label("ALE-50 Towed Decoy", "TOWED DECOY", "AleXPod"),
            ["WI_VG_Kestrel"] = new Label("KS-01 Kestrel", "KS-01", "Kestrel"),
            ["WI_VG_Hamilcar"] = new Label("CG-9 Hamilcar", "CG-9", "Hamilcar"),
            ["WI_VG_Skywell"] = new Label("Skywell Service Kit", "SERVICE KIT", "SkywellKit"),
            ["WI_VG_Torch"] = new Label("SRB-2 Torch", "SRB-2", "Torch"),
            ["WI_VG_Sunspear"] = new Label("HEL-60 Sunspear", "LASER 60KW", "Sunspear"),
        };

        private static readonly Regex Count = new Regex(@" x\d+$");
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        /// <summary>The name the MALD-X definition should carry; <see cref="VanguardPricing"/> probes it to spot Blueprinter's restore.</summary>
        public static string MaldXName => Labels["WI_VG_MaldX"].Name;

        public static void Apply(Encyclopedia e)
        {
            foreach (MissileDefinition def in e.missiles)
            {
                WeaponInfo info = def != null && def.unitPrefab != null && def.unitPrefab.TryGetComponent(out Missile missile) ? missile.GetWeaponInfo() : null;
                if (info != null && Write(info)) def.unitName = info.weaponName;
            }
            foreach (WeaponMount mount in e.weaponMounts)
            {
                if (mount == null || mount.info == null || !Write(mount.info)) continue;
                Match count = Count.Match(mount.mountName ?? "");
                mount.mountName = mount.info.weaponName + (count.Success ? count.Value : "");
            }
            foreach (WeaponInfo info in Resources.FindObjectsOfTypeAll<WeaponInfo>()) Write(info); // the drone guns live on prefabs only
        }

        private static bool Write(WeaponInfo info)
        {
            if (!Labels.TryGetValue(info.name, out Label label)) return false;
            info.weaponName = label.Name;
            info.shortName = label.Short;
            Sprite icon = label.Icon != null ? Icon(label.Icon) : null;
            if (icon != null) info.weaponIcon = icon;
            return true;
        }

        private static Sprite Icon(string name)
        {
            if (Sprites.TryGetValue(name, out Sprite cached)) return cached;
            Sprite sprite = null;
            byte[] png = EmbeddedResources.ReadAll(typeof(VanguardLabels).Assembly, "BoscaliSummer.Vanguard.Icons." + name + "_Icon.png", 1024 * 1024);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "weaponIcon_VG_" + name, wrapMode = TextureWrapMode.Clamp };
            if (png != null && texture.LoadImage(png, false)) // readable: the HUD stores board bakes its mask from the pixels
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = texture.name;
            }
            else Debug.LogWarning("[Vanguard] icon " + name + " is not embedded; the store keeps its baked icon");
            Sprites[name] = sprite;
            return sprite;
        }
    }
}
