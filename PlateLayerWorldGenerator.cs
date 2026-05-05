using System;
using System.Collections.Generic;
using UnityEngine;

namespace PP.WorldGeneration
{
    public enum PlateBiome
    {
        Sea,
        Land
    }

    [Serializable]
    public class ChunkTerrainData
    {
        public Vector2Int chunkCoord;

        public bool isLand;
        public bool isPeak;
        public bool isMountain;

        // 0 = 海
        // 1以上 = 陸
        public int height = -1;

        public bool HasHeight => height >= 0;
    }

    [Serializable]
    public class PlateLayerData
    {
        public Vector2Int plateCoord;
        public PlateBiome biome = PlateBiome.Sea;

        // プレート内に置かれる山頂チャンク座標
        public Vector2Int peakChunkCoord;

        // 山頂のワールド座標
        public Vector2 peakWorldPos;

        // 接続している山脈
        public readonly List<MountainConnection> connections = new();

        public bool IsLand => biome == PlateBiome.Land;
        public int ConnectionCount => connections.Count;
    }

    [Serializable]
    public class MountainConnection
    {
        public PlateLayerData a;
        public PlateLayerData b;

        // この山脈が通るチャンク座標
        public List<Vector2Int> mountainChunks = new();

        public MountainConnection(PlateLayerData a, PlateLayerData b)
        {
            this.a = a;
            this.b = b;
        }

        public bool Contains(PlateLayerData p)
        {
            return a == p || b == p;
        }

        public PlateLayerData Other(PlateLayerData p)
        {
            if (a == p) return b;
            if (b == p) return a;
            return null;
        }
    }

    public class PlateLayerWorldGenerator : MonoBehaviour
    {
        [Header("World Size")]
        [SerializeField] private int plateWidth = 30;
        [SerializeField] private int plateHeight = 20;

        [Tooltip("1プレートレイヤーに含まれるチャンク数。10なら10×10チャンク。")]
        [SerializeField] private int chunksPerPlate = 10;

        [Tooltip("1チャンクのワールド上のサイズ。今は見た目確認用。")]
        [SerializeField] private float chunkWorldSize = 1f;

        [Header("Seed")]
        [SerializeField] private int seed = 12345;
        [SerializeField] private bool randomSeedOnPlay = false;

        [Header("Primary Plate")]
        [Range(0f, 1f)]
        [SerializeField] private float primaryLandChance = 0.10f;

        [Header("Secondary Plate")]
        [Tooltip("四方が海の海プレートが陸になる確率。")]
        [Range(0f, 1f)]
        [SerializeField] private float isolatedSeaToLandChance = 0.15f;

        [Tooltip("上下に陸がある海プレートが陸になる確率。")]
        [Range(0f, 1f)]
        [SerializeField] private float verticalBridgeLandChance = 0.45f;

        [Tooltip("左右に陸がある海プレートが陸になる確率。")]
        [Range(0f, 1f)]
        [SerializeField] private float horizontalBridgeLandChance = 0.45f;

        [Header("Mountain")]
        [SerializeField] private int maxConnectionsPerPeak = 2;

        [Tooltip("2本目の接続で許可する最小角度。90なら、鋭角に曲がる接続を避ける。")]
        [Range(0f, 180f)]
        [SerializeField] private float secondConnectionMinAngle = 90f;

        [Tooltip("山脈線同士の交差を避ける。")]
        [SerializeField] private bool avoidCrossingMountainLines = true;

        [Header("Chunk Height")]
        [SerializeField] private int peakMinHeight = 8;

        [SerializeField] private int peakMaxHeight = 12;

        [SerializeField] private int mountainRandomOffset = 1;

        [SerializeField] private int minLandHeight = 1;

        //[SerializeField] private int minHeightDropPerStep = 1;

        //[SerializeField] private int maxHeightDropPerStep = 2;

        [SerializeField] private float heightDropPerDistance = 0.35f;

        [SerializeField] private int heightNoiseAmount = 1;

        [SerializeField] private bool useEightDirectionHeightSpread = false;

        private ChunkTerrainData[,] chunks;

        public ChunkTerrainData[,] Chunks => chunks;

