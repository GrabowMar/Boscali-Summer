#if UNITY_EDITOR
using System;
using UnityEngine;

public static partial class HudLogUnityCheck
{
    private static void VanguardGauges()
    {
        var registry = Type("BoscaliSummer.Core.Contracts.ScopeGauges");
        var feeds = Type("BoscaliSummer.Modules.Vanguard.Presentation.VanguardHudFeeds");
        registry.GetMethod("Clear", All).Invoke(null, null);
        var host = new GameObject("VanguardGaugeCheck");
        var component = host.AddComponent(feeds);
        feeds.GetMethod("Awake", All).Invoke(component, null);
        try
        {
            feeds.GetMethod("DeclareChannel", All).Invoke(null, new object[] { "vg.kestrel", "KESTREL" });
            var acquire = feeds.GetMethod("Acquire", All);
            object line = acquire.Invoke(null, new object[] { null, "vanguard.kestrel", "vg.kestrel", "test" });
            Check(line != null && ReferenceEquals(line, acquire.Invoke(null, new object[] { null, "vanguard.kestrel", "vg.kestrel", "test" })), "A repeated status tick reuses its gauge handle");
            line.GetType().GetMethod("Set", All).Invoke(line, new object[] { Tone("Caution"), "KESTREL ORBIT", "LASING AGM-48", 0.5f });
            object[] read = { 0, null };
            Check((bool)registry.GetMethod("TryRead", All).Invoke(null, read), "A Vanguard status actually reaches the TAC BOARD gauge registry");
            object spec = read[1];
            Check((string)spec.GetType().GetField("Text", All).GetValue(spec) == "ORBIT", "The gauge carries the live state, with its channel name in the label");
            Check((string)spec.GetType().GetField("Sub", All).GetValue(spec) == "LASING AGM-48", "The target detail is retained");
            line.GetType().GetMethod("Release", All).Invoke(line, null);
            read[1] = null;
            Check(!(bool)registry.GetMethod("TryRead", All).Invoke(null, read), "A released weapon status disappears");
            feeds.GetMethod("ResetForScene", All).Invoke(component, null);
            Check((int)registry.GetProperty("Count", All).GetValue(null) == 0, "Scene reset unregisters owned Vanguard gauges");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); registry.GetMethod("Clear", All).Invoke(null, null); }
    }
}
#endif
