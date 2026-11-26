using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Native map loading, with hooks for map setup and client readiness.</summary>
    [HarmonyPatch]
    internal static class MapLoadPatch
    {
        internal static event Action<MapLoader> BeforeLoad;
        internal static Func<UniTask<MapLoader.LoadResult>, UniTask<MapLoader.LoadResult>> WrapResult;

        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(
            typeof(MapLoader), "Load", new[] { typeof(MapKey), typeof(IProgress<float>), typeof(LoadingFade?) });
        private static bool Prepare() =>
            TargetMethod() is MethodInfo method && method.ReturnType == typeof(UniTask<MapLoader.LoadResult>);

        private static void Prefix(MapLoader __instance) => BeforeLoad?.Invoke(__instance);

        private static void Postfix(ref UniTask<MapLoader.LoadResult> __result)
        {
            if (WrapResult != null) __result = WrapResult(__result);
        }
    }

    /// <summary>The sandbox witness observes attempts. Gameplay consumers see the completed native report.</summary>
    [HarmonyPatch]
    internal static class UnitReportKilledPatch
    {
        internal static event Action<Unit> AfterReport;
#if !PUBLIC_RELEASE
        internal static event Action<Unit> BeforeReport;

        private static void Prefix(Unit __instance) => BeforeReport?.Invoke(__instance);
#endif

        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(Unit), "ReportKilled", Type.EmptyTypes);
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Unit __instance) => AfterReport?.Invoke(__instance);
    }
}