        public int ChunkWidth => plateWidth * chunksPerPlate;
        public int ChunkHeight => plateHeight * chunksPerPlate;

        private System.Random rng;

        private PlateLayerData[,] plates;
        private readonly List<PlateLayerData> landPlates = new();
        private readonly List<MountainConnection> mountainConnections = new();

        public int PlateWidth => plateWidth;
        public int PlateHeight => plateHeight;
        public int ChunksPerPlate => chunksPerPlate;
        public float ChunkWorldSize => chunkWorldSize;

        public PlateLayerData[,] Plates => plates;
        public IReadOnlyList<PlateLayerData> LandPlates => landPlates;
        public IReadOnlyList<MountainConnection> MountainConnections => mountainConnections;

        private void Start()
        {
            Generate();
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (randomSeedOnPlay)
            {
                seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            }

            rng = new System.Random(seed);

            CreatePlateArray();
            GeneratePrimaryPlateLayer();
            GenerateSecondaryPlateLayer();
            CollectLandPlates();
            GeneratePeaks();
            GenerateMountainConnections();

            GenerateChunkTerrainAndHeight();
        }

        private void CreatePlateArray()
        {
            plates = new PlateLayerData[plateWidth, plateHeight];

            for (int x = 0; x < plateWidth; x++)
            {
                for (int y = 0; y < plateHeight; y++)
                {
                    plates[x, y] = new PlateLayerData
                    {
                        plateCoord = new Vector2Int(x, y),
                        biome = PlateBiome.Sea
                    };
                }
            }

            landPlates.Clear();
            mountainConnections.Clear();
        }

        private void GeneratePrimaryPlateLayer()
        {
            for (int x = 0; x < plateWidth; x++)
            {
                for (int y = 0; y < plateHeight; y++)
                {
                    if (Roll(primaryLandChance))
                    {
                        plates[x, y].biome = PlateBiome.Land;
                    }
                    else
                    {
                        plates[x, y].biome = PlateBiome.Sea;
                    }
                }
            }
        }

        private void GenerateSecondaryPlateLayer()
        {
            // 同時更新にする。
            // その場で書き換えると、先に変わったプレートが後続判定に影響してしまうため。
            bool[,] shouldBecomeLand = new bool[plateWidth, plateHeight];

            for (int x = 0; x < plateWidth; x++)
            {
                for (int y = 0; y < plateHeight; y++)
                {
                    PlateLayerData current = plates[x, y];

                    if (current.IsLand)
                    {
                        continue;
                    }

                    bool upLand = IsLandPlate(x, y + 1);
                    bool downLand = IsLandPlate(x, y - 1);
                    bool leftLand = IsLandPlate(x - 1, y);
                    bool rightLand = IsLandPlate(x + 1, y);

                    bool upSea = IsSeaOrOutOfBounds(x, y + 1);
                    bool downSea = IsSeaOrOutOfBounds(x, y - 1);
                    bool leftSea = IsSeaOrOutOfBounds(x - 1, y);
                    bool rightSea = IsSeaOrOutOfBounds(x + 1, y);

                    // 1. 四方が海なら、確率で陸にする
                    if (upSea && downSea && leftSea && rightSea)
                    {
                        if (Roll(isolatedSeaToLandChance))
                        {
                            shouldBecomeLand[x, y] = true;
                            continue;
                        }
                    }

                    // 2. 上下に陸があるなら、確率で陸にする
                    if (upLand && downLand)
                    {
                        if (Roll(verticalBridgeLandChance))
                        {
                            shouldBecomeLand[x, y] = true;
                            continue;
                        }
                    }

                    // 3. 左右に陸があるなら、確率で陸にする
                    if (leftLand && rightLand)
                    {
                        if (Roll(horizontalBridgeLandChance))
                        {
                            shouldBecomeLand[x, y] = true;
                            continue;
                        }
                    }
                }
            }

            for (int x = 0; x < plateWidth; x++)
            {
                for (int y = 0; y < plateHeight; y++)
                {
                    if (shouldBecomeLand[x, y])
                    {
                        plates[x, y].biome = PlateBiome.Land;
                    }
                }
            }
        }

