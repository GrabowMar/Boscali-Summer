using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Local, fading volcanic grade. A separate volume leaves weather, native exposure and night vision in control.</summary>
    internal static class HellAtmosphere
    {
        private static GameObject holder;
        private static Volume volume;
        private static VolumeProfile profile;
        private static float weight;

        internal static void Frame()
        {
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            float target = HellDimension.Inside ? 1f : 0f;
            if (camera != null && !HellDimension.Inside)
            {
                Vector3 at = camera.transform.position;
                foreach (HellTerritory.Zone zone in HellTerritory.Zones)
                {
                    if (!zone.Controlled) continue;
                    Vector3 delta = at - zone.Center.ToLocalPosition();
                    target = Mathf.Max(target, Domain.HellRules.CorruptionWeight(new Vector2(delta.x, delta.z).magnitude, delta.y, zone.Radius) * .8f);
                }
            }
            weight = Mathf.MoveTowards(weight, target, Time.unscaledDeltaTime * .5f);
            if (weight <= .001f && holder == null) return;
            if (holder == null)
            {
                Volume native = LevelInfo.i != null ? LevelInfo.i.PostProcessing : null;
                if (native == null) return;
                holder = new GameObject("Hell Volcanic Atmosphere");
                if (Datum.origin != null) holder.transform.SetParent(Datum.origin, false);
                holder.layer = native.gameObject.layer;
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.DontSave;
                // Split toning precedes native global saturation, preserving the game's G-LOC and exposure behavior.
                var tone = profile.Add<SplitToning>();
                tone.shadows.Override(new Color(.47f, .435f, .48f));
                tone.highlights.Override(new Color(.57f, .465f, .43f));
                tone.balance.Override(-8f);
                volume = holder.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = .9f; // below the game's night vision; postExposure is never overridden
                volume.sharedProfile = profile;
            }
            volume.weight = weight;
        }

        internal static void Clear()
        {
            if (holder != null) Object.Destroy(holder);
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components) if (component != null) Object.Destroy(component);
                Object.Destroy(profile);
            }
            holder = null;
            volume = null;
            profile = null;
            weight = 0f;
        }
    }
}
