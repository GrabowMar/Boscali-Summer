using System;
using System.Collections.Generic;
using BepInEx.Logging;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Installs one mod screen on the vanilla MFD: the once-a-second install attempt, the
    /// claim (<see cref="MfdBezel"/>) or appended host slot (<see cref="MfdScreenHost"/>),
    /// the root rect cloned from a stock screen, the bezel label and highlight, the bind, and
    /// the teardown. A panel keeps its own pages and refresh; it only supplies the build
    /// callback that fills the root.
    /// </summary>
    internal sealed class MfdPanelInstaller
    {
        /// <summary>
        /// Builds the panel content under <paramref name="host"/> (the screen root, or its
        /// Content wrapper when <see cref="Wrap"/> is set) and returns the rect vanilla shows
        /// and hides with the bezel button; null means the host itself.
        /// </summary>
        public delegate RectTransform BuildFn(RectTransform host, float height);

        private readonly string id;
        private readonly string tag;
        private readonly string rootName;
        private readonly bool preferLeft;
        private float nextAttempt;

        public MfdPanelInstaller(string id, string tag, string rootName, bool preferLeft)
        {
            this.id = id;
            this.tag = tag;
            this.rootName = rootName;
            this.preferLeft = preferLeft;
        }

        /// <summary>Appended host slot instead of one of the six free vanilla buttons.</summary>
        public bool Host;

        /// <summary>Mount the content under a full-size "Content" child of the root.</summary>
        public bool Wrap;

        /// <summary>Keep the root inside the canvas. A screen that never clamped keeps it off.</summary>
        public bool Clamp = true;

        /// <summary>An invisible raycast plate on the root, so clicks never reach the map beneath.</summary>
        public bool Plate;

        /// <summary>The cockpit-facing <c>MFDScreen.shortName</c> when it is not the claim id.</summary>
        public string ShortName;

        /// <summary>Fills the screen root; set once (a method group converted per frame would allocate).</summary>
        public BuildFn Builder;

        public ManualLogSource Log;
        public MFDScreen Screen;
        /// <summary>The claimed bezel button; pressing it shows <see cref="Screen"/> (automation).</summary>
        public Button Bezel;
        public GameObject Root;
        public bool Failed;

        /// <summary>
        /// The panel's per-frame gate: false while it failed, or the MFD is unavailable (which
        /// fails it), or the screen is not installed yet. An install is attempted at most
        /// once a second, and the frame that attempts it returns false.
        /// </summary>
        public bool Tick()
        {
            if (Failed) return false;
            if (Application.isBatchMode || !GameAccess.MfdAvailable) { Failed = true; return false; }
            if (Screen != null) return true;
            if (Time.unscaledTime < nextAttempt) return false;
            nextAttempt = Time.unscaledTime + 1f;
            Install();
            return false;
        }

        public void Install()
        {
            try
            {
                VirtualMFD mfd =
                    SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                List<Button> buttons;
                List<MFDScreen> screens;
                int slot;
                bool left;
                bool claimed = Host
                    ? MfdScreenHost.TryHost(id, preferLeft, mfd, out buttons, out screens, out slot, out left)
                    : MfdBezel.TryClaim(id, preferLeft, mfd, out buttons, out screens, out slot, out left);
                if (!claimed)
                {
                    // A host slot only fails on a missing adapter; a claim fails on a crowded bezel.
                    Failed = true;
                    Log?.LogWarning(tag + " MFD unavailable: " + (Host ? "could not add a host button." : "no free bezel slot."));
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    Release();
                    return;
                }

                Screen = Build(template, buttons[slot]);
                Bezel = buttons[slot];
                if (Screen == null)
                {
                    Reset();
                    Failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, Screen))
                {
                    Reset();
                    Failed = true;
                    Log?.LogWarning(tag + " MFD unavailable: bezel changed during installation.");
                    return;
                }

                Log?.LogInfo(tag + " MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Reset();
                Failed = true;
                Log?.LogError(tag + " MFD install failed: " + e);
            }
        }

        /// <summary>The screen for <paramref name="bezel"/>, or null when the bezel has no label or highlight.</summary>
        public MFDScreen Build(MFDScreen template, Button bezel)
        {
            TextMeshProUGUI label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            Image highlight = FindHighlight(bezel);
            if (label == null || highlight == null) return null;

            RectTransform rootRect = MakeRoot(template, rootName, Clamp, Plate, out GameObject root, out float height);
            Root = root;

            RectTransform host = rootRect;
            if (Wrap)
            {
                var wrapper = new GameObject("Content", typeof(RectTransform));
                host = (RectTransform)wrapper.transform;
                host.SetParent(rootRect, false);
                AvLay.Fill(host);
            }

            RectTransform display = Builder(host, height) ?? host;
            MFDScreen screen = root.AddComponent<MFDScreen>();
            screen.shortName = ShortName ?? id;
            screen.displayPanel = display.gameObject;
            screen.aircraftOnly = false;
            screen.label = label;
            screen.highlight = highlight;
            return screen;
        }

        /// <summary>Drop the slot reservation, the screen root and every reference; the next Tick may install again.</summary>
        public void Reset()
        {
            Release();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            Screen = null;
            Bezel = null;
            Failed = false;
            nextAttempt = 0f;
        }

        private void Release()
        {
            if (Host) MfdScreenHost.Release(id);
            else MfdBezel.Release(id);
        }

        /// <summary>
        /// A screen root in the stock screen's bay: the template's anchors, pivot and scale, the
        /// height the bay gives it, and the kit panel width. Position is deliberately not
        /// copied: <c>MFDScreen.ShowScreen</c> assigns <c>VirtualMFD.showPos</c> (zero) straight
        /// to localPosition, so a screen is placed by its parent and anchors.
        /// </summary>
        public static RectTransform MakeRoot(
            MFDScreen template, string name, bool clamp, bool plate,
            out GameObject root, out float height)
        {
            root = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = templateRect.pivot;
            rect.localScale = templateRect.localScale;

            height = AvLay.ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            if (clamp) AvLay.ClampIntoCanvas(rect);

            if (plate)
            {
                Image blocker = root.AddComponent<Image>();
                blocker.color = Color.clear;
                blocker.raycastTarget = true;
            }
            return rect;
        }

        /// <summary>The bezel button's highlight image: the first child image, else the button's own.</summary>
        public static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null && images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }
    }
}