        private void CollectLandPlates()
        {
            landPlates.Clear();

            for (int x = 0; x < plateWidth; x++)
            {
                for (int y = 0; y < plateHeight; y++)
                {
                    if (plates[x, y].IsLand)
                    {
                        landPlates.Add(plates[x, y]);
                    }
                }
            }
        }

        private void GeneratePeaks()
        {
            foreach (PlateLayerData plate in landPlates)
            {
                plate.connections.Clear();

                int baseChunkX = plate.plateCoord.x * chunksPerPlate;
                int baseChunkY = plate.plateCoord.y * chunksPerPlate;

                // 完全ランダムだと端に寄りすぎるので、中央寄りに出やすくする。
                int localChunkX = RandomCenteredIndex(chunksPerPlate);
                int localChunkY = RandomCenteredIndex(chunksPerPlate);

                Vector2Int peakChunk = new Vector2Int(
                    baseChunkX + localChunkX,
                    baseChunkY + localChunkY
                );

                plate.peakChunkCoord = peakChunk;
                plate.peakWorldPos = ChunkToWorldCenter(peakChunk);
            }
        }

        private void GenerateMountainConnections()
        {
            mountainConnections.Clear();

            // 各陸プレートから見て、隣接陸プレートへの候補を作る。
            // 近い順に処理することで、自然に短い山脈が優先される。
            List<(PlateLayerData a, PlateLayerData b, float dist)> candidates = new();

            foreach (PlateLayerData plate in landPlates)
            {
                foreach (PlateLayerData neighbor in GetAdjacentLandPlates(plate))
                {
                    if (plate.plateCoord.x > neighbor.plateCoord.x)
                    {
                        continue;
                    }

                    if (plate.plateCoord.x == neighbor.plateCoord.x &&
                        plate.plateCoord.y > neighbor.plateCoord.y)
                    {
                        continue;
                    }

                    float d = Vector2.Distance(plate.peakWorldPos, neighbor.peakWorldPos);
                    candidates.Add((plate, neighbor, d));
                }
            }

            candidates.Sort((c1, c2) => c1.dist.CompareTo(c2.dist));

            foreach (var candidate in candidates)
            {
                TryConnect(candidate.a, candidate.b);
            }
        }

        private bool TryConnect(PlateLayerData a, PlateLayerData b)
        {
            if (a == null || b == null) return false;
            if (a == b) return false;

            if (a.ConnectionCount >= maxConnectionsPerPeak) return false;
            if (b.ConnectionCount >= maxConnectionsPerPeak) return false;

            if (AlreadyConnected(a, b)) return false;

            if (!PassesAngleRule(a, b)) return false;
            if (!PassesAngleRule(b, a)) return false;

            if (avoidCrossingMountainLines && WouldCrossExistingLine(a.peakWorldPos, b.peakWorldPos))
            {
                return false;
            }

            MountainConnection connection = new MountainConnection(a, b);
            connection.mountainChunks = GetSupercoverLine(a.peakChunkCoord, b.peakChunkCoord);

            a.connections.Add(connection);
            b.connections.Add(connection);
            mountainConnections.Add(connection);

            return true;
        }

        private bool PassesAngleRule(PlateLayerData self, PlateLayerData target)
        {
            // まだ接続がないなら、角度判定不要。
            if (self.ConnectionCount == 0)
            {
                return true;
            }

            // すでに1本接続がある場合、
            // 既存接続方向と新規接続方向の角度が小さすぎるものを避ける。
            foreach (MountainConnection existing in self.connections)
            {
                PlateLayerData other = existing.Other(self);
                if (other == null) continue;

                Vector2 dirExisting = (other.peakWorldPos - self.peakWorldPos).normalized;
                Vector2 dirNew = (target.peakWorldPos - self.peakWorldPos).normalized;

                float angle = Vector2.Angle(dirExisting, dirNew);

                if (angle < secondConnectionMinAngle)
                {
                    return false;
                }
            }

            return true;
        }

