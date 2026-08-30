using BoscaliSummer.Fire;
using BoscaliSummer.Garrisons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Runtime
{
    internal sealed class PackManager : MonoBehaviour
    {
        private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ImpactFireManager.Instance?.ResetForScene();
            RuinAftermathManager.Instance?.ResetForScene();
            ZoneGarrisonManager.Instance?.ResetForScene();
            ModNet.Instance?.ResetForScene();
        }
    }
}
