using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    internal sealed class MinimapFogOverlay : MonoBehaviour
    {
        private const int Size = 256;
        private static readonly AccessTools.FieldRef<WorldMapFogOfWar, Color[]> FogColors =
            AccessTools.FieldRefAccess<WorldMapFogOfWar, Color[]>("_colors");
        private static readonly AccessTools.FieldRef<WorldMapFogOfWar, Vector3[]> FogVertices =
            AccessTools.FieldRefAccess<WorldMapFogOfWar, Vector3[]>("_vertices");

        private readonly Color32[] pixels = new Color32[Size * Size];
        private Texture2D texture;
        private Vector3 lastCenter;
        private float lastZoom;
        private float nextRefresh;

        internal void Refresh(Camera camera, IEnumerable<WorldMapFogOfWar> tiles)
        {
            if (texture != null && camera.transform.position == lastCenter &&
                camera.orthographicSize == lastZoom && Time.unscaledTime < nextRefresh)
                return;

            if (texture == null)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                {
                    name = "Minimap exploration mask",
                    wrapMode = TextureWrapMode.Clamp
                };
                GetComponent<RawImage>().texture = texture;
            }

            lastCenter = camera.transform.position;
            lastZoom = camera.orthographicSize;
            nextRefresh = Time.unscaledTime + 0.25f;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(218, 234, 245, 0);
            foreach (var tile in tiles)
                if (tile != null && tile.gameObject.activeInHierarchy)
                    Rasterize(camera, tile);
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        internal void Invalidate() => nextRefresh = 0f;

        private void Rasterize(Camera camera, WorldMapFogOfWar tile)
        {
            var vertices = FogVertices(tile);
            var colors = FogColors(tile);
            int columns = tile.GridSizeX;
            int rows = tile.GridSizeZ;
            if (vertices == null || colors == null || columns <= 0 || rows <= 0)
                return;

            // Project the same grid used by the fullscreen map into the minimap viewport.
            var origin = (Vector2)camera.WorldToViewportPoint(tile.transform.TransformPoint(vertices[0]));
            var right = (Vector2)camera.WorldToViewportPoint(tile.transform.TransformPoint(vertices[columns])) - origin;
            var up = (Vector2)camera.WorldToViewportPoint(tile.transform.TransformPoint(vertices[rows * (columns + 1)])) - origin;
            float determinant = right.x * up.y - right.y * up.x;
            if (Mathf.Abs(determinant) < 0.000001f)
                return;

            var far = origin + right + up;
            var rightCorner = origin + right;
            var upperCorner = origin + up;
            int minX = PixelFloor(Mathf.Min(Mathf.Min(origin.x, far.x), Mathf.Min(rightCorner.x, upperCorner.x)));
            int maxX = PixelCeil(Mathf.Max(Mathf.Max(origin.x, far.x), Mathf.Max(rightCorner.x, upperCorner.x)));
            int minY = PixelFloor(Mathf.Min(Mathf.Min(origin.y, far.y), Mathf.Min(rightCorner.y, upperCorner.y)));
            int maxY = PixelCeil(Mathf.Max(Mathf.Max(origin.y, far.y), Mathf.Max(rightCorner.y, upperCorner.y)));
            int stride = columns + 1;

            for (int y = minY; y < maxY; y++)
            {
                float dy = (y + 0.5f) / Size - origin.y;
                for (int x = minX; x < maxX; x++)
                {
                    float dx = (x + 0.5f) / Size - origin.x;
                    float u = (dx * up.y - dy * up.x) / determinant;
                    float v = (dy * right.x - dx * right.y) / determinant;
                    if (u < 0f || u > 1f || v < 0f || v > 1f)
                        continue;

                    float gridX = u * columns;
                    float gridY = v * rows;
                    int column = Mathf.Min((int)gridX, columns - 1);
                    int row = Mathf.Min((int)gridY, rows - 1);
                    float fx = gridX - column;
                    float fy = gridY - row;
                    int i = row * stride + column;
                    float alpha = fx + fy <= 1f
                        ? colors[i].a * (1f - fx - fy) + colors[i + 1].a * fx + colors[i + stride].a * fy
                        : colors[i + stride + 1].a * (fx + fy - 1f) +
                            colors[i + stride].a * (1f - fx) + colors[i + 1].a * (1f - fy);
                    byte opacity = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha * 2f) * 255f);
                    int pixel = y * Size + x;
                    if (opacity > pixels[pixel].a)
                        pixels[pixel] = new Color32(218, 234, 245, opacity);
                }
            }
        }

        private static int PixelFloor(float value) => Mathf.Clamp(Mathf.FloorToInt(value * Size), 0, Size);
        private static int PixelCeil(float value) => Mathf.Clamp(Mathf.CeilToInt(value * Size), 0, Size);

        private void OnDestroy()
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