        private bool WouldCrossExistingLine(Vector2 a, Vector2 b)
        {
            foreach (MountainConnection existing in mountainConnections)
            {
                Vector2 c = existing.a.peakWorldPos;
                Vector2 d = existing.b.peakWorldPos;

                // 同じ山頂を共有する線は交差扱いしない。
                if (ApproximatelySamePoint(a, c) || ApproximatelySamePoint(a, d) ||
                    ApproximatelySamePoint(b, c) || ApproximatelySamePoint(b, d))
                {
                    continue;
                }

                if (LineSegmentsIntersect(a, b, c, d))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AlreadyConnected(PlateLayerData a, PlateLayerData b)
        {
            foreach (MountainConnection connection in mountainConnections)
            {
                if ((connection.a == a && connection.b == b) ||
                    (connection.a == b && connection.b == a))
                {
                    return true;
                }
            }

            return false;
        }

        private void GenerateChunkTerrainAndHeight()
        {
            CreateChunkArray();
            MarkLandChunksFromPlates();
            AssignPeakHeights();
            AssignMountainHeights();
            SpreadHeightsFromMountains();
            FillRemainingLandChunks();
        }

        private void CreateChunkArray()
        {
            chunks = new ChunkTerrainData[ChunkWidth, ChunkHeight];

            for (int x = 0; x < ChunkWidth; x++)
            {
                for (int y = 0; y < ChunkHeight; y++)
                {
                    chunks[x, y] = new ChunkTerrainData
                    {
                        chunkCoord = new Vector2Int(x, y),
                        isLand = false,
                        isPeak = false,
                        isMountain = false,
                        height = 0
                    };
                }
            }
        }

        private void MarkLandChunksFromPlates()
        {
            foreach (PlateLayerData plate in landPlates)
            {
                int startX = plate.plateCoord.x * chunksPerPlate;
                int startY = plate.plateCoord.y * chunksPerPlate;

                for (int lx = 0; lx < chunksPerPlate; lx++)
                {
                    for (int ly = 0; ly < chunksPerPlate; ly++)
                    {
                        int cx = startX + lx;
                        int cy = startY + ly;

                        if (!IsInsideChunk(cx, cy)) continue;

                        ChunkTerrainData chunk = chunks[cx, cy];
                        chunk.isLand = true;

                        // 陸だが、まだ高さ未定義
                        chunk.height = -1;
                    }
                }
            }

            // 海チャンクは高さ0のまま
        }

        private void AssignPeakHeights()
        {
            foreach (PlateLayerData plate in landPlates)
            {
                Vector2Int c = plate.peakChunkCoord;

                if (!IsInsideChunk(c.x, c.y)) continue;

                ChunkTerrainData chunk = chunks[c.x, c.y];

                chunk.isLand = true;
                chunk.isPeak = true;
                chunk.isMountain = true;
                chunk.height = RandomIntInclusive(peakMinHeight, peakMaxHeight);
            }
        }

        private void AssignMountainHeights()
        {
            foreach (MountainConnection connection in mountainConnections)
            {
                ChunkTerrainData peakA = GetChunk(connection.a.peakChunkCoord);
                ChunkTerrainData peakB = GetChunk(connection.b.peakChunkCoord);

                if (peakA == null || peakB == null) continue;
                if (!peakA.HasHeight || !peakB.HasHeight) continue;

                int count = connection.mountainChunks.Count;

                for (int i = 0; i < count; i++)
                {
                    Vector2Int c = connection.mountainChunks[i];

                    if (!IsInsideChunk(c.x, c.y)) continue;

                    ChunkTerrainData chunk = chunks[c.x, c.y];

                    if (!chunk.isLand)
                    {
                        continue;
                    }

                    float t = count <= 1 ? 0f : i / (float)(count - 1);

                    int baseHeight = Mathf.RoundToInt(Mathf.Lerp(peakA.height, peakB.height, t));
                    int offset = RandomIntInclusive(-mountainRandomOffset, mountainRandomOffset);

                    int height = Mathf.Clamp(
                        baseHeight + offset,
                        minLandHeight,
                        Mathf.Max(peakA.height, peakB.height)
                    );

                    chunk.isMountain = true;

                    // 山頂チャンクは山頂高さを優先する
                    if (chunk.isPeak)
                    {
                        continue;
                    }

                    // 複数の山脈が通る場合は高い方を採用
                    if (!chunk.HasHeight || height > chunk.height)
                    {
                        chunk.height = height;
                    }
                }
            }
        }

        private void SpreadHeightsFromMountains()
        {
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            int[,] distanceFromRidge = new int[ChunkWidth, ChunkHeight];
            int[,] sourceHeight = new int[ChunkWidth, ChunkHeight];

            for (int x = 0; x < ChunkWidth; x++)
            {
                for (int y = 0; y < ChunkHeight; y++)
                {
                    distanceFromRidge[x, y] = -1;
                    sourceHeight[x, y] = -1;
                }
            }

            // 山頂・山脈をBFSの起点にする
            for (int x = 0; x < ChunkWidth; x++)
            {
                for (int y = 0; y < ChunkHeight; y++)
                {
                    ChunkTerrainData chunk = chunks[x, y];

                    if (!chunk.isLand) continue;

                    if ((chunk.isPeak || chunk.isMountain) && chunk.HasHeight)
                    {
                        Vector2Int c = new Vector2Int(x, y);
                        queue.Enqueue(c);

                        distanceFromRidge[x, y] = 0;
                        sourceHeight[x, y] = chunk.height;
                    }
                }
            }

            while (queue.Count > 0)
            {
                Vector2Int currentCoord = queue.Dequeue();

                foreach (Vector2Int nextCoord in GetHeightSpreadNeighbors(currentCoord))
                {
                    if (!IsInsideChunk(nextCoord.x, nextCoord.y)) continue;

                    ChunkTerrainData next = chunks[nextCoord.x, nextCoord.y];

                    if (!next.isLand) continue;
                    if (distanceFromRidge[nextCoord.x, nextCoord.y] >= 0) continue;

                    int nextDistance = distanceFromRidge[currentCoord.x, currentCoord.y] + 1;
                    int rootHeight = sourceHeight[currentCoord.x, currentCoord.y];

                    distanceFromRidge[nextCoord.x, nextCoord.y] = nextDistance;
                    sourceHeight[nextCoord.x, nextCoord.y] = rootHeight;

                    float rawHeight = rootHeight - nextDistance * heightDropPerDistance;
                    int noise = RandomIntInclusive(-heightNoiseAmount, heightNoiseAmount);

                    int nextHeight = Mathf.RoundToInt(rawHeight) + noise;
                    nextHeight = Mathf.Clamp(nextHeight, minLandHeight, rootHeight);

                    next.height = nextHeight;

                    queue.Enqueue(nextCoord);
                }
            }
        }

        private void FillRemainingLandChunks()
        {
            // 山頂や山脈が存在しない孤立陸プレートなどの保険。
            // まだ高さが入っていない陸チャンクがあれば、最低高度を入れる。
            for (int x = 0; x < ChunkWidth; x++)
            {
                for (int y = 0; y < ChunkHeight; y++)
                {
                    ChunkTerrainData chunk = chunks[x, y];

                    if (!chunk.isLand) continue;

                    if (!chunk.HasHeight)
                    {
                        chunk.height = minLandHeight;
                    }
                }
            }
        }

        private IEnumerable<Vector2Int> GetHeightSpreadNeighbors(Vector2Int c)
        {
            yield return new Vector2Int(c.x + 1, c.y);
            yield return new Vector2Int(c.x - 1, c.y);
            yield return new Vector2Int(c.x, c.y + 1);
            yield return new Vector2Int(c.x, c.y - 1);

            if (!useEightDirectionHeightSpread)
            {
                yield break;
            }

            yield return new Vector2Int(c.x + 1, c.y + 1);
            yield return new Vector2Int(c.x + 1, c.y - 1);
            yield return new Vector2Int(c.x - 1, c.y + 1);
            yield return new Vector2Int(c.x - 1, c.y - 1);
        }

        private ChunkTerrainData GetChunk(Vector2Int c)
        {
            if (!IsInsideChunk(c.x, c.y)) return null;
            return chunks[c.x, c.y];
        }

        private bool IsInsideChunk(int x, int y)
        {
            return x >= 0 && x < ChunkWidth && y >= 0 && y < ChunkHeight;
        }

        private int RandomIntInclusive(int min, int max)
        {
            if (max < min)
            {
                int temp = min;
                min = max;
                max = temp;
            }

            return rng.Next(min, max + 1);
        }

        private List<PlateLayerData> GetAdjacentLandPlates(PlateLayerData plate)
        {
            List<PlateLayerData> result = new();

            int x = plate.plateCoord.x;
            int y = plate.plateCoord.y;

            TryAddLand(x + 1, y, result);
            TryAddLand(x - 1, y, result);
            TryAddLand(x, y + 1, result);
            TryAddLand(x, y - 1, result);

            return result;
        }

        private void TryAddLand(int x, int y, List<PlateLayerData> result)
        {
            if (!IsInsidePlate(x, y)) return;

            PlateLayerData p = plates[x, y];
            if (p.IsLand)
            {
                result.Add(p);
            }
        }

        private bool IsLandPlate(int x, int y)
        {
            if (!IsInsidePlate(x, y)) return false;
            return plates[x, y].IsLand;
        }

        private bool IsSeaOrOutOfBounds(int x, int y)
        {
            if (!IsInsidePlate(x, y)) return true;
            return !plates[x, y].IsLand;
        }

        private bool IsInsidePlate(int x, int y)
        {
            return x >= 0 && x < plateWidth && y >= 0 && y < plateHeight;
        }

        private int RandomCenteredIndex(int size)
        {
            // 0〜1の乱数を2つ平均して、中央寄りにする。
            float a = (float)rng.NextDouble();
            float b = (float)rng.NextDouble();
            float centered = (a + b) * 0.5f;

            int index = Mathf.FloorToInt(centered * size);
            return Mathf.Clamp(index, 0, size - 1);
        }

        private bool Roll(float chance)
        {
            return rng.NextDouble() < chance;
        }

        public Vector2 ChunkToWorldCenter(Vector2Int chunkCoord)
        {
            return new Vector2(
                (chunkCoord.x + 0.5f) * chunkWorldSize,
                (chunkCoord.y + 0.5f) * chunkWorldSize
            );
        }

        public Vector2 PlateToWorldMin(Vector2Int plateCoord)
        {
            return new Vector2(
                plateCoord.x * chunksPerPlate * chunkWorldSize,
                plateCoord.y * chunksPerPlate * chunkWorldSize
            );
        }

        public Vector2 PlateToWorldSize()
        {
            float size = chunksPerPlate * chunkWorldSize;
            return new Vector2(size, size);
        }

        private List<Vector2Int> GetSupercoverLine(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> result = new();

            int x0 = start.x;
            int y0 = start.y;
            int x1 = end.x;
            int y1 = end.y;

            int dx = x1 - x0;
            int dy = y1 - y0;

            int nx = Mathf.Abs(dx);
            int ny = Mathf.Abs(dy);

            int signX = dx > 0 ? 1 : -1;
            int signY = dy > 0 ? 1 : -1;

            int x = x0;
            int y = y0;

            result.Add(new Vector2Int(x, y));

            int ix = 0;
            int iy = 0;

            while (ix < nx || iy < ny)
            {
                float tx = nx == 0 ? float.PositiveInfinity : (0.5f + ix) / nx;
                float ty = ny == 0 ? float.PositiveInfinity : (0.5f + iy) / ny;

                if (tx < ty)
                {
                    x += signX;
                    ix++;
                }
                else if (ty < tx)
                {
                    y += signY;
                    iy++;
                }
                else
                {
                    x += signX;
                    y += signY;
                    ix++;
                    iy++;
                }

                result.Add(new Vector2Int(x, y));
            }

            return result;
        }

        private static bool ApproximatelySamePoint(Vector2 a, Vector2 b)
        {
            return Vector2.SqrMagnitude(a - b) < 0.0001f;
        }

        private static bool LineSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float o1 = Orientation(a, b, c);
            float o2 = Orientation(a, b, d);
            float o3 = Orientation(c, d, a);
            float o4 = Orientation(c, d, b);

            return o1 * o2 < 0f && o3 * o4 < 0f;
        }

        private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }
    }
}