using System;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Turns an occupied civilian building into a heavy stronghold (de facto bunker).
    /// Couples the visual building shell with its interior defense proxy:
    /// - High hitpoints (default 2,500 HP) and heavy armor resistance.
    /// - Coupled damage: attacks against the building or proxy both damage the stronghold.
    /// - Driving progressive URP surface charring and thermal heat glow via BuildingDamageVisual.
    /// - Shared lifecycle: when the stronghold falls, the building collapses into ruins and cleans up.
    /// </summary>
    internal sealed class StrongholdBuilding : MonoBehaviour
    {
        public FactionHQ Owner { get; private set; }
        public Airbase Airbase { get; private set; }
        public Building DefenseProxy { get; private set; }
        public float MaxHitPoints { get; private set; }
        public float CurrentHitPoints { get; private set; }
        public float PierceArmor { get; private set; }
        public float BlastArmor { get; private set; }
        public bool IsDestroyed { get; private set; }

        private BuildingDamageVisual damageVisual;
        private MapBuilding mapBuilding;
        private Building networkBuilding;
        private bool isServer;

        public static StrongholdBuilding Get(GameObject go) =>
            go != null ? go.GetComponent<StrongholdBuilding>() : null;

        public void Initialize(
            FactionHQ owner,
            Airbase airbase,
            Building defenseProxy,
            float maxHp,
            float pierceArmor,
            float blastArmor)
        {
            Owner = owner;
            Airbase = airbase;
            DefenseProxy = defenseProxy;
            MaxHitPoints = Mathf.Max(100f, maxHp);
            CurrentHitPoints = MaxHitPoints;
            PierceArmor = pierceArmor;
            BlastArmor = blastArmor;
            IsDestroyed = false;

            mapBuilding = GetComponent<MapBuilding>();
            networkBuilding = GetComponent<Building>();
            damageVisual = BuildingDamageVisual.GetOrAdd(gameObject);

            try
            {
                isServer = NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active;
            }
            catch
            {
                isServer = false;
            }

            // Hook proxy destruction / damage coupling
            if (defenseProxy != null && defenseProxy.partLookup != null)
            {
                for (int i = 0; i < defenseProxy.partLookup.Count; i++)
                {
                    UnitPart part = defenseProxy.partLookup[i];
                    if (part != null)
                        part.onApplyDamage += OnProxyPartDamage;
                }
            }
        }

        private void OnProxyPartDamage(UnitPart.OnApplyDamage e)
        {
            if (IsDestroyed) return;
            float rawDamage = e.pierceDamage + e.blastDamage + e.fireDamage + e.impactDamage;
            if (rawDamage <= 0.001f) return;

            // Proxy damage directly damages the stronghold
            Vector3 hitPoint = DefenseProxy != null ? DefenseProxy.transform.position : (transform.position + Vector3.up * 4f);
            ApplyCalculatedDamage(rawDamage, hitPoint, Vector3.up, e.blastDamage * 0.1f);
        }

        public bool TakeDamage(
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float fireDamage,
            float impactDamage,
            PersistentID dealerID)
        {
            if (IsDestroyed) return false;

            float totalDamage = StrongholdDamagePolicy.CalculateMitigatedDamage(
                pierceDamage, blastDamage, amountAffected, fireDamage, impactDamage, PierceArmor, BlastArmor);

            Vector3 hitPoint = transform.position + Vector3.up * 4f;
            Vector3 normal = Vector3.forward;

            // Resolve exact hit surface from attacker trajectory
            if (dealerID.IsValid && dealerID.TryGetUnit(out Unit attacker) && attacker != null)
            {
                Vector3 attackerPos = attacker.transform.position;
                Vector3 toCenter = (transform.position + Vector3.up * 4f) - attackerPos;
                if (Physics.Raycast(attackerPos, toCenter.normalized, out RaycastHit hit, toCenter.magnitude + 20f, PhysicsLayers.StaticsMask))
                {
                    hitPoint = hit.point;
                    normal = hit.normal;
                }
                else
                {
                    normal = (-toCenter).normalized;
                }
            }

            return ApplyCalculatedDamage(totalDamage, hitPoint, normal, blastDamage * 0.1f);
        }

        private bool ApplyCalculatedDamage(float damage, Vector3 hitPoint, Vector3 normal, float blastYield)
        {
            if (IsDestroyed || damage <= 0.001f) return false;

            CurrentHitPoints -= damage;
            damageVisual?.ApplyLocalImpact(hitPoint, normal, blastYield, damage);

            if (CurrentHitPoints < MaxHitPoints * 0.5f)
            {
                damageVisual?.SetSevereDamageSmoke(true, hitPoint);
            }

            if (CurrentHitPoints <= 0f)
            {
                DestroyStronghold();
                return true;
            }
            return false;
        }

        public void DestroyStronghold()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            CurrentHitPoints = 0f;

            damageVisual?.SetSevereDamageSmoke(false, Vector3.zero);

            // Destroy defense proxy
            if (DefenseProxy != null)
            {
                if (isServer && NetworkManagerNuclearOption.i != null)
                {
                    try
                    {
                        NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(DefenseProxy.Identity, true);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger?.LogWarning($"Failed to destroy defense proxy for stronghold: {ex.Message}");
                    }
                }
                DefenseProxy = null;
            }

            GarrisonOccupancy.Clear(gameObject, Owner);

            // Trigger building destruction into ruins
            if (mapBuilding != null)
            {
                mapBuilding.Destruct();
            }
            else if (networkBuilding != null && !networkBuilding.disabled)
            {
                networkBuilding.UnitDisabled(false, true);
            }
        }

        private void OnDestroy()
        {
            if (DefenseProxy != null && DefenseProxy.partLookup != null)
            {
                for (int i = 0; i < DefenseProxy.partLookup.Count; i++)
                {
                    UnitPart part = DefenseProxy.partLookup[i];
                    if (part != null)
                        part.onApplyDamage -= OnProxyPartDamage;
                }
            }
        }
    }
}
