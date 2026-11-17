using System;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Scales the cloned vanilla soot spawner; its native Start/EffectManager still own projection and lifetime.</summary>
    internal static class Sa74NativeGround
    {
        private static readonly AccessTools.FieldRef<Shockwave, float> Yield = AccessTools.FieldRefAccess<Shockwave, float>("yieldKilotons");
        private static readonly AccessTools.FieldRef<DecalSpawner, float> Size = AccessTools.FieldRefAccess<DecalSpawner, float>("decalSize");
        private static readonly AccessTools.FieldRef<DecalSpawner, Material> MaterialRef = AccessTools.FieldRefAccess<DecalSpawner, Material>("decalMaterial");
        internal static int ConfiguredCount { get; private set; }
        internal static float ConfiguredDiameter { get; private set; }
        internal static float SourceYieldKilotons { get; private set; }

        /// <summary>Call immediately after Instantiate, before SetOwner changes the native reference yield.</summary>
        internal static void Prepare(GameObject blast, float targetKilotons)
        {
            if (blast == null || targetKilotons <= 0f) return;
            Shockwave source = blast.GetComponentInChildren<Shockwave>(true);
            float originalKilotons = source != null ? Yield(source) : 0f;
            if (originalKilotons <= 0f) return;
            float scale = Mathf.Pow(targetKilotons / originalKilotons, 1f / 3f);
            foreach (DecalSpawner spawner in blast.GetComponentsInChildren<DecalSpawner>(true))
            {
                Material material = MaterialRef(spawner);
                // The shipped 250 kt effect carries decalSize=6000 and the native "scorchmark" material.
                if (material == null || material.name.IndexOf("scorch", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Size(spawner) *= scale;
                ConfiguredCount++;
                ConfiguredDiameter = Mathf.Max(ConfiguredDiameter, Size(spawner));
                SourceYieldKilotons = originalKilotons;
            }
        }

        internal static void Reset()
        {
            ConfiguredCount = 0;
            ConfiguredDiameter = SourceYieldKilotons = 0f;
        }
    }
}
