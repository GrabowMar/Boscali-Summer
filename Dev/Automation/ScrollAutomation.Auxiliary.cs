#if !PUBLIC_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Events.Presentation;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Runtime;
using NOAvionics;
using UnityEngine;
using UnityEngine.EventSystems;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Command.Runtime
{
    public static partial class ScrollAutomation
    {
        /// <summary>Sandbox-only auxiliary-window navigation/readback. Never applies a formation, weather or drone order.</summary>
        public static Dictionary<string, object> Auxiliary(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("ScrollAutomation", "Auxiliary", "requires a nomodkit scenario");
            string id = Text(args, "window") ?? "", action = Text(args, "action") ?? "read";
            int requested = (int)Number(args, "page", 0f), page = 0;
            string[] pages = { "MAIN" };
            object owner = null;
            bool preview = false;
            if (action != "open" && action != "page" && action != "read" && action != "close")
                return Failure("ScrollAutomation", "Auxiliary", "action must be open, page, read or close");

            switch (id)
            {
                case "archive":
                    AvConsole console = VisibleConsole("EVN");
                    // The module host is HideAndDontSave; ordinary scene searches exclude it.
                    EventsMfdPanel desk = Array.Find(Resources.FindObjectsOfTypeAll<EventsMfdPanel>(),
                        candidate => candidate.isActiveAndEnabled && console != null && ReferenceEquals(Field(candidate, "console"), console));
                    if (desk == null) return Failure("ScrollAutomation", "Auxiliary", "no visible events desk");
                    if (action == "open")
                    {
                        if (console.CurrentPageName != "DESK")
                            return Failure("ScrollAutomation", "Auxiliary", "open EVN DESK first");
                        AuxCall(desk, "OpenArchive", Mathf.Clamp(requested, 0, 7), 0);
                    }
                    owner = Field(desk, "archive");
                    if (owner == null) return AuxAbsent(id, action);
                    if (action == "page") AuxCall(owner, "SelectShelf", Mathf.Clamp(requested, 0, 7), 0);
                    if (action == "close") AuxCall(owner, "Close");
                    page = (int)Field(owner, "shelf"); pages = ArchiveShelf.Names;
                    break;
                case "shapes":
                    WmcForm form = WmcPanel.Instance?.Tactical == null ? null : Field(WmcPanel.Instance.Tactical, "form") as WmcForm;
                    if (form == null) return Failure("ScrollAutomation", "Auxiliary", "open WMC FORMATION first");
                    if (action == "open")
                    {
                        // Production's existing preview entry renders the real catalog without recruiting a wing or issuing an order.
                        var families = new List<string>(); FormationSelection.Families(WingData.Formations, families);
                        form.OpenShapeWindow(WingData.Formations, families, WingService.Instance?.Selection?.Current);
                    }
                    owner = Field(form, "window");
                    if (owner == null) return AuxAbsent(id, action);
                    if (action == "page") AuxCall(owner, "PickFamilyTab", requested);
                    if (action == "close") AuxCall(owner, "Close", false);
                    var names = (List<string>)Field(owner, "familyNames");
                    pages = names.GetRange(0, Mathf.Min(4, names.Count)).ToArray();
                    page = names.IndexOf((string)Field(owner, "family")); preview = true;
                    ((AvControl)Field(owner, "applyButton")).Interactable = false;
                    break;
                case "weather":
                    if (action == "open" || action == "close")
                        Modules.Weather.Runtime.WeatherAutomation.Console(new Dictionary<string, object> { ["action"] = action });
                    owner = Object.FindObjectOfType<Modules.Weather.Presentation.MetOfficeWindow>();
                    if (owner == null) return AuxAbsent(id, action);
                    var classes = (AvControl[])Field(owner, "classButtons");
                    if (action == "page") AuxPress(classes[Mathf.Clamp(requested, 0, classes.Length - 1)].GetComponentInChildren<AvHit>());
                    object picker = Field(owner, "picker");
                    page = Convert.ToInt32(picker.GetType().GetProperty("Class").GetValue(picker));
                    pages = Modules.Weather.Domain.MetCatalog.ClassWords;
                    break;
                case "drone":
                    if (action == "open" || action == "close")
                        BoscaliSummer.Vanguard.VanguardAutomation.Step(new Dictionary<string, object> { ["action"] = "link", ["do"] = action == "open" ? "terminal" : "close" });
                    owner = Object.FindObjectOfType<Modules.Vanguard.Presentation.DroneTerminalWindow>();
                    if (owner == null) return AuxAbsent(id, action);
                    break;
                default: return Failure("ScrollAutomation", "Auxiliary", "unknown auxiliary window");
            }
            AvWindow window = Field(owner, "window") as AvWindow;
            if (window == null) return Failure("ScrollAutomation", "Auxiliary", "owner has no window");
            var corners = new Vector3[4]; window.Root.GetWorldCorners(corners);
            Canvas canvas = window.Root.GetComponentInParent<Canvas>(); Camera camera = MapUiPointer.UiCamera(canvas);
            bool fits = true;
            foreach (Vector3 corner in corners)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                fits &= point.x >= -1f && point.y >= -1f && point.x <= Screen.width + 1f && point.y <= Screen.height + 1f;
            }
            return new Dictionary<string, object> { ["ok"] = true, ["window"] = id, ["visible"] = window.Visible && Rendered(window.Root),
                ["page"] = page, ["pageName"] = page >= 0 && page < pages.Length ? pages[page] : "", ["pages"] = pages,
                ["pageCount"] = pages.Length, ["fits"] = fits, ["readOnlyPreview"] = preview };
        }

        private static Dictionary<string, object> AuxAbsent(string id, string action) => action == "close" || action == "read"
            ? new Dictionary<string, object> { ["ok"] = true, ["window"] = id, ["visible"] = false }
            : Failure("ScrollAutomation", "Auxiliary", "window unavailable: " + id);
        private static object AuxCall(object owner, string method, params object[] args) =>
            owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(owner, args);
        private static void AuxPress(AvHit hit)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            hit.OnPointerDown(data); hit.OnPointerUp(data); hit.OnPointerClick(data);
        }
    }
}
#endif
