using UnityEngine;

namespace PP.WorldGeneration
{
    [RequireComponent(typeof(PlateLayerWorldGenerator))]
    public class PlateLayerDebugRenderer : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private bool drawPlates = true;
        [SerializeField] private bool drawPeaks = true;
        [SerializeField] private bool drawMountains = true;
        [SerializeField] private bool drawMountainChunks = true;

        [Header("Colors")]
        [SerializeField] private Color seaColor = new Color(0.1f, 0.35f, 0.55f, 0.25f);
        [SerializeField] private Color landColor = new Color(0.45f, 0.65f, 0.35f, 0.35f);
        [SerializeField] private Color plateBorderColor = new Color(1f, 1f, 1f, 0.2f);
        [SerializeField] private Color peakColor = new Color(1f, 0.9f, 0.2f, 1f);
        [SerializeField] private Color mountainLineColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color mountainChunkColor = new Color(0.15f, 0.15f, 0.15f, 0.35f);

        [Header("Size")]
        [SerializeField] private float peakRadius = 0.35f;

        [Header("Height Display")]
        [SerializeField] private bool drawChunkHeights = true;

        [SerializeField] private bool drawOnlyLandChunkHeights = true;

        [SerializeField] private float heightViewAlpha = 0.45f;

        [SerializeField] private int visualMaxHeight = 12;

        private PlateLayerWorldGenerator generator;

        private void OnValidate()
        {
            generator = GetComponent<PlateLayerWorldGenerator>();
        }

        private void OnDrawGizmos()
        {
            if (generator == null)
            {
                generator = GetComponent<PlateLayerWorldGenerator>();
            }

            if (generator == null || generator.Plates == null)
            {
                return;
            }

            if (drawPlates)
            {
                DrawPlates();
            }

            if (drawChunkHeights)
            {
                DrawChunkHeights();
            }

            if (drawMountainChunks)
            {
                DrawMountainChunks();
            }

            if (drawMountains)
            {
                DrawMountainLines();
            }

            if (drawPeaks)
            {
                DrawPeaks();
            }
        }

        private void DrawPlates()
        {
            Vector2 plateSize = generator.PlateToWorldSize();

            for (int x = 0; x < generator.PlateWidth; x++)
            {
                for (int y = 0; y < generator.PlateHeight; y++)
                {
                    PlateLayerData plate = generator.Plates[x, y];

                    Vector2 min = generator.PlateToWorldMin(plate.plateCoord);
                    Vector3 center = new Vector3(
                        min.x + plateSize.x * 0.5f,
                        min.y + plateSize.y * 0.5f,
                        0f
                    );

                    Vector3 size = new Vector3(plateSize.x, plateSize.y, 0.01f);

                    Gizmos.color = plate.IsLand ? landColor : seaColor;
                    Gizmos.DrawCube(center, size);

                    Gizmos.color = plateBorderColor;
                    Gizmos.DrawWireCube(center, size);
                }
            }
        }

        private void DrawPeaks()
        {
            foreach (PlateLayerData plate in generator.LandPlates)
            {
                Gizmos.color = peakColor;
                Vector3 pos = new Vector3(plate.peakWorldPos.x, plate.peakWorldPos.y, -0.1f);
                Gizmos.DrawSphere(pos, peakRadius);
            }
        }

        private void DrawMountainLines()
        {
            foreach (MountainConnection connection in generator.MountainConnections)
            {
                Vector3 a = new Vector3(connection.a.peakWorldPos.x, connection.a.peakWorldPos.y, -0.2f);
                Vector3 b = new Vector3(connection.b.peakWorldPos.x, connection.b.peakWorldPos.y, -0.2f);

                Gizmos.color = mountainLineColor;
                Gizmos.DrawLine(a, b);
            }
        }

        private void DrawMountainChunks()
        {
            foreach (MountainConnection connection in generator.MountainConnections)
            {
                foreach (Vector2Int chunk in connection.mountainChunks)
                {
                    Vector2 world = generator.ChunkToWorldCenter(chunk);

                    Vector3 center = new Vector3(world.x, world.y, -0.15f);
                    Vector3 size = Vector3.one * generator.ChunkWorldSize;

                    Gizmos.color = mountainChunkColor;
                    Gizmos.DrawCube(center, size);
                }
            }
        }

        private void DrawChunkHeights()
        {
            ChunkTerrainData[,] chunks = generator.Chunks;

            if (chunks == null)
            {
                return;
            }

            for (int x = 0; x < generator.ChunkWidth; x++)
            {
                for (int y = 0; y < generator.ChunkHeight; y++)
                {
                    ChunkTerrainData chunk = chunks[x, y];

                    if (drawOnlyLandChunkHeights && !chunk.isLand)
                    {
                        continue;
                    }

                    float t = visualMaxHeight <= 0
                        ? 0f
                        : Mathf.Clamp01(chunk.height / (float)visualMaxHeight);

                    // ’á‚¢‚Ù‚Ç”–‚¢—ÎA‚‚¢‚Ù‚ÇŠDFŠñ‚è
                    Color low = new Color(0.45f, 0.65f, 0.35f, heightViewAlpha);
                    Color high = new Color(0.35f, 0.35f, 0.35f, heightViewAlpha);

                    Color color = Color.Lerp(low, high, t);

                    if (chunk.isPeak)
                    {
                        color = new Color(1f, 0.85f, 0.2f, heightViewAlpha);
                    }
                    else if (chunk.isMountain)
                    {
                        color = new Color(0.2f, 0.2f, 0.2f, heightViewAlpha);
                    }

                    Vector2 world = generator.ChunkToWorldCenter(chunk.chunkCoord);

                    Gizmos.color = color;
                    Gizmos.DrawCube(
                        new Vector3(world.x, world.y, -0.12f),
                        Vector3.one * generator.ChunkWorldSize
                    );
                }
            }
        }
    }
}