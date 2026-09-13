using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Patches
{
    /// <summary>
    /// Dynamically anchors map grid coordinate labels, minor tick labels, and corner
    /// readouts to the actual viewport boundaries of <see cref="DynamicMap"/>.
    ///
    /// <para>In vanilla Nuclear Option, the maximized map viewport was a fixed 900x900
    /// square, and <see cref="GridLabels"/> hardcoded viewport edge positions:
    /// <list type="bullet">
    ///   <item><c>+440f</c> (450 - 10) for major top coordinate numbers (1..12)</item>
    ///   <item><c>-440f</c> (-450 + 10) for major left coordinate letters (A..L)</item>
    ///   <item><c>+420f</c> / <c>-420f</c> (450 - 30) for minor sub-cell tick labels</item>
    ///   <item><c>(430f, -390f)</c> and <c>(430f, -420f)</c> for corner tooltips and aircraft coords</item>
    /// </list>
    /// When Boscali Summer expands the map viewport to fill the center column (e.g. ~1320x918),
    /// the hardcoded <c>-440f</c> left edge pinned the vertical labels inside column 3 over active
    /// map terrain instead of the actual left edge of the expanded viewport. These transpilers
    /// replace the constants with dynamic bounds queries against the live map <see cref="RectTransform"/>.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class GridLabelsPatch
    {
        public const float MajorPadding = GridLabelsMath.MajorPadding;
        public const float MinorPadding = GridLabelsMath.MinorPadding;
        public const float FallbackSize = GridLabelsMath.FallbackSize;

        private static readonly FieldInfo GridToolTipField = AccessTools.Field(typeof(GridLabels), "gridToolTip");
        private static readonly FieldInfo GridAircraftField = AccessTools.Field(typeof(GridLabels), "gridAircraft");

        private static readonly MethodInfo GetTopMethod = typeof(GridLabelsPatch).GetMethod(nameof(GetTopEdge), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GetLeftMethod = typeof(GridLabelsPatch).GetMethod(nameof(GetLeftEdge), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GetTopMinorMethod = typeof(GridLabelsPatch).GetMethod(nameof(GetTopEdgeMinor), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GetLeftMinorMethod = typeof(GridLabelsPatch).GetMethod(nameof(GetLeftEdgeMinor), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GetMapWidthMethod = typeof(GridLabelsPatch).GetMethod(nameof(GetMapImageBaseWidth), BindingFlags.Public | BindingFlags.Static);

        // ---------------------------------------------------------------- pure math calculations

        public static float CalculateTopEdge(float viewportHeight, float padding = MajorPadding) =>
            GridLabelsMath.CalculateTopEdge(viewportHeight, padding);

        public static float CalculateLeftEdge(float viewportWidth, float padding = MajorPadding) =>
            GridLabelsMath.CalculateLeftEdge(viewportWidth, padding);

        public static float CalculateTopEdgeMinor(float viewportHeight, float padding = MinorPadding) =>
            GridLabelsMath.CalculateTopEdgeMinor(viewportHeight, padding);

        public static float CalculateLeftEdgeMinor(float viewportWidth, float padding = MinorPadding) =>
            GridLabelsMath.CalculateLeftEdgeMinor(viewportWidth, padding);

        public static Vector2 CalculateTooltipPosition(float viewportWidth, float viewportHeight)
        {
            GridLabelsMath.CalculateTooltipPosition(viewportWidth, viewportHeight, out float x, out float y);
            return new Vector2(x, y);
        }

        public static Vector2 CalculateAircraftCoordPosition(float viewportWidth, float viewportHeight)
        {
            GridLabelsMath.CalculateAircraftCoordPosition(viewportWidth, viewportHeight, out float x, out float y);
            return new Vector2(x, y);
        }

        // ---------------------------------------------------------------- dynamic edge lookups

        public static float GetTopEdge()
        {
            GetViewportHalfExtents(out _, out float halfH);
            return CalculateTopEdge(halfH * 2f);
        }

        public static float GetLeftEdge()
        {
            GetViewportHalfExtents(out float halfW, out _);
            return CalculateLeftEdge(halfW * 2f);
        }

        public static float GetTopEdgeMinor()
        {
            GetViewportHalfExtents(out _, out float halfH);
            return CalculateTopEdgeMinor(halfH * 2f);
        }

        public static float GetLeftEdgeMinor()
        {
            GetViewportHalfExtents(out float halfW, out _);
            return CalculateLeftEdgeMinor(halfW * 2f);
        }

        public static float GetMapImageBaseWidth()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && map.mapImage != null)
            {
                RectTransform rt = map.mapImage.GetComponent<RectTransform>();
                if (rt != null && rt.sizeDelta.x > 100f)
                    return rt.sizeDelta.x;
            }
            return FallbackSize;
        }

        public static void GetViewportHalfExtents(out float halfWidth, out float halfHeight)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && DynamicMap.mapMaximized)
            {
                RectTransform rt = map.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Rect rect = rt.rect;
                    if (rect.width > 100f && rect.height > 100f)
                    {
                        halfWidth = rect.width * 0.5f;
                        halfHeight = rect.height * 0.5f;
                        return;
                    }
                }
            }

            halfWidth = FallbackSize * 0.5f;
            halfHeight = FallbackSize * 0.5f;
        }

        // ---------------------------------------------------------------- corner readouts

        public static void RepositionCornerReadouts(GridLabels gridLabels)
        {
            if (gridLabels == null) return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized) return;

            GetViewportHalfExtents(out float halfW, out float halfH);
            Vector2 tooltipPos = CalculateTooltipPosition(halfW * 2f, halfH * 2f);
            Vector2 aircraftPos = CalculateAircraftCoordPosition(halfW * 2f, halfH * 2f);

            var tooltip = GridToolTipField?.GetValue(gridLabels) as Text;
            if (tooltip != null)
            {
                tooltip.transform.localPosition = new Vector3(tooltipPos.x, tooltipPos.y, 0f);
            }

            var aircraft = GridAircraftField?.GetValue(gridLabels) as Text;
            if (aircraft != null)
            {
                aircraft.transform.localPosition = new Vector3(aircraftPos.x, aircraftPos.y, 0f);
            }
        }

        // ---------------------------------------------------------------- harmony patches

        [HarmonyPatch(typeof(GridLabels), nameof(GridLabels.GridLabels_OnMapChanged))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> OnMapChangedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float f)
                {
                    if (Mathf.Approximately(f, 440f))
                    {
                        yield return new CodeInstruction(OpCodes.Call, GetTopMethod);
                        continue;
                    }
                    if (Mathf.Approximately(f, -440f))
                    {
                        yield return new CodeInstruction(OpCodes.Call, GetLeftMethod);
                        continue;
                    }
                }
                yield return instruction;
            }
        }

        [HarmonyPatch(typeof(GridLabels), "UpdateMinorGridLabels")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateMinorGridLabelsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float f)
                {
                    if (Mathf.Approximately(f, 420f))
                    {
                        yield return new CodeInstruction(OpCodes.Call, GetTopMinorMethod);
                        continue;
                    }
                    if (Mathf.Approximately(f, -420f))
                    {
                        yield return new CodeInstruction(OpCodes.Call, GetLeftMinorMethod);
                        continue;
                    }
                    if (Mathf.Approximately(f, 900f))
                    {
                        yield return new CodeInstruction(OpCodes.Call, GetMapWidthMethod);
                        continue;
                    }
                }
                yield return instruction;
            }
        }

        [HarmonyPatch(typeof(GridLabels), nameof(GridLabels.Maximize))]
        [HarmonyPostfix]
        private static void MaximizePostfix(GridLabels __instance, bool maximized)
        {
            if (!maximized) return;
            RepositionCornerReadouts(__instance);
        }
    }
}
