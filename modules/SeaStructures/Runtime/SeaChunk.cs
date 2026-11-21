using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// One physical piece torn off a platform. It keeps its real mesh, falls, burns if the fire set gives it a
    /// flame, and stays as wreckage for minutes instead of being deleted after the hit. A global ceiling keeps a
    /// long battle bounded; over the ceiling the mesh is discarded and nothing is drawn.
    /// </summary>
    internal sealed class SeaChunk : MonoBehaviour
    {
        internal const int MaxLive = 240;
        private const float Life = 240f;
        private static int live;
        private Mesh mesh;
        private float born, life = Life;

        internal static int Live => live;

        internal static void Spawn(Vector3 at, Quaternion rotation, Mesh mesh, Material material, Vector3 velocity,
            float mass, float lifetime = Life)
        {
            if (mesh == null) return;
            if (live >= MaxLive) { Object.Destroy(mesh); return; }
            var go = new GameObject("SeaChunk");
            go.transform.SetParent(Datum.origin, false);
            go.transform.SetPositionAndRotation(at, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(mass, 20f, 4000f);
            body.drag = 0.05f;
            body.angularDrag = 0.4f;
            body.velocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 1.2f;
            var chunk = go.AddComponent<SeaChunk>();
            chunk.mesh = mesh;
            chunk.born = Time.time;
            chunk.life = lifetime;
            live++;
        }

        private void Update()
        {
            float age = Time.time - born;
            // Steel does not float: once it has splashed, let it go down and fade out of the fight.
            // Datum.SeaLevel is the world sea plane; LocalSeaY is only the floating-origin offset.
            if (transform.position.y < Datum.SeaLevel.y - 1.5f) transform.position += Vector3.down * (Time.deltaTime * 1.2f);
            if (age > life) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
            live--;
        }
    }
}
