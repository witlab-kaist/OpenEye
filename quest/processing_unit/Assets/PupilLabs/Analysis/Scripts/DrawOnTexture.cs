using System.Collections;
using UnityEngine;

namespace PupilLabs.Analysis
{
    [RequireComponent(typeof(Renderer))]
    public class DrawOnTexture : MonoBehaviour
    {
        [Tooltip("A lookup texture that ramps from [0,1] in UV co-ordinates in attention values")]
        [SerializeField]
        private Texture2D heatmapLookUpTable;

        [Tooltip("The brush size or spread from the eye gaze point")]
        [SerializeField]
        private float drawBrushSize = 2000.0f;

        [Tooltip("The intensity or amplitude of the brush that falls off from the eye gaze point")]
        [SerializeField]
        private float drawIntensity = 15.0f;

        [Tooltip("The minimum threshold value to apply colors to the heat map")]
        [SerializeField]
        private float minThresholdDeltaHeatMap = 0.001f; // Mostly for performance to reduce spreading heatmap for small values.

        // The internal texture reference we will modify.
        // Bound to the renderer on this GameObject.
        private Texture2D texture;

        private void Awake()
        {
            SetupTexture();
        }

        public void OnHit(RaycastHit hit)
        {
            StartCoroutine(DrawAt(hit.textureCoord));
        }

        private void SetupTexture()
        {
            Renderer rendererComponent = GetComponent<Renderer>();

            // Create new texture and bind it to renderer/material.
            texture = new Texture2D(2048, 1024, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;

            for (int ix = 0; ix < texture.width; ix++)
            {
                for (int iy = 0; iy < texture.height; iy++)
                {
                    texture.SetPixel(ix, iy, Color.clear);
                }
            }
            texture.Apply(false);

            rendererComponent.material.SetTexture("_MainTex", texture);
        }

        protected void OnDestroy()
        {
            Destroy(texture);
        }

        private IEnumerator DrawAt(Vector2 posUV)
        {
            // Assign colors
            yield return null;

            StartCoroutine(ComputeHeatmapAt(posUV, true, true));

            StartCoroutine(ComputeHeatmapAt(posUV, true, false));

            StartCoroutine(ComputeHeatmapAt(posUV, false, true));

            StartCoroutine(ComputeHeatmapAt(posUV, false, false));
        }

        private IEnumerator ComputeHeatmapAt(Vector2 currPosUV, bool positiveX, bool positiveY)
        {
            yield return null;

            // Determine the center of our to be drawn 'blob'
            var center = new Vector2(currPosUV.x * texture.width, currPosUV.y * texture.height);
            int signX = positiveX ? 1 : -1;
            int signY = positiveY ? 1 : -1;
            int startX = positiveX ? 0 : 1;
            int startY = positiveY ? 0 : 1;

            for (int dx = startX; dx < texture.width; dx++)
            {
                float tx = currPosUV.x * texture.width + dx * signX;
                if ((tx < 0) || (tx >= texture.width))
                    break;

                for (int dy = startY; dy < texture.height; dy++)
                {
                    float ty = currPosUV.y * texture.height + dy * signY;
                    if ((ty < 0) || (ty >= texture.height))
                        break;

                    if (ComputeHeatmapColorAt(new Vector2(tx, ty), center, out Color? newColor))
                    {
                        if (newColor.HasValue)
                        {
                            texture.SetPixel((int)tx, (int)ty, newColor.Value);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            texture.Apply(false);
        }

        private bool ComputeHeatmapColorAt(Vector2 currentPoint, Vector2 originalPivot, out Color? col)
        {
            col = null;

            float spread = drawBrushSize;
            float amplitude = drawIntensity;
            float distCenterToCurrPnt = Vector2.Distance(originalPivot, currentPoint) / spread;

            float B = 2f;
            float scaledInterest = 1f / (1f + Mathf.Pow(Mathf.Epsilon, -(B * distCenterToCurrPnt)));
            float delta = scaledInterest / amplitude;
            if (delta < minThresholdDeltaHeatMap)
                return false;

            Color baseColor = texture.GetPixel((int)currentPoint.x, (int)currentPoint.y);
            float normalizedInterest = Mathf.Clamp01(baseColor.a + delta);

            // Get color from given heatmap ramp
            if (heatmapLookUpTable != null)
            {
                col = heatmapLookUpTable.GetPixel((int)(normalizedInterest * (heatmapLookUpTable.width - 1)), 0);
                col = new Color(col.Value.r, col.Value.g, col.Value.b, normalizedInterest);
            }
            else
            {
                col = Color.blue;
                col = new Color(col.Value.r, col.Value.g, col.Value.b, normalizedInterest);
            }

            return true;
        }
    }
}
