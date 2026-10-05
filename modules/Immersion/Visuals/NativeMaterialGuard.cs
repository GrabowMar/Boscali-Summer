using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // Binding-time only: UnitPart reacquires renderer.material for damage/livery.
    // Membership is authoritative even on legacy shaders without _HitPoints/_Livery.
    internal static class NativeMaterialGuard
    {
        private static readonly FieldInfo DamageField = typeof(UnitPart).GetField("damageMaterial",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool CanBindRenderer(Aircraft aircraft, Renderer renderer)
        {
            if (aircraft == null || renderer == null || DamageField == null ||
                DamageField.FieldType != typeof(DamageMaterial)) return false;
            List<UnitPart> parts = aircraft.partLookup;
            if (parts == null || parts.Count == 0) return false;
            bool hasLivePart = false;
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart part = parts[i];
                if (part == null) continue;
                hasLivePart = true;
                DamageMaterial damage = DamageField.GetValue(part) as DamageMaterial;
                if (damage == null) continue;
                List<Renderer> controlled = damage.renderers;
                if (controlled == null) return false;
                for (int r = 0; r < controlled.Count; r++)
                    if (controlled[r] == renderer) return false;
            }
            return hasLivePart;
        }
    }
}
