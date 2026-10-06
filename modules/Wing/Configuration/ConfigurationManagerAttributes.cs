using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
#pragma warning disable 0649/// <summary>Optional ConfigurationManager display metadata, discovered by class and field names without
/// an assembly dependency. Includes only the fields this plugin uses.</summary>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Display name override for the settings entry.</summary>
    public string DispName;

    /// <summary>Sort priority within a category; higher first, default 0.</summary>
    public int? Order;

    /// <summary>Show only in advanced settings or search results.</summary>
    public bool? IsAdvanced;

    /// <summary>Control visibility of internal persistence entries.</summary>
    public bool? Browsable;

    /// <summary>Custom IMGUI value drawer, including action buttons. Its exact ConfigEntryBase delegate
    /// signature is required by ConfigurationManager's reflection lookup.</summary>
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;

    /// <summary>Hide the irrelevant reset button on action entries.</summary>
    public bool? HideDefaultButton;
}
