using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Autopilot.Configuration;
using BoscaliSummer.Features.Autopilot.Domain;
using BoscaliSummer.Features.Autopilot.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Autopilot.Presentation
{
    /// <summary>
    /// UGUI presentation overlay for the ACE3 radial interaction menu.
    /// Renders dynamic root rings, blossoming child branches, connecting stem lines,
    /// and the signature rotating segmented selector reticle.
    /// </summary>
    internal sealed class AceRadialMenuUi : MonoBehaviour, ISceneService
    {
        private const int MaxVisualNodes = 32;

        private static AceRadialMenuUi instance;
        public static AceRadialMenuUi Instance => instance;

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RectTransform canvasRect;

        private GameObject centerReticle;
        private GameObject selectorReticle;
        private RectTransform selectorRect;

        private readonly List<NodeWidget> widgetPool = new List<NodeWidget>();
        private readonly AceRadialMenuTree tree = new AceRadialMenuTree();
        private AutopilotSettings settings;

        private bool isOpen;
        private Vector2 menuCenterScreen;
        private Vector2 virtualCursor;
        private Aircraft currentAircraft;
        private bool wasCursorVisible;
        private CursorLockMode prevLockMode;
        private float keyDownTime;
        private bool isHoldMode;

        public bool IsOpen => isOpen;

        public void Configure(AutopilotSettings autopilotSettings)
        {
            settings = autopilotSettings;
        }

        private struct NodeWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Background;
            public Text LabelText;
            public Image BranchIndicator;
        }

        public void ResetForScene()
        {
            CloseMenu();
            tree.ClearRootActions();
        }

        private void Awake()
        {
            instance = this;
            BuildCanvas();
            PopulateDefaultActions();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void BuildCanvas()
        {
            GameObject canvasGo = new GameObject("AceRadialMenuCanvas");
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;

            canvasRect = canvasGo.GetComponent<RectTransform>();

            // Center anchor reticle
            centerReticle = new GameObject("CenterReticle", typeof(RectTransform), typeof(Image));
            centerReticle.transform.SetParent(canvasRect, false);
            var centerImg = centerReticle.GetComponent<Image>();
            centerImg.color = new Color(0.2f, 0.9f, 0.5f, 0.6f);
            var centerRect = centerReticle.GetComponent<RectTransform>();
            centerRect.sizeDelta = new Vector2(8f, 8f);

            // Rotating selector reticle
            selectorReticle = new GameObject("SelectorReticle", typeof(RectTransform), typeof(Image));
            selectorReticle.transform.SetParent(canvasRect, false);
            var selImg = selectorReticle.GetComponent<Image>();
            selImg.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            selectorRect = selectorReticle.GetComponent<RectTransform>();
            selectorRect.sizeDelta = new Vector2(56f, 56f);
            selectorReticle.SetActive(false);

            // Pre-allocate node widget pool
            for (int i = 0; i < MaxVisualNodes; i++)
            {
                widgetPool.Add(CreateNodeWidget(i));
            }
        }

        private NodeWidget CreateNodeWidget(int index)
        {
            GameObject nodeGo = new GameObject($"NodeWidget_{index}", typeof(RectTransform), typeof(Image));
            nodeGo.transform.SetParent(canvasRect, false);
            var rt = nodeGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 32f);

            var bg = nodeGo.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.1f, 0.85f);

            // Label text
            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(nodeGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            var txt = textGo.GetComponent<Text>();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 13;
            txt.color = new Color(0.4f, 0.95f, 0.6f, 1f);
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Branch indicator ('>' dot)
            GameObject branchGo = new GameObject("BranchDot", typeof(RectTransform), typeof(Image));
            branchGo.transform.SetParent(nodeGo.transform, false);
            var branchRt = branchGo.GetComponent<RectTransform>();
            branchRt.anchorMin = new Vector2(1f, 0.5f);
            branchRt.anchorMax = new Vector2(1f, 0.5f);
            branchRt.anchoredPosition = new Vector2(-6f, 0f);
            branchRt.sizeDelta = new Vector2(6f, 6f);
            var branchImg = branchGo.GetComponent<Image>();
            branchImg.color = new Color(0.3f, 0.85f, 0.5f, 0.7f);

            nodeGo.SetActive(false);

            return new NodeWidget
            {
                Root = nodeGo,
                Rect = rt,
                Background = bg,
                LabelText = txt,
                BranchIndicator = branchImg
            };
        }

        public void PopulateDefaultActions()
        {
            tree.ClearRootActions();

            // 1. Autopilot Branch
            var autopilotBranch = new AceRadialAction("autopilot", "AUTOPILOT");
            autopilotBranch.AddChild(new AceRadialAction(
                "autopilot.land",
                "LAND",
                _ => AutopilotLandController.Instance?.Toggle(),
                candidate =>
                {
                    AutopilotLandController ctrl = AutopilotLandController.Instance;
                    return ctrl != null && (ctrl.IsEngaged ? ctrl.IsEngagedOn(candidate) : ctrl.CanEngage(candidate));
                }
            ));
            autopilotBranch.AddChild(new AceRadialAction(
                "autopilot.cancel",
                "CANCEL",
                _ => AutopilotLandController.Instance?.Toggle(),
                _ => AutopilotLandController.Instance != null && AutopilotLandController.Instance.IsEngaged
            ));
            tree.AddRootAction(autopilotBranch);

            // 2. Command Target Presets (via IRadialMenuPage)
            if (ModServices.TryGet(out IRadialMenuPage targetPresets))
            {
                AceRadialAction pageAction = AceRadialMenuTree.FromRadialPage(targetPresets);
                if (pageAction != null) tree.AddRootAction(pageAction);
            }

            // 3. Quick Wing / Squad Action (if available)
            var squadBranch = new AceRadialAction("squad", "SQUAD");
            squadBranch.AddChild(new AceRadialAction("squad.formup", "FORM UP", _ => { }));
            squadBranch.AddChild(new AceRadialAction("squad.engage", "ENGAGE", _ => { }));
            squadBranch.AddChild(new AceRadialAction("squad.rtb", "RTB", _ => { }));
            tree.AddRootAction(squadBranch);
        }

        public void OpenMenu(Vector2? screenPosition = null)
        {
            if (isOpen) return;

            if (GameManager.GetLocalAircraft(out Aircraft ac))
            {
                currentAircraft = ac;
            }

            wasCursorVisible = Cursor.visible;
            prevLockMode = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            menuCenterScreen = screenPosition ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            virtualCursor = (Vector2)Input.mousePosition;
            tree.ResetNavigation();
            isOpen = true;
            canvasGroup.alpha = 1f;

            centerReticle.GetComponent<RectTransform>().position = menuCenterScreen;
        }

        public void CloseMenu()
        {
            if (!isOpen) return;

            Cursor.visible = wasCursorVisible;
            Cursor.lockState = prevLockMode;

            isOpen = false;
            canvasGroup.alpha = 0f;
            selectorReticle.SetActive(false);
            for (int i = 0; i < widgetPool.Count; i++)
            {
                widgetPool[i].Root.SetActive(false);
            }
            tree.ResetNavigation();
        }

        private void Update()
        {
            KeyCode hotkey = settings?.AceRadialKey?.Value ?? KeyCode.C;

            if (!isOpen)
            {
                if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey))
                {
                    if (GameplayUI.GameIsPaused || DynamicMap.mapMaximized) return;
                    if (UnityEngine.EventSystems.EventSystem.current != null &&
                        UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null) return;
                    if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null) return;

                    currentAircraft = ac;
                    keyDownTime = Time.unscaledTime;
                    isHoldMode = true;
                    OpenMenu();
                }
                return;
            }

            // Close if paused or ESC pressed
            if (GameplayUI.GameIsPaused || Input.GetKeyDown(KeyCode.Escape))
            {
                CloseMenu();
                return;
            }

            // Track mouse cursor in screen overlay coordinates
            Vector2 mouseScreen = Input.mousePosition;
            virtualCursor = mouseScreen;

            // Update menu tree physics, angles, hit-testing, and layout
            tree.Update(virtualCursor, menuCenterScreen, currentAircraft, Time.unscaledTime, Time.unscaledDeltaTime);

            // Handle hotkey release (Classic ACE3 hold-and-release to execute)
            if (hotkey != KeyCode.None && Input.GetKeyUp(hotkey))
            {
                if (tree.HoveredIndex >= 0)
                {
                    tree.TriggerSelected(currentAircraft);
                    CloseMenu();
                    return;
                }

                // If held deliberately (> 0.25s) and released with nothing selected, close
                if (Time.unscaledTime - keyDownTime > 0.25f)
                {
                    CloseMenu();
                    return;
                }

                // Quick tap (< 0.25s): stay open in sticky / toggle mode
                isHoldMode = false;
            }

            // In toggle mode, pressing hotkey again closes the menu
            if (!isHoldMode && hotkey != KeyCode.None && Input.GetKeyDown(hotkey))
            {
                CloseMenu();
                return;
            }

            // Left mouse click: execute or expand hovered node, or close if clicked far outside
            if (Input.GetMouseButtonDown(0))
            {
                if (tree.HoveredIndex >= 0)
                {
                    bool executed = tree.TriggerSelected(currentAircraft);
                    if (executed)
                    {
                        CloseMenu();
                        return;
                    }
                }
                else
                {
                    float dist = Vector2.Distance(virtualCursor, menuCenterScreen);
                    if (dist > 180f)
                    {
                        CloseMenu();
                        return;
                    }
                }
            }

            // Right mouse click: back up one level or close
            if (Input.GetMouseButtonDown(1))
            {
                if (tree.IsBranchExpanded)
                {
                    tree.ResetNavigation();
                }
                else
                {
                    CloseMenu();
                    return;
                }
            }

            // Render active layout nodes to UGUI widgets
            IReadOnlyList<AceRadialNodeLayout> nodes = tree.CurrentLayoutNodes;
            int count = Mathf.Min(nodes.Count, widgetPool.Count);

            for (int i = 0; i < count; i++)
            {
                AceRadialNodeLayout node = nodes[i];
                NodeWidget widget = widgetPool[i];

                widget.Root.SetActive(true);
                widget.Rect.position = new Vector2(node.Position.X, node.Position.Y);
                widget.Rect.localScale = Vector3.one * node.Scale;

                widget.LabelText.text = node.DisplayName.ToUpperInvariant();
                widget.BranchIndicator.gameObject.SetActive(node.IsBranch);

                bool isHovered = (i == tree.HoveredIndex);

                if (!node.IsAllowed)
                {
                    widget.Background.color = new Color(0.12f, 0.08f, 0.08f, 0.45f);
                    widget.LabelText.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                }
                else if (isHovered)
                {
                    widget.Background.color = new Color(0.15f, 0.35f, 0.22f, 0.95f);
                    widget.LabelText.color = new Color(0.95f, 1f, 0.85f, 1f);
                }
                else
                {
                    widget.Background.color = new Color(0.08f, 0.12f, 0.1f, 0.85f);
                    widget.LabelText.color = new Color(0.4f, 0.95f, 0.6f, 1f);
                }
            }

            // Hide unused widgets
            for (int i = count; i < widgetPool.Count; i++)
            {
                widgetPool[i].Root.SetActive(false);
            }

            // Render signature ACE3 rotating selector reticle over hovered node
            if (tree.HoveredIndex >= 0 && tree.HoveredIndex < nodes.Count)
            {
                selectorReticle.SetActive(true);
                selectorRect.position = new Vector2(nodes[tree.HoveredIndex].Position.X, nodes[tree.HoveredIndex].Position.Y);
                selectorRect.localEulerAngles = new Vector3(0f, 0f, tree.SelectorRotationDeg);
            }
            else
            {
                selectorReticle.SetActive(false);
            }
        }
    }
}
