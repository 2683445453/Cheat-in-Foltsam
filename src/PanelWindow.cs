using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    internal sealed class PanelGrip : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        internal RectTransform Window;
        internal RectTransform Canvas;
        internal bool Resize;
        private Vector2 startPointer;
        private Vector2 startPosition;
        private float startScale;

        public void OnBeginDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Canvas, eventData.pressPosition, null, out startPointer);
            startPosition = Window.anchoredPosition;
            startScale = Window.localScale.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Canvas, eventData.position, null, out var pointer))
                return;

            var delta = pointer - startPointer;
            if (Resize)
            {
                var diagonal = new Vector2(Window.rect.width, -Window.rect.height);
                float scale = startScale + Vector2.Dot(delta, diagonal) / diagonal.sqrMagnitude;
                SetBounds(Window, Canvas, startPosition, scale);
            }
            else
            {
                SetBounds(Window, Canvas, startPosition + delta, startScale);
            }
        }

        internal static void SetBounds(RectTransform window, RectTransform canvas, Vector2 position, float scale)
        {
            var area = canvas.rect.size;
            var size = window.rect.size;
            if (area.x <= 0f || area.y <= 0f)
                return;

            float maximum = Mathf.Min(1.65f, Mathf.Min((area.x - 20f) / size.x, (area.y - 20f) / size.y));
            maximum = Mathf.Max(0.1f, maximum);
            scale = Mathf.Clamp(scale, Mathf.Min(0.70f, maximum), maximum);
            window.localScale = new Vector3(scale, scale, 1f);
            window.anchoredPosition = new Vector2(
                Mathf.Clamp(position.x, 8f, Mathf.Max(8f, area.x - size.x * scale - 8f)),
                -Mathf.Clamp(-position.y, 8f, Mathf.Max(8f, area.y - size.y * scale - 8f)));
        }
    }

    public sealed partial class TrainerPanel
    {
        private readonly List<Object> windowArt = new List<Object>();
        private Sprite frameArt;
        private Sprite controlArt;
        private Sprite shadowArt;
        private RectTransform window;
        private Vector2 lastCanvasSize;

        private void CreateWindowArt()
        {
            frameArt = CreateRoundedSprite(16, 2, false);
            controlArt = CreateRoundedSprite(8, 1, false);
            shadowArt = CreateRoundedSprite(16, 0, true);
        }

        private Sprite CreateRoundedSprite(int radius, int stroke, bool shadow)
        {
            const int width = 64;
            int padding = shadow ? 8 : 0;
            float half = width / 2f - padding;
            var pixels = new Color32[width * width];
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - width / 2f) - (half - radius);
                    float dy = Mathf.Abs(y + 0.5f - width / 2f) - (half - radius);
                    float distance = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude +
                        Mathf.Min(Mathf.Max(dx, dy), 0) - radius;
                    float coverage = shadow ? 1f - Mathf.SmoothStep(0f, 1f, (distance + 3f) / 10f) :
                        Mathf.Clamp01(0.5f - distance);
                    byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(78f, 255f,
                        Mathf.Clamp01(0.5f - distance - stroke)));
                    pixels[y * width + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(255 * coverage));
                }
            }

            var texture = new Texture2D(width, width, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            int border = radius + padding + 1;
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, width), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            windowArt.Add(sprite);
            windowArt.Add(texture);
            return sprite;
        }

        private static void UseSlicedArt(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        private void AddGrip(Image image, bool resize)
        {
            var grip = image.gameObject.AddComponent<PanelGrip>();
            grip.Window = window;
            grip.Canvas = (RectTransform)transform;
            grip.Resize = resize;
        }

        private void KeepWindowOnScreen()
        {
            var canvas = (RectTransform)transform;
            if (canvas.rect.size == lastCanvasSize)
                return;
            lastCanvasSize = canvas.rect.size;
            PanelGrip.SetBounds(window, canvas, window.anchoredPosition, window.localScale.x);
            if (minimap != null)
                PanelGrip.SetBounds(minimap, canvas, minimap.anchoredPosition, minimap.localScale.x);
        }
    }
}
