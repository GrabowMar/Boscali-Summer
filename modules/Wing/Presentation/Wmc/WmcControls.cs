using NOAvionics;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The panel's control registry (WMC automation: <see cref="WmcPanel.Press"/>, the <c>Wmc</c> scenario's <c>press</c>):
    /// a control id maps to the kit v2 part that answers it, or to an action. Pressing a part clicks it as a pointer would (through
    /// its <see cref="AvHit"/>: a disabled control ignores the click, a hidden one is not pressed). Tab pages register with
    /// <see cref="Add(string, AvControl)"/> / <see cref="Add(string, AvPart)"/> / <see cref="Add(string, Action, Func{bool})"/>.</summary>
    internal sealed class WmcControls
    {
        private sealed class Entry
        {
            public Transform Host;
            public Action Act;
            public Func<bool> Shown;
            public AvHit Hit;
        }

        private readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>(256);

        public int Count => map.Count;

        /// <summary>A kit v2 button answers <paramref name="id"/>.</summary>
        public void Add(string id, AvControl control) => Set(id, control != null ? control.transform : null);

        /// <summary>A kit v2 part with a click target (an <see cref="AvRow"/> made with a click, an <see cref="AvCell"/>, a list
        /// row) answers <paramref name="id"/>.</summary>
        public void Add(string id, AvPart part) => Set(id, part?.Rect);

        /// <summary>Anything that hosts an <see cref="AvHit"/> in its children answers <paramref name="id"/>.</summary>
        public void Add(string id, Transform host) => Set(id, host);

        /// <summary><paramref name="press"/> answers <paramref name="id"/> while <paramref name="shown"/> (null: always).</summary>
        public void Add(string id, Action press, Func<bool> shown = null)
        {
            if (string.IsNullOrEmpty(id) || press == null) return;
            map[id] = new Entry { Act = press, Shown = shown };
        }

        /// <summary>Drops <paramref name="id"/> only while <paramref name="control"/> still answers it (a swapped cell's old id).</summary>
        public void Release(string id, AvControl control)
        {
            if (string.IsNullOrEmpty(id) || control == null) return;
            if (map.TryGetValue(id, out Entry e) && e.Host == control.transform) map.Remove(id);
        }

        public bool Has(string id) => id != null && map.ContainsKey(id);

        public void Clear()
        {
            map.Clear();
        }

        /// <summary>Press <paramref name="id"/> as a left click would. False when nothing answers it or it is hidden; a disabled
        /// control takes the click and ignores it.</summary>
        public bool Press(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (map.TryGetValue(id, out Entry e))
            {
                if (e.Act != null)
                {
                    if (e.Shown != null && !e.Shown()) return false;
                    e.Act();
                    return true;
                }
                if (e.Host == null || !e.Host.gameObject.activeInHierarchy) return false;
                if (e.Hit == null) e.Hit = e.Host.GetComponentInChildren<AvHit>(true);
                if (e.Hit == null) return false;
                e.Hit.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                return true;
            }
            return false;
        }

        private void Set(string id, Transform host)
        {
            if (string.IsNullOrEmpty(id) || host == null) return;
            map[id] = new Entry { Host = host };
        }
    }
}
