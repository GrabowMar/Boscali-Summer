using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal static class MegaplaneWreckArt
    {
        private static Transform fractures;

        internal static void Add(Transform nativeArtFrame, string artNode, bool foreExposed, bool aftExposed)
        {
            if (!foreExposed && !aftExposed) return;
            if (fractures == null)
            {
                // The catalogue's disabled prefab owns these source meshes for its entire lifetime.
                Transform prefab = MegaplaneTemplate.Definition.unitPrefab.transform;
                fractures = MegaplaneArt.BuildWreck(prefab.parent);
                fractures.gameObject.SetActive(false);
            }
            if (foreExposed) Attach(nativeArtFrame, artNode + "__Fore");
            if (aftExposed) Attach(nativeArtFrame, artNode + "__Aft");
        }

        private static void Attach(Transform frame, string name)
        {
            Transform source = fractures.Find(name);
            if (source != null)
            {
                Transform instance = Object.Instantiate(source, frame, false);
                instance.name = name;
            }
        }
    }
}
