using System;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// One front position: a natural Bezier curve fitted to a stretch of Command's front
    /// trace and offset onto the owning side, plus the support and redoubt traces,
    /// communication links and forward saps it matures into. Nodes and edges are gone;
    /// every consumer anchors to the curve stations.
    /// </summary>
    internal sealed class TrenchLine
    {
        public const int MaximumCurvePoints = TrenchTraceMath.MaximumStations;

        public int Id { get; }
        public string Name { get; set; }
        public FactionHQ OwnerHq { get; set; }
        public TrenchStage Stage { get; set; }
        public float Pressure { get; }
        public Vector3 Center { get; private set; }
        public float Radius { get; private set; }
        public float NextGrowthAt { get; set; }
        public bool Suppressed { get; set; }
        public bool Overrun { get; set; }
        public float RetireAt { get; set; }
        public int DefenderCount { get; set; }

        /// <summary>Trace stations on the contour; y is unused, offsets are lateral.</summary>
        public Vector3[] Base { get; }
        /// <summary>Owned-side unit direction per station, away from the enemy.</summary>
        public Vector3[] Inward { get; }
        /// <summary>Chosen ground-seeking offset behind the trace, per station.</summary>
        public float[] Offset { get; }
        /// <summary>Metres between curve stations, for run-length and anchor maths.</summary>
        public float StationSpacing { get; }

        /// <summary>Fire-trench curve: ground-snapped stations every ~10m.</summary>
        public Vector3[] Curve { get; set; }
        /// <summary>Enemy direction per fire-curve station; the parapet faces it.</summary>
        public Vector3[] Threat { get; set; }
        /// <summary>Anchors for works and defenders, roughly every 60m of ditch.</summary>
        public Vector3[] Anchors { get; set; }

        public Vector3[] Support { get; set; }
        public Vector3[] SupportAnchors { get; set; }
        public Vector3[] Redoubt { get; set; }
        public Vector3[] RedoubtAnchors { get; set; }
        public Vector3[][] Links { get; set; }
        public Vector3[][] Spurs { get; set; }

        /// <summary>Corridor, ownership and buildable-ground check for nested footprints.</summary>
        public Func<Vector3, bool> Validator { get; set; }

        public TrenchLine(int id, string name, FactionHQ owner, float pressure, float stationSpacing,
            Vector3[] baseCurve, Vector3[] inward, float[] offset, Vector3[] curve, Vector3[] threat)
        {
            Id = id;
            Name = name;
            OwnerHq = owner;
            Pressure = pressure;
            StationSpacing = stationSpacing;
            Base = baseCurve;
            Inward = inward;
            Offset = offset;
            Curve = curve;
            Threat = threat;
            Stage = TrenchStage.Scrape;
            Validate();
        }

        /// <summary>Centre and bounding radius of the fire curve, for LOD and spacing tests.</summary>
        public void Validate()
        {
            if (Curve == null || Curve.Length == 0) return;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < Curve.Length; i++) sum += Curve[i];
            Center = sum / Curve.Length;

            float maxSq = 0f;
            for (int i = 0; i < Curve.Length; i++)
            {
                float dSq = (Curve[i] - Center).sqrMagnitude;
                if (dSq > maxSq) maxSq = dSq;
            }
            Radius = Mathf.Sqrt(maxSq) + 20f;
        }

        public bool Contains(Vector3 position)
            => Validator == null || Validator(position);

        /// <summary>Enemy direction at the curve station nearest a position.</summary>
        public Vector3 ThreatAt(Vector3 position)
        {
            if (Threat == null || Threat.Length == 0) return Vector3.forward;
            if (Curve == null || Curve.Length == 0) return Threat[0];
            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Curve.Length; i++)
            {
                float dx = Curve[i].x - position.x, dz = Curve[i].z - position.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return Threat[best];
        }
    }
}
