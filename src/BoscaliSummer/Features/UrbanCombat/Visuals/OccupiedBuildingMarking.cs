using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal sealed class OccupiedBuildingMarking : MonoBehaviour
    {
        private GameObject rooftopMast;
        private GameObject facadeBanner;

        public static OccupiedBuildingMarking Apply(GameObject shell, FactionHQ owner, Bounds bounds)
        {
            if (shell == null) return null;
            OccupiedBuildingMarking marking = shell.GetComponent<OccupiedBuildingMarking>();
            if (marking == null) marking = shell.AddComponent<OccupiedBuildingMarking>();
            marking.Setup(owner, bounds);
            return marking;
        }

        private void Setup(FactionHQ owner, Bounds bounds)
        {
            CleanUp();

            Color factionColor = GetFactionColor(owner);

            Vector3 mastPos = bounds.center + new Vector3(bounds.extents.x * 0.75f, 0f, bounds.extents.z * 0.75f);
            mastPos.y = bounds.max.y;

            rooftopMast = new GameObject("BoscaliSummer.RooftopMast");
            rooftopMast.transform.position = mastPos;
            rooftopMast.transform.SetParent(transform, true);

            MeshFilter mf = rooftopMast.AddComponent<MeshFilter>();
            MeshRenderer mr = rooftopMast.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreatePoleMesh(5.5f, 0.15f);

            Material mastMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mastMat.color = new Color(0.2f, 0.22f, 0.24f, 1f);
            mr.sharedMaterial = mastMat;

            var beaconGo = new GameObject("BeaconLight");
            beaconGo.transform.position = mastPos + Vector3.up * 5.2f;
            beaconGo.transform.SetParent(rooftopMast.transform, true);

            Light beaconLight = beaconGo.AddComponent<Light>();
            beaconLight.type = LightType.Point;
            beaconLight.color = factionColor;
            beaconLight.range = 35f;
            beaconLight.intensity = 3.5f;

            Vector3 bannerPos = bounds.center + transform.forward * (bounds.extents.z + 0.15f);
            bannerPos.y = bounds.center.y;

            facadeBanner = new GameObject("BoscaliSummer.FacadeBanner");
            facadeBanner.transform.position = bannerPos;
            facadeBanner.transform.rotation = transform.rotation;
            facadeBanner.transform.SetParent(transform, true);

            MeshFilter bmf = facadeBanner.AddComponent<MeshFilter>();
            MeshRenderer bmr = facadeBanner.AddComponent<MeshRenderer>();
            bmf.sharedMesh = CreateBannerMesh(3.5f, 6.0f);

            Material bannerMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            bannerMat.color = factionColor;
            bmr.sharedMaterial = bannerMat;
        }

        private static Color GetFactionColor(FactionHQ owner)
        {
            if (owner != null && owner.faction != null)
            {
                Color c = owner.faction.color;
                if (c.a > 0.1f) return c;
            }

            if (owner != null)
            {
                string name = owner.name?.ToLowerInvariant() ?? string.Empty;
                if (name.Contains("boscali") || name.Contains("nato") || name.Contains("blue"))
                    return new Color(0.12f, 0.45f, 0.95f, 1f);
                if (name.Contains("primeva") || name.Contains("red") || name.Contains("opfor"))
                    return new Color(0.85f, 0.18f, 0.15f, 1f);
            }
            return new Color(0.9f, 0.65f, 0.1f, 1f);
        }

        private static Mesh CreatePoleMesh(float height, float radius)
        {
            Mesh mesh = new Mesh();
            mesh.name = "MastPole";

            int segments = 8;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            int[] triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices[i] = new Vector3(x, 0f, z);
                vertices[i + segments + 1] = new Vector3(x, height, z);
            }

            int triIdx = 0;
            for (int i = 0; i < segments; i++)
            {
                int b1 = i;
                int b2 = i + 1;
                int t1 = i + segments + 1;
                int t2 = i + segments + 2;

                triangles[triIdx++] = b1;
                triangles[triIdx++] = t1;
                triangles[triIdx++] = b2;

                triangles[triIdx++] = b2;
                triangles[triIdx++] = t1;
                triangles[triIdx++] = t2;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBannerMesh(float width, float height)
        {
            Mesh mesh = new Mesh();
            mesh.name = "FactionBanner";

            float hw = width * 0.5f;
            float hh = height * 0.5f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-hw, -hh, 0f),
                new Vector3( hw, -hh, 0f),
                new Vector3(-hw,  hh, 0f),
                new Vector3( hw,  hh, 0f)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3
            };

            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void CleanUp()
        {
            if (rooftopMast != null) { Destroy(rooftopMast); rooftopMast = null; }
            if (facadeBanner != null) { Destroy(facadeBanner); facadeBanner = null; }
        }

        private void OnDestroy()
        {
            CleanUp();
        }
    }
}