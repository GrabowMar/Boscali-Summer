using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Accept the editor's material texture paths when an older Blueprinter runtime cannot apply them.</summary>
    internal static class BlueprinterMaterialBindings
    {
        private const string AssemblyName = "com.nikkorap.blueprinter";
        private const string TexturePrefix = "materialTexture::";
        private static ManualLogSource log;
        private static Harmony harmony;

        public static void Install(ManualLogSource logger)
        {
            log = logger;
            if (harmony != null) return;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) TryPatch(assembly);
            if (harmony == null) AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        public static void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            harmony?.UnpatchSelf();
            harmony = null;
            log = null;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

        private static void TryPatch(Assembly assembly)
        {
            if (harmony != null || assembly.GetName().Name != AssemblyName) return;
            try
            {
                MethodInfo apply = assembly.GetType("Blueprinter.MemberPathSetter", false)?.GetMethod("TryApply",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(object), typeof(string), typeof(object) }, null);
                if (apply == null || apply.ReturnType != typeof(bool))
                {
                    log?.LogWarning("[Blueprinter] Material texture path adapter unavailable: MemberPathSetter.TryApply was not found.");
                    return;
                }
                var patch = new Harmony(Plugin.PluginGuid + ".blueprinter-material-bindings");
                patch.Patch(apply, postfix: new HarmonyMethod(typeof(BlueprinterMaterialBindings), nameof(ApplyTexture)));
                harmony = patch;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            }
            catch (Exception error)
            {
                log?.LogWarning("[Blueprinter] Material texture path adapter could not install: " + error.Message);
            }
        }

        private static void ApplyTexture(object __0, string __1, object __2, ref bool __result)
        {
            if (__result || !(__0 is Material material) || material == null ||
                !(__2 is Texture texture) || texture == null || __1 == null ||
                !__1.StartsWith(TexturePrefix, StringComparison.Ordinal)) return;
            string property = __1.Substring(TexturePrefix.Length);
            if (property.Length == 0 || !material.HasProperty(property)) return;
            Shader shader = material.shader;
            int index = shader != null ? shader.FindPropertyIndex(property) : -1;
            if (index < 0 || shader.GetPropertyType(index) != ShaderPropertyType.Texture) return;
            material.SetTexture(property, texture);
            __result = material.GetTexture(property) == texture;
        }
    }
}
