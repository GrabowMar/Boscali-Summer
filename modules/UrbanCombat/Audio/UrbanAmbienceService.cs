using BoscaliSummer.Features.UrbanCombat.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Features.UrbanCombat.Audio
{
    /// <summary>
    /// Client-local air-raid siren near cities. No network, no world mutation. One 3D
    /// voice parked over the nearest airbase within siren range, so the camera hears it
    /// like any other positional sound; hysteresis keeps the voice from hopping between
    /// cities. Ceilings: 1 source/clip, 64 airbases, 0.5 s tick, 30 s catalogue.
    /// Headless skips all audio.
    /// </summary>
    internal sealed class UrbanAmbienceService : MonoBehaviour, ISceneService
    {
        private const float TickInterval = 0.5f;
        private const float CatalogueRefresh = 30f;
        private const int MaxAirbases = 64;

        private UrbanAmbienceAudio audio;
        private Airbase[] airbases;
        private Airbase anchor;
        private float anchorDist;
        private float nextTick;
        private float nextCatalogue;
        private float sirenTarget;
        private bool audioRouted;

        public void ResetForScene()
        {
            nextTick = 0f;
            nextCatalogue = 0f;
            airbases = null;
            anchor = null;
            anchorDist = 0f;
            sirenTarget = 0f;
            audioRouted = false;
            if (audio != null) audio.Silence();
        }

        private void Awake()
        {
            if (Application.isBatchMode) return;
            audio = gameObject.AddComponent<UrbanAmbienceAudio>();
            audio.Initialize();
        }

        private void OnDestroy()
        {
            if (audio != null) Destroy(audio);
            audio = null;
        }

        private void Update()
        {
            if (audio == null) return;
            if (Application.isBatchMode)
            {
                sirenTarget = 0f;
                audio.UpdateAudio(0f, Vector3.zero, false);
                return;
            }
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + TickInterval;
                Evaluate();
            }
            if (!audioRouted) TryRouteAudio();
            bool hasAnchor = anchor != null;
            Vector3 anchorPos = hasAnchor ? AnchorPosition(anchor) : Vector3.zero;
            audio.UpdateAudio(hasAnchor ? sirenTarget : 0f, anchorPos, hasAnchor);
        }

        private void Evaluate()
        {
            UrbanCombatSettings urban = Plugin.Settings != null ? Plugin.Settings.UrbanCombat : null;
            if (urban == null || !urban.AmbienceEnabled.Value)
            {
                anchor = null;
                sirenTarget = 0f;
                return;
            }
            float master = urban.AmbienceVolume.Value;
            if (!TryGetListenerPosition(out Vector3 listenerPos))
            {
                anchor = null;
                sirenTarget = 0f;
                return;
            }
            RefreshCatalogue();
            if (airbases == null || airbases.Length == 0)
            {
                anchor = null;
                sirenTarget = 0f;
                return;
            }
            if (anchor != null)
            {
                anchorDist = (listenerPos - AnchorPosition(anchor)).magnitude;
                if (anchorDist > UrbanAmbienceMath.SirenReleaseMeters) anchor = null;
            }
            Airbase nearest = null;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < airbases.Length; i++)
            {
                Airbase airbase = airbases[i];
                if (airbase == null) continue;
                float dist = (listenerPos - AnchorPosition(airbase)).magnitude;
                if (dist > UrbanAmbienceMath.SirenOuterMeters || dist >= nearestDist) continue;
                nearest = airbase;
                nearestDist = dist;
            }
            if (anchor == null)
            {
                anchor = nearest;
                anchorDist = nearestDist;
            }
            else if (nearest != null && nearest != anchor &&
                UrbanAmbienceMath.ShouldSwitchAnchor(anchorDist, nearestDist))
            {
                anchor = nearest;
                anchorDist = nearestDist;
            }
            sirenTarget = anchor != null ? UrbanAmbienceMath.SirenVolume(master) : 0f;
        }

        private static Vector3 AnchorPosition(Airbase airbase)
        {
            return airbase.center != null ? airbase.center.position : airbase.transform.position;
        }

        private static bool TryGetListenerPosition(out Vector3 pos)
        {
            Camera cam = null;
            try
            {
                CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
                cam = cameras != null ? cameras.mainCamera : null;
            }
            catch (System.Exception)
            {
            }
            if (cam == null)
            {
                try
                {
                    cam = Camera.main;
                }
                catch (System.Exception)
                {
                }
            }
            if (cam == null)
            {
                pos = Vector3.zero;
                return false;
            }
            pos = cam.transform.position;
            return true;
        }

        private void RefreshCatalogue()
        {
            if (Time.unscaledTime < nextCatalogue && airbases != null) return;
            nextCatalogue = Time.unscaledTime + CatalogueRefresh;
            try
            {
                if (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                {
                    var values = FactionRegistry.airbaseLookup.Values;
                    int n = values.Count < MaxAirbases ? values.Count : MaxAirbases;
                    var cache = new Airbase[n];
                    int i = 0;
                    foreach (Airbase airbase in values)
                    {
                        if (i >= n) break;
                        cache[i++] = airbase;
                    }
                    airbases = cache;
                }
                else
                {
                    Airbase[] found = UnityEngine.Object.FindObjectsOfType<Airbase>();
                    if (found != null && found.Length > MaxAirbases)
                    {
                        var cache = new Airbase[MaxAirbases];
                        System.Array.Copy(found, cache, MaxAirbases);
                        airbases = cache;
                    }
                    else airbases = found;
                }
            }
            catch (System.Exception)
            {
                airbases = null;
            }
        }

        private void TryRouteAudio()
        {
            if (audio == null || audioRouted) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            audio.SetOutputGroup(group);
            audioRouted = true;
        }
    }
}
