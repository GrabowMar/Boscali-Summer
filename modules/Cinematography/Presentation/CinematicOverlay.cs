using BoscaliSummer.Modules.Cinematography.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Cinematography.Presentation
{
    internal sealed class CinematicOverlay : MonoBehaviour
    {
        private Image top,bottom,fade;
        private TMP_Text title;
        internal static CinematicOverlay Create(Transform owner)
        {
            var go=new GameObject("BoscaliSummer.CinematicOverlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            go.transform.SetParent(owner,false);
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30006;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var overlay=go.AddComponent<CinematicOverlay>();var root=(RectTransform)go.transform;
            overlay.top=AvLay.Solid(root,"LetterboxTop",Color.black);AvLay.Fill(overlay.top.rectTransform);
            overlay.bottom=AvLay.Solid(root,"LetterboxBottom",Color.black);AvLay.Fill(overlay.bottom.rectTransform);
            overlay.title=AvText.Make(root,"CinematicTitle",AvTextRole.Head,"",TextAlignmentOptions.Center);
            overlay.title.richText=false;overlay.title.raycastTarget=false;
            AvText.Size(overlay.title,28);AvText.Fit(overlay.title,true);overlay.title.fontSizeMin=16;
            var t=overlay.title.rectTransform;t.anchorMin=new Vector2(0,0);t.anchorMax=new Vector2(1,0);
            t.offsetMin=new Vector2(48,18);t.offsetMax=new Vector2(-48,90);
            overlay.fade=AvLay.Solid(root,"CinematicFade",Color.clear);AvLay.Fill(overlay.fade.rectTransform);
            overlay.top.raycastTarget=overlay.bottom.raycastTarget=overlay.fade.raycastTarget=false;
            return overlay;
        }
        internal void Paint(ShotPlan plan,float seconds)
        {
            var o=plan.options;float bars=o?.letterbox ?? 0;
            top.rectTransform.anchorMin=new Vector2(0,1-bars);top.rectTransform.anchorMax=Vector2.one;
            bottom.rectTransform.anchorMin=Vector2.zero;bottom.rectTransform.anchorMax=new Vector2(1,bars);
            top.enabled=bottom.enabled=bars>0;
            AvText.Set(title,o?.title ?? "");fade.color=new Color(0,0,0,o?.Fade(seconds,plan.duration) ?? 0);
        }
        internal void Release() { gameObject.SetActive(false);Destroy(gameObject); }
    }
}
