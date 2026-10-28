using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Writes <see cref="VanguardPrices"/> onto the loaded bundle's assets on every peer (the loadout menu, the host's charges
    /// and the kill rewards all read them), so a price change needs no bundle rebuild. Blueprinter applies its bundle patches
    /// while the first GameWorld loads, after sceneLoaded, which puts the baked values back: a 1 Hz probe on the MALD-X
    /// definition re-applies whenever it drifts (sim 2026-10-25, vg-economy). Idempotent. Carries <see cref="VanguardLabels"/> too.
    /// </summary>
    internal sealed class VanguardPricing : MonoBehaviour, ISceneService
    {
        private float nextProbe;

        public void ResetForScene()
        {
            nextProbe = 0f;
            Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextProbe) return;
            nextProbe = Time.unscaledTime + 1f;
            if (Drifted()) Apply();
        }

        private int labelledMounts;

        // Also re-applies once the runtime stores have joined the mount list, so they get their icons.
        private bool Drifted()
        {
            if (Encyclopedia.i == null) return false;
            if (Encyclopedia.i.weaponMounts.Count != labelledMounts) return true;
            foreach (MissileDefinition def in Encyclopedia.i.missiles)
                if (def != null && def.jsonKey == VanguardKeys.MaldX)
                    return def.value != VanguardPrices.Munition(VanguardKeys.MaldX) || def.unitName != VanguardLabels.MaldXName;
            return false;
        }

        private void Apply()
        {
            if (Encyclopedia.i == null) return;
            VanguardLabels.Apply(Encyclopedia.i);
            labelledMounts = Encyclopedia.i.weaponMounts.Count;
            foreach (MissileDefinition def in Encyclopedia.i.missiles)
            {
                float price = def != null ? VanguardPrices.Munition(def.jsonKey) : 0f;
                if (price <= 0f) continue;
                def.value = price;
                WeaponInfo info = def.unitPrefab != null && def.unitPrefab.TryGetComponent(out Missile missile) ? missile.GetWeaponInfo() : null;
                if (info != null) info.costPerRound = price;
            }
            foreach (WeaponInfo info in Resources.FindObjectsOfTypeAll<WeaponInfo>())
            {
                float price = VanguardPrices.Gun(info.name);
                if (price > 0f) info.costPerRound = price;
            }
        }
    }
}
