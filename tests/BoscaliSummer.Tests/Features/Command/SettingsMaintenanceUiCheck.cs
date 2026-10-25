#if UNITY_EDITOR
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;
using static UnityCheckHarness;

public static partial class SettingsUnityCheck
{
    private static void CheckMaintenance(Camera camera, Canvas canvas, int height, SettingsMfdPanel panel,
        AvConsole pilot, AvConsole server, CommandSettings settings)
    {
        ShowMode(panel, pilot, server, false);
        pilot.SetPage(CPerf);
        Settle(pilot);
        RectTransform content = pilot.Page(CPerf).Content;
        ConfigFile config = settings.ExpandedMapUi.ConfigFile;
        string owned = Path.Combine(Paths.CachePath, "BoscaliSummer");
        Directory.CreateDirectory(owned);
        string cached = Path.Combine(owned, "maintenance-check.bin"), sibling = Path.Combine(Paths.CachePath, "other-mod.bin");
        File.WriteAllText(cached, "generated");
        File.WriteAllText(sibling, "keep");
        RenderCacheMaintenance.Calls = 0;
        RenderCacheMaintenance.Error = null;
        GameAccess.ForceServer = false;
        Settle(pilot);
        Check(!FindControl(content, "FACTORY RESET").Interactable && FindControl(content, "CLEAR CACHE").Interactable,
            "remote players can clear local cache, but cannot reset a config overlaid by host values");
        settings.DeckOpacity.Value = .25f;
        Click(FindControl(content, "FACTORY RESET"));
        Check(settings.DeckOpacity.Value == .25f, "locked factory button cannot reset settings");

        Click(FindControl(content, "CLEAR CACHE"));
        Settle(pilot);
        Check(File.Exists(cached) && RenderCacheMaintenance.Calls == 0, "first cache click only asks for confirmation");
        RenderMaintenance(camera, canvas, height, pilot, "cache-confirm");
        Click(FindControl(content, "CANCEL"));
        Check(File.Exists(cached) && RenderCacheMaintenance.Calls == 0, "cache cancellation changes nothing");
        Click(FindControl(content, "CLEAR CACHE"));
        pilot.SetPage(CDisplay); pilot.SetPage(CPerf); Settle(pilot);
        Check(!FindControl(content, "CONFIRM CLEAR").gameObject.activeInHierarchy,
            "leaving the page cancels a pending confirmation");
        Click(FindControl(content, "CLEAR CACHE"));
        ShowMode(panel, pilot, server, true); ShowMode(panel, pilot, server, false);
        Check(!FindControl(content, "CONFIRM CLEAR").gameObject.activeInHierarchy,
            "switching audience cancels a pending confirmation");
        Click(FindControl(content, "CLEAR CACHE"));
        Click(FindControl(content, "CONFIRM CLEAR"));
        Settle(pilot);
        Check(!File.Exists(cached) && File.Exists(sibling) && RenderCacheMaintenance.Calls == 1,
            "confirmed cache clear reaches its owner once and preserves sibling files");
        Check(settings.DeckOpacity.Value == .25f && HasSubText(content, "Cache cleared:"),
            "cache clear preserves settings and shows its result");

        GameAccess.ForceServer = true;
        Settle(pilot);
        ConfigEntry<string> saved = config.Bind("Spooktober", "Progress", ""); saved.Value = "keep-career";
        config.Save();
        string before = File.ReadAllText(config.ConfigFilePath);
        Click(FindControl(content, "FACTORY RESET"));
        Settle(pilot);
        Check(settings.DeckOpacity.Value == .25f && File.ReadAllText(config.ConfigFilePath) == before,
            "first factory click has no settings or disk side effect");
        RenderMaintenance(camera, canvas, height, pilot, "factory-confirm");
        Click(FindControl(content, "CANCEL"));
        Check(settings.DeckOpacity.Value == .25f, "factory cancellation keeps the current settings");

        Click(FindControl(content, "FACTORY RESET"));
        GameAccess.ForceServer = false; // Authority can change after arming, before the next refresh.
        Click(FindControl(content, "CONFIRM RESET"));
        Check(settings.DeckOpacity.Value == .25f && HasSubText(content, "Factory reset locked"),
            "factory confirmation rechecks authority at execution");
        GameAccess.ForceServer = true; Settle(pilot);
        Click(FindControl(content, "FACTORY RESET"));
        Click(FindControl(content, "CONFIRM RESET"));
        Settle(pilot);
        Check(settings.DeckOpacity.Value == (float)settings.DeckOpacity.DefaultValue && saved.Value == "keep-career",
            "confirmed factory action restores defaults but keeps saved player content");
        Check(File.Exists(config.ConfigFilePath + ".factory-reset.bak") && HasSubText(content, "Restart the game"),
            "factory action backs up the config and gives persistent restart feedback");
        RenderMaintenance(camera, canvas, height, pilot, "factory-result");

        RenderCacheMaintenance.Error = "Renderer cache unavailable.";
        Click(FindControl(content, "CLEAR CACHE"));
        Click(FindControl(content, "CONFIRM CLEAR"));
        Settle(pilot);
        Check(HasSubText(content, "Cache partly cleared:") && HasSubText(content, "Renderer cache unavailable."),
            "partial maintenance failure is visible and does not report full success");
        RenderCacheMaintenance.Error = null;
        GameAccess.ForceServer = false;
    }

    private static void RenderMaintenance(Camera camera, Canvas canvas, int height, AvConsole pilot, string state)
    {
        Settle(pilot);
        RectTransform content = pilot.Page(CPerf).Content;
        ScrollRect scroll = pilot.Root.GetComponentInChildren<ScrollRect>(true);
        RectTransform section = (RectTransform)content.Find("Section MAINTENANCE");
        float travel = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
        float top = section != null ? -section.anchoredPosition.y : 0f;
        scroll.verticalNormalizedPosition = travel <= 0f ? 1f : 1f - Mathf.Clamp01(top / travel);
        Render(camera, canvas, height, "maintenance-" + state);
        scroll.verticalNormalizedPosition = 1f;
    }
}
#endif
