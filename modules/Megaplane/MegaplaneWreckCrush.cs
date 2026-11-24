using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>One local compression crease where the airframe first struck solid ground.
    /// Both LODs and their attached parts use the same deformation in original aircraft coordinates.</summary>
    internal sealed class MegaplaneWreckCrush : MonoBehaviour
    {
        private const float Length = 11f, Width = 8f, Depth = 6f, MaxDent = 1.85f;
        private Vector3 contact, along, across, inward;
        private float strength;
        private readonly List<Mesh> owned = new List<Mesh>();
        private readonly List<MeshFilter> filters = new List<MeshFilter>();
        private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private Vector3 hotspot, contactNormal;

        internal int Meshes => owned.Count;
        internal int MovedVertices { get; private set; }
        internal float MaxDisplacement { get; private set; }
        internal Renderer HotRenderer { get; private set; }
        internal Vector3 WorldHotspot => HotRenderer != null ? HotRenderer.transform.TransformPoint(hotspot) : transform.position;
        internal Vector3 WorldContactNormal => HotRenderer != null ? HotRenderer.localToWorldMatrix.MultiplyVector(contactNormal).normalized : Vector3.up;

        internal static MegaplaneWreckCrush Create(Transform field, Vector3 location, Vector3 normal, float severity, bool water)
        {
            if (water || severity <= .55f || normal.sqrMagnitude < .5f) return null;
            var crush = field.gameObject.AddComponent<MegaplaneWreckCrush>();
            crush.contact = location;
            crush.inward = normal.normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.forward,crush.inward);
            if (tangent.sqrMagnitude < .05f) tangent = Vector3.ProjectOnPlane(Vector3.right,crush.inward);
            crush.along = tangent.normalized;
            crush.across = Vector3.Cross(crush.inward,crush.along).normalized;
            crush.strength = MaxDent * Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.55f,1f,severity));
            return crush;
        }

        // Signed displacement along the contact normal and its exact derivative. Cubic
        // falloff gives a smooth, stationary border; opposite panels beyond the depth band stay put.
        private float Displacement(Vector3 p, out Vector3 gradient)
        {
            Vector3 d = p-contact;
            float u = Vector3.Dot(d,along)/Length, v = Vector3.Dot(d,across)/Width, w = Vector3.Dot(d,inward)/Depth;
            float remaining = 1f-u*u-v*v-w*w;
            if (remaining <= 0f) { gradient = Vector3.zero; return 0f; }
            float ridge = u+.32f*v;
            float corrugation = Mathf.Exp(-13f*ridge*ridge);
            float profile = .47f+.53f*corrugation;
            float cubed = remaining*remaining*remaining;
            float shoulder = -6f*strength*profile*remaining*remaining;
            float ridgeSlope = -13.78f*strength*cubed*ridge*corrugation;
            gradient = along*((shoulder*u+ridgeSlope)/Length) +
                       across*((shoulder*v+.32f*ridgeSlope)/Width) + inward*(shoulder*w/Depth);
            return strength*cubed*profile;
        }

        internal void Apply(Transform piece, Transform leaf)
        {
            filters.Clear();
            leaf.GetComponentsInChildren(true,filters);
            foreach (MeshFilter filter in filters)
            {
                Mesh source = filter.sharedMesh;
                if (source == null || owned.Contains(source)) continue;
                Matrix4x4 toPiece = piece.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                Matrix4x4 fromPiece = toPiece.inverse;
                Vector3 localContact = fromPiece.MultiplyPoint3x4(contact);
                Vector3 closest = toPiece.MultiplyVector(source.bounds.ClosestPoint(localContact)-localContact);
                if (closest.sqrMagnitude > Length*Length) continue;

                source.GetVertices(vertices);
                bool changed = false;
                Matrix4x4 normalToPiece = fromPiece.transpose, normalFromPiece = toPiece.transpose;
                for (int i=0; i<vertices.Count; i++)
                {
                    Vector3 p = toPiece.MultiplyPoint3x4(vertices[i]);
                    float offset = Displacement(p,out Vector3 gradient);
                    if (offset < .0005f) continue;
                    if (!changed)
                    {
                        source.GetNormals(normals);
                        source.GetTangents(tangents);
                        changed = true;
                    }
                    vertices[i] += fromPiece.MultiplyVector(inward*offset);
                    // For J = I + inward ⊗ gradient, transport split normals by J^-T
                    // and tangents by J. UV seams, normal maps and hard panel edges survive.
                    if (normals.Count == vertices.Count)
                    {
                        Vector3 n = normalToPiece.MultiplyVector(normals[i]);
                        n -= gradient*(Vector3.Dot(inward,n)/(1f+Vector3.Dot(gradient,inward)));
                        normals[i] = normalFromPiece.MultiplyVector(n).normalized;
                    }
                    if (tangents.Count == vertices.Count)
                    {
                        Vector4 original = tangents[i];
                        Vector3 tangent = toPiece.MultiplyVector(new Vector3(original.x,original.y,original.z));
                        tangent = fromPiece.MultiplyVector(tangent+inward*Vector3.Dot(gradient,tangent)).normalized;
                        tangents[i] = new Vector4(tangent.x,tangent.y,tangent.z,original.w);
                    }
                    MovedVertices++;
                    if (offset > MaxDisplacement)
                    {
                        MaxDisplacement = offset;
                        hotspot = vertices[i];
                        contactNormal = fromPiece.MultiplyVector(inward).normalized;
                        HotRenderer = filter.GetComponent<Renderer>();
                    }
                }
                if (!changed) continue;
                Mesh dented = Instantiate(source);
                owned.Add(dented);
                dented.name = "GrozaCrush_"+source.name;
                dented.SetVertices(vertices);
                if (normals.Count == vertices.Count) dented.SetNormals(normals);
                if (tangents.Count == vertices.Count) dented.SetTangents(tangents);
                dented.RecalculateBounds();
                filter.sharedMesh = dented;
            }
        }

        internal void Finish()
        {
            // Free temporary vertex buffers after construction. Settled wrecks can persist for minutes.
            filters.Clear(); filters.TrimExcess();
            vertices.Clear(); vertices.TrimExcess();
            normals.Clear(); normals.TrimExcess();
            tangents.Clear(); tangents.TrimExcess();
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in owned) if (mesh != null) Destroy(mesh);
        }
    }
}
