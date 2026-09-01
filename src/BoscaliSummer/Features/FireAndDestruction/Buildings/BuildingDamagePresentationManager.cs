using System.Collections.Generic;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal sealed class BuildingDamagePresentationManager : MonoBehaviour, ISceneService
    {
        private sealed class PendingTransition
        {
            public MapBuilding Building;
            public float Severity;
        }

        private const int MaximumPendingTransitions = 64;
        private const int MaximumTrackedBuildings = 256;
        private const int MaximumDamageDecals = 48;
        private const int TransitionsPerFrame = 2;

        public static BuildingDamagePresentationManager Instance { get; private set; }

        private readonly Queue<int> transitionOrder = new Queue<int>(MaximumPendingTransitions);
        private readonly Dictionary<int, PendingTransition> pendingTransitions =
            new Dictionary<int, PendingTransition>(MaximumPendingTransitions);
        private readonly List<BuildingDamageVisual> trackedVisuals =
            new List<BuildingDamageVisual>(MaximumTrackedBuildings);
        private readonly Stack<GameObject> availableDecals = new Stack<GameObject>(MaximumDamageDecals);
        private readonly List<GameObject> allDecals = new List<GameObject>(MaximumDamageDecals);
        private float nextDecalSelection;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene() => Clear();

        internal void Enqueue(MapBuilding building, float severity)
        {
            if (building == null) return;
            severity = BuildingDamageVisual.QuantizeSeverity(severity);
            int id = building.GetInstanceID();
            if (pendingTransitions.TryGetValue(id, out PendingTransition pending))
            {
                if (severity > pending.Severity) pending.Severity = severity;
                return;
            }
            if (pendingTransitions.Count >= MaximumPendingTransitions) return;
            pendingTransitions.Add(id, new PendingTransition { Building = building, Severity = severity });
            transitionOrder.Enqueue(id);
        }

        private void Update()
        {
            int budget = TransitionsPerFrame;
            while (budget-- > 0 && transitionOrder.Count > 0)
            {
                int id = transitionOrder.Dequeue();
                if (!pendingTransitions.TryGetValue(id, out PendingTransition transition)) continue;
                pendingTransitions.Remove(id);
                if (transition.Building != null)
                    BuildingDamageVisual.ApplyImmediate(transition.Building, transition.Severity, this);
            }

            if (Time.unscaledTime < nextDecalSelection) return;
            nextDecalSelection = Time.unscaledTime + 0.5f;
            SelectNearestDamageDecals(Camera.main);
        }

        internal void Register(BuildingDamageVisual visual)
        {
            if (visual == null || trackedVisuals.Contains(visual) ||
                trackedVisuals.Count >= MaximumTrackedBuildings) return;
            trackedVisuals.Add(visual);
            nextDecalSelection = 0f;
        }

        internal void Unregister(BuildingDamageVisual visual)
        {
            if (visual == null) return;
            visual.ReleaseDamageDecals(this);
            trackedVisuals.Remove(visual);
        }

        internal GameObject AcquireDamageDecal()
        {
            while (availableDecals.Count > 0)
            {
                GameObject pooled = availableDecals.Pop();
                if (pooled != null)
                {
                    pooled.SetActive(true);
                    return pooled;
                }
            }
            if (allDecals.Count >= MaximumDamageDecals) return null;
            GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
            if (prefab == null) return null;
            GameObject decal = Object.Instantiate(prefab, Datum.origin, false);
            decal.name = "BoscaliSummer.BuildingScorch";
            allDecals.Add(decal);
            return decal;
        }

        internal void ReleaseDamageDecal(GameObject decal)
        {
            if (decal == null || !allDecals.Contains(decal)) return;
            decal.SetActive(false);
            availableDecals.Push(decal);
        }

        private void SelectNearestDamageDecals(Camera camera)
        {
            for (int i = trackedVisuals.Count - 1; i >= 0; i--)
            {
                BuildingDamageVisual visual = trackedVisuals[i];
                if (visual == null) trackedVisuals.RemoveAt(i);
                else visual.DecalSelection = false;
            }
            if (camera == null) return;

            int remaining = MaximumDamageDecals;
            while (remaining > 0)
            {
                BuildingDamageVisual nearest = null;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < trackedVisuals.Count; i++)
                {
                    BuildingDamageVisual visual = trackedVisuals[i];
                    if (visual.DecalSelection || visual.DesiredDamageDecals <= 0) continue;
                    float distance = (camera.transform.position - visual.WorldCenter).sqrMagnitude;
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearest = visual;
                }
                if (nearest == null) break;
                nearest.DecalSelection = true;
                int desired = Mathf.Min(nearest.DesiredDamageDecals, remaining);
                nearest.SetDamageDecalCount(desired, this);
                remaining -= desired;
            }

            for (int i = 0; i < trackedVisuals.Count; i++)
                if (!trackedVisuals[i].DecalSelection)
                    trackedVisuals[i].SetDamageDecalCount(0, this);
        }

        private void Clear()
        {
            transitionOrder.Clear();
            pendingTransitions.Clear();
            for (int i = 0; i < trackedVisuals.Count; i++)
                if (trackedVisuals[i] != null) trackedVisuals[i].ForgetDamageDecals();
            trackedVisuals.Clear();
            availableDecals.Clear();
            for (int i = 0; i < allDecals.Count; i++)
                if (allDecals[i] != null) Object.Destroy(allDecals[i]);
            allDecals.Clear();
            nextDecalSelection = 0f;
        }
    }
}
