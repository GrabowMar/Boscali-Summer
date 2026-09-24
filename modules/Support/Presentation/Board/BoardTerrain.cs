using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Board
{
    /// <summary>Borrow the loaded map's terrain sprite; never duplicate or own its texture.
    /// World corners use the same projection as markers, so zoom and pan cannot drift.</summary>
    internal sealed class BoardTerrain
    {
        private readonly BoardSurface board;
        private readonly Image image;
        private Vector2 size;
        private float nextResolve;
        public bool Available => image != null && image.sprite != null;

        public BoardTerrain(RectTransform parent, BoardSurface board)
        {
            this.board = board;
            image = AvKit.Panel(parent, new Rect(0f, 0f, 1f, 1f), Color.white);
            image.name = "TheatreTerrain";
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.enabled = false;
        }

        public void Refresh()
        {
            if (Time.unscaledTime >= nextResolve)
            {
                nextResolve = Time.unscaledTime + 1f;
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                MapSettings settings = level != null ? level.LoadedMapSettings : null;
                if (settings != null) SetSource(settings.MapImage, settings.MapSize);
            }
            if (!Available) return;
            Vector2 a = board.Project(-size.x * 0.5f, size.y * 0.5f);
            Vector2 b = board.Project(size.x * 0.5f, -size.y * 0.5f);
            AvKit.Place(image.rectTransform, new Rect(a.x, a.y, b.x - a.x, a.y - b.y));
        }

        // Also used by the offline harness with a local image from the installed game.
        internal void SetSource(Sprite sprite, Vector2 metres)
        {
            if (sprite == null || metres.x <= 0f || metres.y <= 0f ||
                float.IsNaN(metres.x) || float.IsNaN(metres.y) ||
                float.IsInfinity(metres.x) || float.IsInfinity(metres.y)) return;
            image.sprite = sprite;
            image.enabled = true;
            size = metres;
            board.SetMapHalf(Mathf.Max(size.x, size.y) * 0.5f);
        }
    }
}
