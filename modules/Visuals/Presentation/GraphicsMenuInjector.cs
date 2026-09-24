using System;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Features.Visuals.Runtime;
using NuclearOption.MissionEditorScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Visuals.Presentation
{
    /// <summary>
    /// Injects native-styled UI toggles into Nuclear Option's in-game Graphics settings panel (GraphicsMenu).
    /// Clones native controls to preserve exact fonts, styling, sounds, and layout.
    /// </summary>
    internal static class GraphicsMenuInjector
    {
        private static VisualsSettings settings;

        public static void SetSettings(VisualsSettings visualsSettings)
        {
            settings = visualsSettings;
        }

        public static void Inject(GraphicsMenu menu)
        {
            if (menu == null || settings == null) return;

            try
            {
                Transform bottomPanel = null;
                Transform content = null;
                GameObject rowTemplate = null;

                // 1. Try finding via debugVisToggle
                FieldInfo debugVisField = typeof(GraphicsMenu).GetField("debugVisToggle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Toggle debugVisToggle = debugVisField?.GetValue(menu) as Toggle;
                if (debugVisToggle != null)
                {
                    rowTemplate = debugVisToggle.transform.parent.gameObject;
                    bottomPanel = rowTemplate.transform.parent;
                    content = bottomPanel?.parent;
                }

                // 2. Fallback search via menu hierarchy
                if (bottomPanel == null)
                {
                    content = menu.transform.Find("Scroll View/Viewport/Content");
                    bottomPanel = content?.Find("BottomPanel ") ?? content?.Find("BottomPanel");
                }

                if (bottomPanel == null)
                {
                    Debug.LogWarning("[Visuals] Could not locate BottomPanel in GraphicsMenu hierarchy.");
                    return;
                }

                // Prevent duplicate injection if menu is reopened
                if (bottomPanel.Find("BoscaliVisuals_Header") != null)
                {
                    Refresh(menu);
                    return;
                }

                // 3. Fallback row template if debugVis was not resolved
                if (rowTemplate == null)
                {
                    FieldInfo grassField = typeof(GraphicsMenu).GetField("grassToggle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Toggle grassToggle = grassField?.GetValue(menu) as Toggle;
                    if (grassToggle != null)
                    {
                        rowTemplate = grassToggle.transform.parent.gameObject;
                    }
                }

                if (rowTemplate == null)
                {
                    Debug.LogWarning("[Visuals] Could not locate a native toggle row template in GraphicsMenu.");
                    return;
                }

                // 4. Find Header template from TopPanel or SideBySidePanel
                Transform headerTemplate = content?.Find("TopPanel/Header")
                    ?? content?.Find("SideBySidePanel/Right Panel/Header (1)")
                    ?? content?.Find("SideBySidePanel/Left Panel/Header");

                // Target index inside BottomPanel (place right before "Reset Button Box" if present)
                Transform resetBox = bottomPanel.Find("Reset Button Box");
                int insertIndex = resetBox != null ? resetBox.GetSiblingIndex() : bottomPanel.childCount;

                // 5. Create Header
                CreateHeader(bottomPanel, headerTemplate, insertIndex);
                insertIndex++;

                // 6. Create Toggles
                CreateToggleRow(rowTemplate, bottomPanel, insertIndex++, "Boscali_CinematicPostFx", "Cinematic Post-FX (ACES & Bloom)", settings.CinematicPostFxEnabled);
                CreateToggleRow(rowTemplate, bottomPanel, insertIndex++, "Boscali_GForceVisuals", "G-Force Visuals (Blackout & Redout)", settings.GForceEffectsEnabled);
                CreateToggleRow(rowTemplate, bottomPanel, insertIndex++, "Boscali_MotionBlur", "High-Speed Transonic Blur", settings.MotionBlurEnabled);
                CreateToggleRow(rowTemplate, bottomPanel, insertIndex++, "Boscali_FoliageDynamics", "Foliage Wind Dynamics", settings.FoliageDynamicsEnabled);

                // 7. Force layout recalculation across the hierarchy
                if (bottomPanel is RectTransform bottomRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(bottomRect);
                }
                if (content is RectTransform contentRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                }
                if (menu.transform is RectTransform menuRect)
                {
                    FixLayout.ForceRebuildRecursive(menuRect);
                }

                Debug.Log("[Visuals] Injected Boscali Visual Enhancements toggles into GraphicsMenu.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Visuals] Could not inject settings UI into GraphicsMenu: {ex.Message}");
            }
        }

        public static void Refresh(GraphicsMenu menu)
        {
            if (menu == null || settings == null) return;

            try
            {
                Transform content = menu.transform.Find("Scroll View/Viewport/Content");
                Transform bottomPanel = content?.Find("BottomPanel ") ?? content?.Find("BottomPanel");
                if (bottomPanel == null) return;

                UpdateToggleState(bottomPanel, "Boscali_CinematicPostFx", settings.CinematicPostFxEnabled.Value);
                UpdateToggleState(bottomPanel, "Boscali_GForceVisuals", settings.GForceEffectsEnabled.Value);
                UpdateToggleState(bottomPanel, "Boscali_MotionBlur", settings.MotionBlurEnabled.Value);
                UpdateToggleState(bottomPanel, "Boscali_FoliageDynamics", settings.FoliageDynamicsEnabled.Value);
            }
            catch
            {
                // Ignored
            }
        }

        private static void CreateHeader(Transform bottomPanel, Transform headerTemplate, int siblingIndex)
        {
            GameObject headerObj;
            if (headerTemplate != null)
            {
                headerObj = UnityEngine.Object.Instantiate(headerTemplate.gameObject, bottomPanel);
            }
            else
            {
                headerObj = new GameObject("BoscaliVisuals_Header");
                headerObj.transform.SetParent(bottomPanel, false);
                LayoutElement layout = headerObj.AddComponent<LayoutElement>();
                layout.minHeight = 28f;
                layout.preferredHeight = 28f;
            }

            headerObj.name = "BoscaliVisuals_Header";
            headerObj.transform.SetSiblingIndex(siblingIndex);

            TextMeshProUGUI headerText = headerObj.GetComponentInChildren<TextMeshProUGUI>();
            if (headerText != null)
            {
                headerText.text = "BOSCALI VISUAL ENHANCEMENTS";
                headerText.color = new Color(0.35f, 0.85f, 1f, 1f); // Tactical cyan accent
            }
        }

        private static void CreateToggleRow(GameObject rowTemplate, Transform bottomPanel, int siblingIndex, string name, string labelText, ConfigEntry<bool> entry)
        {
            GameObject rowObj = UnityEngine.Object.Instantiate(rowTemplate, bottomPanel);
            rowObj.name = name;
            rowObj.transform.SetSiblingIndex(siblingIndex);

            TextMeshProUGUI label = rowObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = labelText;
            }

            Toggle toggle = rowObj.GetComponentInChildren<Toggle>();
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
                toggle.SetIsOnWithoutNotify(entry.Value);
                toggle.onValueChanged.AddListener((val) =>
                {
                    entry.Value = val;
                    if (VisualsManager.Instance != null) VisualsManager.Instance.ApplySettings();
                    if (FoliageWindService.Instance != null) FoliageWindService.Instance.ApplySettings();
                });
            }
        }

        private static void UpdateToggleState(Transform container, string name, bool value)
        {
            Transform t = container.Find(name);
            if (t != null)
            {
                Toggle toggle = t.GetComponentInChildren<Toggle>();
                if (toggle != null)
                {
                    toggle.SetIsOnWithoutNotify(value);
                }
            }
        }
    }
}
