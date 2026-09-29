#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Real production OPS builders on kit v2 (<see cref="AvConsole"/>), deterministic display data;
/// no game or network simulation. Each named "page" is one of the two STATUS/ACTIONS sub-flows a
/// domain's <c>BuildXPage(AvFlow)</c> builds together — both are always built, only the requested
/// sub is activated/inspected, matching what the console actually shows on screen.
/// </summary>
public static partial class SupportPanelUnityCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private static readonly string[] Pages = { "Station", "SpaceOps", "Status", "CyberOps", "SpecStatus", "SpecActions" };
    private static int assertions;
    // Every ability name shown by an ACTIONS page, by page name: CheckActionForms proves each ability has exactly one home.
    private static readonly Dictionary<string, HashSet<string>> ActionNamesByPage = new Dictionary<string, HashSet<string>>();
    private static object overlaySupport;

    /// <summary>Which domain (0/1/2) and sub (0 STATUS / 1 ACTIONS) a page name maps to, and the
    /// build method / OpsSubPage field that domain's shell uses.</summary>
    private static void PageInfo(string page, out int tab, out int sub, out string buildMethod, out string subField)
    {
        string domain = page == "Station" || page == "SpaceOps" ? "SPACE"
            : page == "Status" || page == "CyberOps" ? "CYBER" : "SPEC OPS";
        tab = domain == "SPACE" ? 0 : domain == "CYBER" ? 1 : 2;
        sub = page == "SpaceOps" || page == "CyberOps" || page == "SpecActions" ? 1 : 0;
        buildMethod = domain == "SPACE" ? "BuildSpacePage" : domain == "CYBER" ? "BuildCyberPage" : "BuildSpecOpsPage";
        subField = domain == "SPACE" ? "spacePage" : domain == "CYBER" ? "cyberPage" : "specPage";
    }

    /// <summary>Every ability surface prints the cost and readiness words <c>AbilityStatus</c> decided.</summary>
    private static void CheckFacts(object panel, string page)
    {
        Type status = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz.AbilityStatus", true);
        MethodInfo facts = status.GetMethod("For", BindingFlags.Static | BindingFlags.Public);
        object support = Get(panel, "support");
        if (page == "SpaceOps")
            foreach (object row in (IEnumerable)Get(panel, "spaceStrikeRows"))
            {
                object action = Get(row, "Action");
                object avRow = Get(row, "Row");
                string readiness = ((TMP_Text)Field(avRow, "sub")).text;
                string cost = ((TMP_Text)Field(avRow, "value")).text;
                Check(!string.IsNullOrEmpty(readiness), "M3: a SPACE row must show readiness.");
                object f = facts.Invoke(null, new[] { support, action, (object)false });
                Check(readiness == (string)Field(f, "Readiness"), "SPACE readiness must be AbilityStatus's: " + readiness);
                string price = (string)Field(f, "CostText");
                Check(cost == price, "SPACE cost must be AbilityStatus's: " + cost);
                object button = Get(row, "Button");
                bool enabled = (bool)Property(button, "Interactable");
                Check(enabled == (bool)Field(f, "Enabled"), "The row's trailing control must follow AbilityStatus.");
            }
        if (page == "CyberOps")
            foreach (object row in (IEnumerable)Get(panel, "cyberAbilityRows"))
            {
                object avRow = Get(row, "Row");
                object f = facts.Invoke(null, new[] { support, Get(row, "Action"), (object)false });
                string sub = ((TMP_Text)Field(avRow, "sub")).text;
                Check(sub.StartsWith((string)Field(f, "Readiness"), StringComparison.Ordinal), "CYBER readiness must lead with AbilityStatus's.");
                Check(((TMP_Text)Field(avRow, "value")).text == (string)Field(f, "CostText"), "CYBER cost must be AbilityStatus's.");
            }
        if (page == "SpecActions")
            foreach (object row in (IEnumerable)Get(panel, "actionRows"))
            {
                object avRow = Get(row, "View");
                object f = facts.Invoke(null, new[] { support, Get(row, "Definition"), (object)false });
                Check(((TMP_Text)Field(avRow, "value")).text == (string)Field(f, "CostText"), "SPEC OPS cost must be AbilityStatus's.");
            }
    }

    private static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Hidden);
        return field != null ? field.GetValue(target) : Property(target, name);
    }

    /// <summary>Quantised image rects: two pages that share a form share most of these.</summary>
    private static HashSet<string> Signature(RectTransform pageRoot)
    {
        var set = new HashSet<string>();
        var corners = new Vector3[4];
        foreach (Image image in pageRoot.GetComponentsInChildren<Image>(false))
        {
            image.rectTransform.GetWorldCorners(corners);
            set.Add(Mathf.RoundToInt(corners[1].x / 8f) + ":" + Mathf.RoundToInt(corners[1].y / 8f) + ":" +
                    Mathf.RoundToInt((corners[2].x - corners[1].x) / 8f) + ":" + Mathf.RoundToInt((corners[1].y - corners[0].y) / 8f));
        }
        return set;
    }

    /// <summary>
    /// The ACTIONS pages are one shared row vocabulary (spec 6.2), so their forms are not compared: what
    /// must differ, and be right, is the content. For each ACTIONS page: every row names its ability, the
    /// trailing control's enabled/armed state and label follow <c>AbilityStatus</c>, and the row carries
    /// hover help that leads with the ability's name (the footer line the player reads on hover). The
    /// domain sub-page's STATUS / ACTIONS tabs carry their help too.
    /// </summary>
    private static void CheckActionRows(object panel, string page)
    {
        if (page != "SpaceOps" && page != "CyberOps" && page != "SpecActions") return;
        Type status = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz.AbilityStatus", true);
        MethodInfo facts = status.GetMethod("For", BindingFlags.Static | BindingFlags.Public);
        object support = Get(panel, "support");
        PageInfo(page, out _, out _, out _, out string subField);
        object subPage = Get(panel, subField);
        foreach (string tab in new[] { "statusTab", "actionsTab" })
            Check(!string.IsNullOrEmpty((string)Property(Field(subPage, tab), "Help")), page + ": the " + tab + " must carry hover help.");

        var rows = new List<(object action, object row, object button, bool orbital)>();
        if (page == "SpaceOps")
            foreach (object r in (IEnumerable)Get(panel, "spaceStrikeRows")) rows.Add((Get(r, "Action"), Get(r, "Row"), Get(r, "Button"), true));
        else if (page == "CyberOps")
            foreach (object r in (IEnumerable)Get(panel, "cyberAbilityRows")) rows.Add((Get(r, "Action"), Get(r, "Row"), Get(r, "Button"), false));
        else
            foreach (object r in (IEnumerable)Get(panel, "actionRows"))
                if ((int)Get(r, "Tab") == 2) rows.Add((Get(r, "Definition"), Get(r, "View"), Get(r, "Trailing"), false));
        Check(rows.Count > 0, page + ": the ACTIONS page must list abilities.");

        var names = new HashSet<string>();
        foreach ((object action, object avRow, object button, bool orbital) in rows)
        {
            string name = (string)Field(action, "Name");
            string shown = ((TMP_Text)Field(avRow, "name")).text;
            Check(orbital ? shown.StartsWith(name, StringComparison.Ordinal) : shown == name, page + ": row must name '" + name + "', shows '" + shown + "'");
            Check(names.Add(name), page + ": ability '" + name + "' is listed twice.");
            object f = facts.Invoke(null, new[] { support, action, (object)false });
            Check((bool)Property(button, "Interactable") == (bool)Field(f, "Enabled"), page + ": '" + name + "' enabled state must follow AbilityStatus.");
            bool armed = (bool)Field(f, "Armed");
            Check((string)Property(button, "Label") == (armed ? "ABORT" : "ARM"), page + ": '" + name + "' verb must be ARM, or ABORT while armed.");
            string help = (string)Property(button, "Help");
            Check(!string.IsNullOrEmpty(help) && help.StartsWith(name, StringComparison.Ordinal), page + ": '" + name + "' must carry hover help that leads with its name: " + help);
            var tip = ((Component)Field(avRow, "frame")).GetComponent<AvHelpTip>();
            Check(tip != null && tip.Text == help, page + ": '" + name + "' row body must share the control's hover help.");
        }
        if (page == "SpaceOps")
        {
            Check(((string)Property(Get(panel, "spaceUplinkOpen"), "Help") ?? "").Contains("sensor feed"), "The uplink OPEN control must carry its help.");
            Check(((string)Property(Get(panel, "spaceUplinkAim"), "Help") ?? "").Contains("Right-click the map"), "The uplink AIM control must carry its help.");
            Check(((string)Property(Get(panel, "spaceRephaseOpen"), "Help") ?? "").Contains("Relocation uses"), "The relocation OPEN control must carry its help.");
        }
        ActionNamesByPage[page] = names;
    }

    /// <summary>Across the three ACTIONS pages: every catalogue ability has exactly one home page.</summary>
    private static void CheckActionForms()
    {
        Check(ActionNamesByPage.Count == 3, "All three ACTIONS pages must have been checked.");
        var seen = new HashSet<string>();
        foreach (KeyValuePair<string, HashSet<string>> page in ActionNamesByPage)
            foreach (string name in page.Value)
                Check(seen.Add(name), "Ability '" + name + "' is listed on more than one ACTIONS page.");
        int catalogued = 0;
        foreach (object action in (IEnumerable)Property(overlaySupport, "Actions")) catalogued++;
        Check(seen.Count == catalogued, "Every catalogue ability needs a home ACTIONS page: " + seen.Count + " of " + catalogued);
    }

    /// <summary>A real station fitted through the production model: the RECON loadout launched
    /// step by step and ticked to hard dock, so the truss and the console show a station the
    /// player could actually have built.</summary>
    private static object PlatformFixture(out double now)
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        Type missionsType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformMissions", true);
        Type missionType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformMission", true);
        Type modulesType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformModules", true);
        object recon = Enum.Parse(missionType, "Recon");
        object platform = Activator.CreateInstance(platformType);
        MethodInfo next = missionsType.GetMethod("Next", BindingFlags.Static | BindingFlags.Public);
        MethodInfo price = modulesType.GetMethod("LaunchPrice", BindingFlags.Static | BindingFlags.Public);
        const double insertion = 45.0, dock = 20.0;

        now = 100.0;
        for (int launched = 0; launched < 8; launched++)
        {
            object step = next.Invoke(null, new[] { platform, recon, now });
            if ((bool)Property(step, "Complete")) break;
            object module = step.GetType().GetField("Module", Hidden).GetValue(step);
            var cell = (int)step.GetType().GetField("Cell", Hidden).GetValue(step);
            object failure = Invoke(platform, "TryLaunch", module, cell, (byte)0, 42,
                now, (float)price.Invoke(null, new[] { module }), insertion, dock, 0UL);
            Check(failure.ToString() == "None", "The station fixture must launch " + module + ": " + failure);
            now += dock + insertion;
            Call(platform, "Tick", now, 0.01f, true);
        }
        Check((bool)Property(platform, "Exists"), "The station fixture must reach orbit.");
        // Settle on a pass so the banner, tiles and console strip all read live figures.
        object state = Invoke(platform, "State", now);
        if (!(bool)Property(state, "InPass"))
        {
            now += (double)state.GetType().GetField("TimeToPass", Hidden).GetValue(state) + 0.5;
            Call(platform, "Tick", now, 0.01f, true);
        }
        return platform;
    }

    private static int OrbitalCoreCell()
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        return (int)platformType.GetField("CoreCell", BindingFlags.Static | BindingFlags.Public).GetRawConstantValue();
    }

    /// <summary>The first cell the fixture station can still dock to, so the console's inspector and
    /// launch card show a real free cell rather than a refusal.</summary>
    private static int FreeCell(object platform)
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        var cells = (int)platformType.GetField("CellCount", BindingFlags.Static | BindingFlags.Public).GetRawConstantValue();
        for (int cell = 0; cell < cells; cell++)
            if ((bool)Invoke(platform, "CanAttach", cell)) return cell;
        return OrbitalCoreCell();
    }

    /// <summary>A real CyberNetwork fixture — the home backbone, cities in and out of reach, one
    /// real location with one live access lease — driven through the production model
    /// and console painter, so the offline render shows the board the player actually sees.</summary>
    private static object CyberFixture(out int held, out double clock)
    {
        Type networkType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.CyberNetwork", true);
        Type nodeKindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.NodeKind", true);
        Type locationKindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.LocationKind", true);
        object network = Activator.CreateInstance(networkType);
        clock = 60.0;
        Call(network, "PlaceStatic", 1, Enum.Parse(nodeKindType, "Command"), 0f, 0f, clock);
        Call(network, "PlaceStatic", 2, Enum.Parse(nodeKindType, "Base"), -14000f, 7000f, clock);
        Call(network, "PlaceStatic", 3, Enum.Parse(nodeKindType, "Base"), 11000f, -9000f, clock);
        object city = Enum.Parse(locationKindType, "City");
        object airfield = Enum.Parse(locationKindType, "Airfield");
        held = (int)Invoke(network, "ReportLocation", 11, city, 16000f, 8000f, clock);
        int reachable = (int)Invoke(network, "ReportLocation", 12, airfield, -12000f, -16000f, clock);
        Invoke(network, "ReportLocation", 13, city, 52000f, 38000f, clock);
        Check(held >= 0 && reachable >= 0, "The CYBER fixture must place its hackable locations.");

        // Host time: accrue resources, complete one quiet city operation, and choose its optional
        // payload so the fixture exercises the live 75-second, one-effect lease.
        for (int i = 0; i < 1200; i++) Call(network, "Tick", clock += 0.25, 0.25f, 1f);
        Check(Convert.ToInt32(Invoke(network, "TryStartBreach", held, true, clock)) == 0,
            "The fixture must start a first real-site operation.");
        for (int i = 0; i < 600 && (bool)Property(network, "BreachActive"); i++)
            Call(network, "Tick", clock += 0.25, 0.25f, 1f);
        Check((int)Invoke(network, "Stage", held) == 3 && (float)Invoke(network, "AccessRemaining", clock) > 70f,
            "One completed operation must leave one live lease.");
        Type capstoneType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.Capstone", true);
        Invoke(network, "TryChooseCapstone", Enum.Parse(capstoneType, "Reveal"), clock);
        Check((int)Property(network, "AccessSlot") == held, "The lease site should remain the active target.");
        return network;
    }

    /// <summary>A real detachment through the production model: nine objectives across the
    /// theatre, CHARLIE raised, ALPHA's recon succeeded and holds an observation post over a
    /// scouted town, BRAVO en route to sabotage an air-defence site, CHARLIE on task seizing an
    /// airfield, DELTA still unformed.</summary>
    private static object DetachmentFixture(out double now)
    {
        Type detachmentType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.SpecOpsDetachment", true);
        Type kindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.ObjectiveKind", true);
        Type missionType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.FieldMission", true);
        object detachment = Activator.CreateInstance(detachmentType);
        Call(detachment, "BeginObjectives");
        var fixture = new (string Kind, int Anchor, float X, float Z, int Threat, int Radars, bool Hostile, string Name)[]
        {
            ("Town", 101, 14000f, 6000f, 5, 0, true, "KERSEY"),
            ("AirDefence", 102, 26000f, -4000f, 4, 3, true, "AIR DEFENCE 26/-4"),
            ("Airfield", 103, 21000f, 15000f, 7, 1, true, "NORTH RIDGE AIRFIELD"),
            ("Outpost", 104, -9000f, 22000f, 0, 0, false, "LIGHTHOUSE POINT"),
            ("Town", 105, 34000f, 9000f, 11, 2, true, "PORT SAINT MARIE"),
            ("Airfield", 106, 41000f, -18000f, 9, 2, true, "SOUTHERN AIRBASE"),
            ("Town", 107, 5000f, -21000f, 1, 0, false, "VALE"),
            ("Outpost", 108, 30000f, 27000f, 3, 0, true, "HILL 402"),
            ("AirDefence", 109, 47000f, 4000f, 6, 4, true, "AIR DEFENCE 47/4")
        };
        foreach (var o in fixture)
            Call(detachment, "ReportObjective", Enum.Parse(kindType, o.Kind), o.Anchor, o.X, o.Z, o.Threat, o.Radars, o.Hostile, o.Name, false);
        Call(detachment, "EndObjectives", 0.0);
        Func<double> lucky = () => 0.0;
        Check((bool)Invoke(detachment, "TryRaise", 2), "The fixture must raise CHARLIE.");
        Invoke(detachment, "TryLaunch", 0, Enum.Parse(missionType, "Recon"), 101, 12000f, 0.0,
            0f, 0f, "MARIS AIRPORT");
        Call(detachment, "Tick", 50.0, lucky, null);
        Type directiveType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.SpecOpsDirective", true);
        Check((bool)Invoke(detachment, "TryDirective", 0, Enum.Parse(directiveType, "Execute"), 50.0),
            "ALPHA must execute from its arrival decision window.");
        Call(detachment, "Tick", 90.0, lucky, null);
        Invoke(detachment, "TryLaunch", 1, Enum.Parse(missionType, "Sabotage"), 102, 60000f, 90.0,
            0f, 0f, "MARIS AIRPORT");
        Invoke(detachment, "TryLaunch", 2, Enum.Parse(missionType, "Seize"), 103, 8000f, 90.0,
            0f, 0f, "MARIS AIRPORT");
        now = 140.0;
        Call(detachment, "Tick", now, lucky, null);
        Check(Property(detachment, "Formed").Equals(3), "The fixture must field three teams.");
        object charlie = Invoke(detachment, "Team", 2);
        Check(Convert.ToInt32(Get(charlie, "State")) == Convert.ToInt32(Enum.Parse(
            Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.TeamState", true), "Deciding")),
            "CHARLIE remains visible at the arrival decision stage.");
        return detachment;
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static object Property(object target, string property) =>
        target.GetType().GetProperty(property, Hidden).GetValue(target);


    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }

    private static void Capture(GameObject canvas, float height, string file, float width = 480f)
    {
        Canvas.ForceUpdateCanvases();
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = AvStyleHost.FuiColor("ground", Color.black);
        camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture((int)width * 2, (int)height * 2, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
    }
}
#endif
