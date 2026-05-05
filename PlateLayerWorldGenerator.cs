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

    public enum MountainDirectionType
    {
        Vertical,
        Horizontal
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

        public MountainDirectionType directionType = MountainDirectionType.Vertical;

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
        [SerializeField] private int maxConnectionsPerPeak = 3;

        [Tooltip("山頂をプレート外周から何チャンク内側に置くか。2なら外周2チャンクには生成しない。")]
        [SerializeField] private int peakInnerMarginChunks = 2;

        [Tooltip("追加接続で許可する最小角度。90なら鋭角に曲がる接続を避ける。")]
        [Range(0f, 180f)]
        [SerializeField] private float secondConnectionMinAngle = 90f;

        [Tooltip("山脈線同士の交差を避ける。")]
        [SerializeField] private bool avoidCrossingMountainLines = true;

        [Header("Chunk Height")]
        [SerializeField] private int peakMinHeight = 8;
        [SerializeField] private int peakMaxHeight = 12;

        [SerializeField] private int minLandHeight = 1;

        [Header("Primary Height")]
        [Tooltip("1次高さ設定。山頂から1チャンク離れるごとにどれくらい高さを下げるか。")]
        [SerializeField] private float primaryHeightDropPerChunk = 0.55f;

        [SerializeField] private int primaryHeightNoiseAmount = 1;

        [Header("Secondary Mountain Height")]
        [SerializeField] private int mountainRandomOffset = 1;

        [Tooltip("2次高さ設定。山脈から1チャンク離れるごとにどれくらい高さを下げるか。")]
        [SerializeField] private float secondaryHeightDropPerChunk = 0.75f;

        [Tooltip("山脈から何チャンク分まで2次高さ設定を広げるか。")]
        [SerializeField] private int secondaryRidgeSpreadDistance = 8;

        [SerializeField] private int secondaryHeightNoiseAmount = 1;

        [Tooltip("ONなら、2次高さ設定は現在の高さより高い場合だけ上書きする。")]
        [SerializeField] private bool secondaryOverwriteOnlyWhenHigher = true;

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

            GenerateChunkTerrainAndPrimaryHeight();

            GenerateMountainConnections();

            ApplyMountainAndSecondaryHeights();

            FillRemainingLandChunks();
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
                int localChunkX = RandomCenteredIndexWithMargin(chunksPerPlate, peakInnerMarginChunks);
                int localChunkY = RandomCenteredIndexWithMargin(chunksPerPlate, peakInnerMarginChunks);

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

            foreach (PlateLayerData plate in landPlates)
            {
                plate.connections.Clear();
            }

            bool connectedSomething = true;
            int safety = 0;

            while (connectedSomething && safety < 64)
            {
                connectedSomething = false;
                safety++;

                foreach (PlateLayerData plate in landPlates)
                {
                    if (plate.ConnectionCount >= maxConnectionsPerPeak)
                    {
                        continue;
                    }

                    PlateLayerData target = FindBestMountainTargetByAngle(plate);

                    if (target == null)
                    {
                        continue;
                    }

                    if (TryConnect(plate, target))
                    {
                        connectedSomething = true;
                    }
                }
            }
        }

        private PlateLayerData FindBestMountainTargetByAngle(PlateLayerData self)
        {
            PlateLayerData best = null;
            float bestScore = float.NegativeInfinity;

            foreach (PlateLayerData candidate in GetAdjacentLandPlates(self))
            {
                if (candidate == null) continue;
                if (candidate == self) continue;

                if (candidate.ConnectionCount >= maxConnectionsPerPeak)
                {
                    continue;
                }

                if (AlreadyConnected(self, candidate))
                {
                    continue;
                }

                if (!PassesAngleRule(self, candidate))
                {
                    continue;
                }

                if (!PassesAngleRule(candidate, self))
                {
                    continue;
                }

                if (avoidCrossingMountainLines &&
                    WouldCrossExistingLine(self.peakWorldPos, candidate.peakWorldPos))
                {
                    continue;
                }

                float selfScore = GetAngleScore(self, candidate);
                float targetScore = GetAngleScore(candidate, self);

                if (selfScore < 0f || targetScore < 0f)
                {
                    continue;
                }

                // 距離は使わない。
                // 角度が180°に近いものを優先する。
                // 少しだけ乱数を足して、完全同点時の偏りを減らす。
                float score = selfScore + targetScore + (float)rng.NextDouble() * 0.001f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private float GetAngleScore(PlateLayerData self, PlateLayerData target)
        {
            // まだ接続がない場合は角度評価できないので中立点。
            if (self.ConnectionCount == 0)
            {
                return 0f;
            }

            Vector2 dirNew = (target.peakWorldPos - self.peakWorldPos).normalized;

            float minAngle = 180f;

            foreach (MountainConnection existing in self.connections)
            {
                PlateLayerData other = existing.Other(self);
                if (other == null) continue;

                Vector2 dirExisting = (other.peakWorldPos - self.peakWorldPos).normalized;

                float angle = Vector2.Angle(dirExisting, dirNew);

                if (angle < secondConnectionMinAngle)
                {
                    return -1f;
                }

                minAngle = Mathf.Min(minAngle, angle);
            }

            // 複数の既存山脈がある場合、
            // そのどれかに対して鋭角にならないことを優先しつつ、
            // 一番近い角度がなるべく180°に近いものを選ぶ。
            return minAngle;
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

        private void GenerateChunkTerrainAndPrimaryHeight()
        {
            CreateChunkArray();
            MarkLandChunksFromPlates();
            AssignPeakHeights();
            ApplyPrimaryHeightsFromPeaks();
        }

        private void ApplyPrimaryHeightsFromPeaks()
        {
            foreach (PlateLayerData plate in landPlates)
            {
                Vector2Int peakCoord = plate.peakChunkCoord;
                ChunkTerrainData peakChunk = GetChunk(peakCoord);

                if (peakChunk == null || !peakChunk.HasHeight)
                {
                    continue;
                }

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

                        if (!chunk.isLand) continue;

                        Vector2Int currentCoord = new Vector2Int(cx, cy);

                        float distance = Vector2Int.Distance(currentCoord, peakCoord);

                        float rawHeight = peakChunk.height - distance * primaryHeightDropPerChunk;
                        int noise = RandomIntInclusive(-primaryHeightNoiseAmount, primaryHeightNoiseAmount);

                        int height = Mathf.RoundToInt(rawHeight) + noise;
                        height = Mathf.Clamp(height, minLandHeight, peakChunk.height);

                        if (chunk.isPeak)
                        {
                            chunk.height = peakChunk.height;
                        }
                        else
                        {
                            chunk.height = height;
                        }
                    }
                }
            }
        }

        private void ApplyMountainAndSecondaryHeights()
        {
            foreach (MountainConnection connection in mountainConnections)
            {
                ChunkTerrainData peakA = GetChunk(connection.a.peakChunkCoord);
                ChunkTerrainData peakB = GetChunk(connection.b.peakChunkCoord);

                if (peakA == null || peakB == null) continue;
                if (!peakA.HasHeight || !peakB.HasHeight) continue;

                bool isVerticalMountain = IsVerticalMountain(connection);

                connection.directionType = isVerticalMountain
    ? MountainDirectionType.Vertical
    : MountainDirectionType.Horizontal;

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

                    int mountainHeight = Mathf.Clamp(
                        baseHeight + offset,
                        minLandHeight,
                        Mathf.Max(peakA.height, peakB.height)
                    );

                    chunk.isMountain = true;

                    if (!chunk.isPeak)
                    {
                        ApplyHeightToChunk(chunk, mountainHeight);
                    }

                    if (isVerticalMountain)
                    {
                        // 縦向き山脈なら、左右方向へ高さを流す
                        SpreadSecondaryHeightFromMountainChunk(c, mountainHeight, Vector2Int.left);
                        SpreadSecondaryHeightFromMountainChunk(c, mountainHeight, Vector2Int.right);
                    }
                    else
                    {
                        // 横向き山脈なら、上下方向へ高さを流す
                        SpreadSecondaryHeightFromMountainChunk(c, mountainHeight, Vector2Int.up);
                        SpreadSecondaryHeightFromMountainChunk(c, mountainHeight, Vector2Int.down);
                    }
                }
            }
        }

        private bool IsVerticalMountain(MountainConnection connection)
        {
            Vector2 a = connection.a.peakWorldPos;
            Vector2 b = connection.b.peakWorldPos;

            Vector2 dir = (b - a).normalized;

            // 画面上方向を0°とする
            float angle = Vector2.SignedAngle(Vector2.up, dir);

            // A→B と B→A の向き違いを同じ山脈として扱うため、
            // -90°〜90°に正規化する
            if (angle > 90f)
            {
                angle -= 180f;
            }
            else if (angle < -90f)
            {
                angle += 180f;
            }

            // -45°〜45°なら縦向き山脈
            return angle >= -45f && angle <= 45f;
        }

        private void SpreadSecondaryHeightFromMountainChunk(
            Vector2Int origin,
            int originHeight,
            Vector2Int direction
        )
        {
            for (int step = 1; step <= secondaryRidgeSpreadDistance; step++)
            {
                Vector2Int c = origin + direction * step;

                if (!IsInsideChunk(c.x, c.y))
                {
                    break;
                }

                ChunkTerrainData chunk = chunks[c.x, c.y];

                if (!chunk.isLand)
                {
                    break;
                }

                if (chunk.isPeak)
                {
                    continue;
                }

                float rawHeight = originHeight - step * secondaryHeightDropPerChunk;
                int noise = RandomIntInclusive(-secondaryHeightNoiseAmount, secondaryHeightNoiseAmount);

                int height = Mathf.RoundToInt(rawHeight) + noise;
                height = Mathf.Clamp(height, minLandHeight, originHeight);

                ApplyHeightToChunk(chunk, height);
            }
        }

        private void ApplyHeightToChunk(ChunkTerrainData chunk, int newHeight)
        {
            if (chunk == null) return;
            if (!chunk.isLand) return;

            if (secondaryOverwriteOnlyWhenHigher)
            {
                if (!chunk.HasHeight || newHeight > chunk.height)
                {
                    chunk.height = newHeight;
                }
            }
            else
            {
                chunk.height = newHeight;
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

        private int RandomCenteredIndexWithMargin(int size, int margin)
        {
            int safeMargin = Mathf.Clamp(margin, 0, Mathf.Max(0, (size - 1) / 2));

            int min = safeMargin;
            int max = size - 1 - safeMargin;

            if (min > max)
            {
                return size / 2;
            }

            int selectableSize = max - min + 1;
            return min + RandomCenteredIndex(selectableSize);
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