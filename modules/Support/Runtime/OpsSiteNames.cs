using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Read-only geographic labels and friendly staging sites. Airbases come from the native
    /// registry; towns reuse the field runtime's existing scan or its host-authored mirror.
    /// No object search, defender information, instance-id matching or authority lives here.
    /// </summary>
    internal sealed class OpsSiteNames
    {
        internal const int MaximumHomes = 8;
        private const int MaximumAirbases = 64;
        private const int MaximumRegistryReads = 256;
        private const int MaximumTowns = 24;
        private const float RefreshSeconds = 2f;
        private const float MatchMetres = 250f;

        private readonly Airbase[] airbases = new Airbase[MaximumAirbases];
        private readonly float[] airbaseX = new float[MaximumAirbases], airbaseZ = new float[MaximumAirbases];
        private readonly string[] airbaseNames = new string[MaximumAirbases];
        private readonly float[] homeX = new float[MaximumHomes], homeZ = new float[MaximumHomes];
        private readonly string[] homeNames = new string[MaximumHomes];
        private readonly float[] townX = new float[MaximumTowns], townZ = new float[MaximumTowns];
        private readonly string[] townNames = new string[MaximumTowns];
        private FactionHQ owner;
        private float nextRefresh;
        private int airbaseCount, homeCount, townCount;

        public void Refresh(FactionHQ currentOwner, SpecOpsTheater field, SpecOpsDetachment detachment, float now)
        {
            if (currentOwner == null)
            {
                if (owner != null || airbaseCount != 0 || homeCount != 0 || townCount != 0) Clear();
                return;
            }
            if (owner == currentOwner && now < nextRefresh) return;
            owner = currentOwner;
            nextRefresh = now + RefreshSeconds;
            airbaseCount = homeCount = 0;

            int examined = 0;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbaseCount >= MaximumAirbases || examined++ >= MaximumRegistryReads) break;
                    if (airbase == null || airbase.AttachedAirbase) continue;
                    GlobalPosition at = Centre(airbase).ToGlobalPosition();
                    if (!Finite(at.x) || !Finite(at.z)) continue;
                    int slot = airbaseCount++;
                    airbases[slot] = airbase;
                    airbaseX[slot] = at.x;
                    airbaseZ[slot] = at.z;
                    airbaseNames[slot] = AirbaseName(airbase);
                }
            for (int i = airbaseCount; i < MaximumAirbases; i++)
            {
                airbases[i] = null;
                airbaseNames[i] = null;
            }

            examined = 0;
            foreach (Airbase airbase in currentOwner.GetAirbases())
            {
                if (homeCount >= MaximumHomes || examined++ >= MaximumRegistryReads) break;
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != currentOwner) continue;
                GlobalPosition at = Centre(airbase).ToGlobalPosition();
                if (!Finite(at.x) || !Finite(at.z)) continue;
                int slot = homeCount++;
                homeX[slot] = at.x;
                homeZ[slot] = at.z;
                homeNames[slot] = NameForAirbase(airbase);
            }
            for (int i = homeCount; i < MaximumHomes; i++) homeNames[i] = null;

            townCount = field != null ? field.CopyTownNames(townX, townZ, townNames) : 0;
            // Remote peers have no host-only town scan. Their named objectives are an already
            // verified geographic feed; use those names without deriving any hidden defenses.
            for (int i = 0; detachment != null && i < detachment.ObjectiveCount && townCount < MaximumTowns; i++)
            {
                FieldObjective objective = detachment.Objective(i);
                if (objective.Kind != ObjectiveKind.Town || string.IsNullOrWhiteSpace(objective.Name) ||
                    !Finite(objective.X) || !Finite(objective.Z)) continue;
                bool duplicate = false;
                for (int t = 0; t < townCount; t++)
                    if (Math.Abs(townX[t] - objective.X) < 1f && Math.Abs(townZ[t] - objective.Z) < 1f)
                    { duplicate = true; break; }
                if (duplicate) continue;
                townX[townCount] = objective.X;
                townZ[townCount] = objective.Z;
                townNames[townCount++] = objective.Name;
            }
            for (int i = townCount; i < MaximumTowns; i++) townNames[i] = null;
        }

        /// <summary>Copy only real friendly staging sites; caller buffers and the feed both cap at eight.</summary>
        public int FillHomes(float[] xs, float[] zs, string[] names)
        {
            if (xs == null || zs == null) return 0;
            int capacity = Math.Min(MaximumHomes, Math.Min(xs.Length, zs.Length));
            if (names != null) capacity = Math.Min(capacity, names.Length);
            int count = Math.Min(homeCount, capacity);
            for (int i = 0; i < capacity; i++)
            {
                xs[i] = i < count ? homeX[i] : 0f;
                zs[i] = i < count ? homeZ[i] : 0f;
                if (names != null) names[i] = i < count ? homeNames[i] : null;
            }
            return count;
        }

        /// <summary>A label only when the correct kind of real site matches within 250 metres.</summary>
        public string NameForNode(NodeKind kind, float x, float z)
        {
            if (!Finite(x) || !Finite(z)) return null;
            if (kind == NodeKind.City) return Match(townX, townZ, townNames, townCount, x, z);
            if (kind == NodeKind.Command || kind == NodeKind.Base || kind == NodeKind.Airfield)
                return Match(airbaseX, airbaseZ, airbaseNames, airbaseCount, x, z);
            return null;
        }

        public void Clear()
        {
            owner = null;
            nextRefresh = 0f;
            airbaseCount = homeCount = townCount = 0;
            Array.Clear(airbases, 0, airbases.Length);
            Array.Clear(airbaseNames, 0, airbaseNames.Length);
            Array.Clear(homeNames, 0, homeNames.Length);
            Array.Clear(townNames, 0, townNames.Length);
        }

        private string NameForAirbase(Airbase airbase)
        {
            for (int i = 0; i < airbaseCount; i++)
                if (airbases[i] == airbase) return airbaseNames[i];
            return AirbaseName(airbase);
        }

        private static string Match(float[] xs, float[] zs, string[] names, int count, float x, float z)
        {
            string name = null;
            float nearest = MatchMetres * MatchMetres;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])) continue;
                float dx = xs[i] - x, dz = zs[i] - z;
                float squared = dx * dx + dz * dz;
                if (squared > nearest) continue;
                nearest = squared;
                name = names[i];
            }
            return name;
        }

        private static string AirbaseName(Airbase airbase)
        {
            string name = null;
            try
            {
                if (airbase.SavedAirbase != null) name = airbase.SavedAirbase.DisplayName;
            }
            catch (Exception)
            {
                name = null;
            }
            if (string.IsNullOrWhiteSpace(name)) name = airbase.name;
            name = PlaceNames.Clean(name);
            return name.Length >= 3 ? name : null;
        }

        private static Vector3 Centre(Airbase airbase) =>
            airbase.center != null ? airbase.center.position : airbase.transform.position;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
