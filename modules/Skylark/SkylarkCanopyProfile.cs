using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal static class SkylarkCanopyProfile
    {
        private enum Surface { Canopy, Bulkhead, Spine }

        private static readonly (string Name, Surface Surface)[] Layers =
        {
            ("Canopy", Surface.Canopy), ("Canopy_Frame", Surface.Canopy), ("Canopy_Int", Surface.Canopy),
            ("Canopy_Frame_Int", Surface.Canopy), ("Canopy_InternalView", Surface.Canopy), ("Canopy_LOD", Surface.Canopy),
            ("Cockpit_Ext", Surface.Bulkhead), ("Cockpit_Interior", Surface.Bulkhead), ("Fittings_Spine", Surface.Spine)
        };

        internal static void Refit(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("CanopyMount", out Transform hinge) || !nodes.TryGetValue("CamPoint", out Transform eye)) return;
            var meshes = new List<(MeshFilter Filter, Surface Surface)>();
            foreach (var layer in Layers)
            {
                if (!nodes.TryGetValue(layer.Name, out Transform node)) continue;
                MeshFilter filter = node.GetComponent<MeshFilter>();
                // Never warp just one side of the glass sandwich.
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return;
                meshes.Add((filter, layer.Surface));
            }

            float sill = aircraft.InverseTransformPoint(hinge.position).y + .085f * size;
            float eyeStation = aircraft.InverseTransformPoint(eye.position).z;
            foreach (var mesh in meshes) Warp(aircraft, mesh.Filter, mesh.Surface, sill, eyeStation, size);
        }

        private static void Warp(Transform aircraft, MeshFilter filter, Surface surface, float sill, float eyeStation, float size)
        {
            Mesh source = filter.sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Matrix4x4 toAir = aircraft.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 toMesh = toAir.inverse;
            Matrix4x4 normalToAir = toMesh.transpose, normalToMesh = toAir.transpose;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = toAir.MultiplyPoint3x4(vertices[i]);
                float aboveSill = point.y - sill;
                float station = (point.z - eyeStation) / size;
                // In the fixed cockpit meshes only the rear bulkhead meets this contour.
                if (aboveSill <= 0f || (surface == Surface.Bulkhead && station > -.50f)) continue;
                float compression = Compression(station, surface);
                point.y -= compression * aboveSill;
                vertices[i] = toMesh.MultiplyPoint3x4(point);

                // Transform the authored vertex normals and tangents through the same warp.
                // Rebuilding normals across the split glass UV islands would show seams in reflections.
                float slopeY = 1f - compression;
                const float sample = .001f;
                float slopeZ = -aboveSill * (Compression(station + sample, surface) - Compression(station - sample, surface)) / (2f * sample * size);
                if (normals.Length == vertices.Length)
                {
                    Vector3 normal = normalToAir.MultiplyVector(normals[i]);
                    normal.y /= slopeY;
                    normal.z -= slopeZ * normal.y;
                    normals[i] = normalToMesh.MultiplyVector(normal).normalized;
                }
                if (tangents.Length == vertices.Length)
                {
                    Vector4 original = tangents[i];
                    Vector3 tangent = toAir.MultiplyVector(new Vector3(original.x, original.y, original.z));
                    tangent.y = tangent.y * slopeY + tangent.z * slopeZ;
                    tangent = toMesh.MultiplyVector(tangent).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                }
            }

            Mesh profile = Object.Instantiate(source);
            profile.name = source.name + "_FlightProfile";
            profile.vertices = vertices;
            if (normals.Length == vertices.Length) profile.normals = normals;
            else profile.RecalculateNormals();
            if (tangents.Length == vertices.Length) profile.tangents = tangents;
            profile.RecalculateBounds();
            filter.sharedMesh = profile;
        }

        private static float Compression(float station, Surface surface)
        {
            if (surface == Surface.Spine)
            {
                // Leave the rear fuselage joint alone and meet the reduced canopy at the front lip.
                float blend = Mathf.InverseLerp(-1.55f, -.70f, station);
                return .25f * blend + .02f * Mathf.Sin(Mathf.PI * blend);
            }
            // Give the seated pilot the tallest part. Taper the glass more toward the back
            // and ahead of the eye line, where the broad windscreen used to dominate the nose.
            float aft = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.64f, -.22f, station));
            float fore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.20f, .96f, station));
            return .135f + .115f * aft + .235f * fore;
        }
    }
}
