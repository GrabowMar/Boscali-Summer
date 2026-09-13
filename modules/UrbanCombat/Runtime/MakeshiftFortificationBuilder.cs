using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal static class MakeshiftFortificationBuilder
    {
        internal const string NamePrefix = "BoscaliSummer:AssaultDefense:";

        public static GameObject CreateConcreteBarrier(Vector3 position, Quaternion rotation, Transform parent)
        {
            var go = new GameObject("BoscaliSummer.ConcreteBarrier");
            go.transform.position = position;
            go.transform.rotation = rotation;
            if (parent != null) go.transform.SetParent(parent, true);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "JerseyBarrier";

            float hw = 0.35f;
            float thw = 0.15f;
            float hl = 1.6f;
            float h = 1.1f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-hl, 0f, -hw),
                new Vector3( hl, 0f, -hw),
                new Vector3( hl, 0f,  hw),
                new Vector3(-hl, 0f,  hw),

                new Vector3(-hl, h, -thw),
                new Vector3( hl, h, -thw),
                new Vector3( hl, h,  thw),
                new Vector3(-hl, h,  thw),
            };

            int[] triangles = new int[]
            {
                0, 4, 1,  1, 4, 5,
                2, 6, 3,  3, 6, 7,
                4, 7, 5,  5, 7, 6,
                0, 3, 4,  4, 3, 7,
                1, 5, 2,  2, 5, 6,
                0, 1, 3,  3, 1, 2
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            Material mat = MaterialProvider.GetConcreteMaterial() ?? MaterialProvider.GetSandbagMaterial();
            if (mat != null) mr.sharedMaterial = mat;

            return go;
        }

        public static void ApplyPresentation(Building building)
        {
            if (building == null || building.transform.Find("BoscaliSummer.FortificationPresentation") != null)
                return;

            var root = new GameObject("BoscaliSummer.FortificationPresentation");
            root.transform.SetParent(building.transform, false);

            GameObject left = CreateConcreteBarrier(
                building.transform.position - building.transform.right * 2.2f,
                building.transform.rotation, root.transform);
            GameObject right = CreateConcreteBarrier(
                building.transform.position + building.transform.right * 2.2f,
                building.transform.rotation, root.transform);
            if (left != null) left.transform.localPosition += Vector3.back * 1.8f;
            if (right != null) right.transform.localPosition += Vector3.back * 1.8f;

            GameObject sentry = VanillaSoldierFactory.CreateVisualSoldier(
                building.transform.position + building.transform.forward * 1.6f,
                building.transform.rotation, root.transform);
            if (sentry != null) sentry.name = "BoscaliSummer.FortificationSentry";
            OccupiedBuildingMarking.Apply(building.gameObject, building.NetworkHQ);
        }

    }
}
