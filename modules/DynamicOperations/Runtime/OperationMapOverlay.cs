using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    internal sealed class OperationMapOverlay : MonoBehaviour, ISceneService
    {
        private OperationsManager manager;
        private GameObject root;
        private DynamicMap map;
        private readonly RectTransform[] pins = new RectTransform[3];
        private readonly TMP_Text[] labels = new TMP_Text[3];

        internal void Configure(OperationsManager owner) => manager = owner;
        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; map = null;
        }
        private void OnDestroy() => ResetForScene();
        private void Update()
        {
            if (!DynamicMap.mapMaximized)
            { if (root != null) root.SetActive(false); return; }
            DynamicMap current = SceneSingleton<DynamicMap>.i;
            if (current == null || current.mapImage == null || manager == null) return;
            manager.Refresh();
            if (map != current) ResetForScene();
            map = current;
            if (root == null)
            {
                root = new GameObject("BoscaliSummer.ContractMarkers", typeof(RectTransform));
                root.transform.SetParent(map.mapImage.transform, false);
                for (int i = 0; i < pins.Length; i++)
                {
                    pins[i] = new GameObject("Contract", typeof(RectTransform)).GetComponent<RectTransform>();
                    pins[i].SetParent(root.transform, false);
                    AvKit.Rule(pins[i], new Rect(-8f, 1f, 16f, 2f), AvTheme.RailInfo).raycastTarget = false;
                    AvKit.Rule(pins[i], new Rect(-1f, 8f, 2f, 16f), AvTheme.RailInfo).raycastTarget = false;
                    labels[i] = AvStyled.Label(pins[i], new Rect(12f, 12f, 220f, 38f), "", "row-main");
                    labels[i].fontSize = 14f;
                    labels[i].richText = false; labels[i].raycastTarget = false;
                }
            }
            root.SetActive(true);
            for (int i = 0; i < pins.Length; i++)
            {
                SecondaryObjectiveView card = i < manager.Objectives.Count ? manager.Objectives[i] : null;
                bool visible = card != null && card.IsActive && card.HasMarker;
                pins[i].gameObject.SetActive(visible);
                if (!visible) continue;
                pins[i].localPosition = new Vector3(card.X * map.mapDisplayFactor, card.Z * map.mapDisplayFactor, 0f);
                pins[i].localScale = Vector3.one / Mathf.Max(0.001f, map.mapImage.transform.localScale.x);
                labels[i].text = "MIS " + card.Id + "  " + card.Title + (card.Radius > 0f ? "\nAREA " + card.Radius.ToString("0") + " m" : "");
            }
        }
    }
}
