using UnityEngine;
using UnityEngine.Rendering;

namespace Greatwall.Flight
{
    [DisallowMultipleComponent]
    public sealed class FlightLandscape : MonoBehaviour
    {
        // The headset camera clips at 1000 m. Keep at least 2000 m in every
        // horizontal direction so turning around or looking diagonally cannot expose a swap.
        private const int Radius = 5;
        private const int Side = Radius * 2 + 1;
        private const int Variants = 4;
        private const int Divisions = 64;
        private const float TileSize = 400f;
        private const float FlightCorridorX = TileSize * 0.5f;
        private const int WallLaneCount = 2;
        private const float TowerScale = 1.25f;

        [SerializeField] private TerrainData terrainSource;
        [SerializeField] private Shader terrainShader;
        [SerializeField] private GameObject wallSection;
        [SerializeField] private Mesh towerMesh;
        [SerializeField] private Material towerMaterial;
        [SerializeField] private Material horizonSky;
        [Tooltip("地景沿起飞航向向后移动的速度（米/秒）。")]
        [SerializeField, Min(0f)] private float scrollSpeed = 11f;
        [Tooltip("起飞时，地形基准面在座位下方的距离（米）。")]
        [SerializeField, Min(64f)] private float depthBelowSeat = 70f;

        private sealed class Tile
        {
            public Transform Transform;
            public MeshFilter Terrain;
            public MeshFilter[] Walls;
            public Transform[] Towers;
        }

        private readonly Tile[,] _pool = new Tile[Side, Side];
        private readonly Mesh[,] _terrainMeshes = new Mesh[Variants, Variants];
        private readonly Mesh[,] _wallMeshes = new Mesh[WallLaneCount, Variants];
        private readonly Pose[,] _towerPoses = new Pose[WallLaneCount, Variants];
        private Transform _landscape;
        private Material _terrainMaterial;
        private Material[] _scenicWallMaterials;
        private Material _scenicTowerMaterial;
        private FlightVegetation _vegetation;
        private Vector3 _origin;
        private Vector2Int _centerCell;
        private float[,] _heights;
        private float _heightExtent;
        private float _heightmapLast;
        private bool _departureReady;
        private bool _positioned;

        internal void Prepare()
        {
            if (_landscape != null || terrainSource == null || terrainShader == null
                || wallSection == null || towerMesh == null || towerMaterial == null)
                return;

            _landscape = new GameObject("飞行山地与长城").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_landscape.gameObject,
                gameObject.scene);
            _landscape.gameObject.SetActive(false);

            _heightExtent = terrainSource.size.y * 1.55f;
            int resolution = terrainSource.heightmapResolution;
            _heightmapLast = resolution - 1;
            _heights = terrainSource.GetHeights(0, 0, resolution, resolution);
            _terrainMaterial = MakeTerrainMaterial();
            _scenicTowerMaterial = MakeMasonryMaterial(towerMaterial);
            var wall = new FlightWallSections(wallSection, MakeMasonryMaterial);
            _scenicWallMaterials = wall.Materials;

            for (int z = 0; z < Variants; z++)
            {
                for (int x = 0; x < Variants; x++)
                    _terrainMeshes[x, z] = MakeTerrain(x, z);
                for (int lane = 0; lane < WallLaneCount; lane++)
                {
                    int side = lane == 0 ? -1 : 1;
                    _wallMeshes[lane, z] = wall.BendAlong(z * TileSize, TileSize, distance => WallPoint(distance, side));
                    _towerPoses[lane, z] = MakeTowerPose(z, side);
                }
            }
            _vegetation = new FlightVegetation(terrainSource, MakeFoliageMaterial, SourceUv,
                ProjectSourcePoint, SurfaceHeight, DistanceToWall, Variants, TileSize);
            _terrainMaterial.SetTexture("_DetailCoverage", _vegetation.DetailCoverage);

            for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                var tile = MakeRenderer("山地分段", _landscape, _terrainMaterial);
                var entry = new Tile { Transform = tile.transform, Terrain = tile };
                _pool[x, z] = entry;
                // Only the slots that can wrap to column zero ever need wall renderers.
                if (x == 0)
                {
                    entry.Walls = new MeshFilter[WallLaneCount];
                    entry.Towers = new Transform[WallLaneCount];
                    for (int lane = 0; lane < WallLaneCount; lane++)
                    {
                        string side = lane == 0 ? "左侧" : "右侧";
                        MeshFilter wallFilter = MakeRenderer(side + "城墙", tile.transform, null);
                        wallFilter.GetComponent<MeshRenderer>().sharedMaterials = _scenicWallMaterials;
                        entry.Walls[lane] = wallFilter;
                        var tower = MakeRenderer(side + "敌楼", tile.transform, _scenicTowerMaterial);
                        tower.sharedMesh = towerMesh;
                        tower.transform.localScale = Vector3.one * TowerScale;
                        entry.Towers[lane] = tower.transform;
                    }
                }
            }
            _heights = null;
        }

        internal void SetDeparturePose(Vector3 seat, float heading)
        {
            Prepare();
            if (_landscape == null) return;
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            _origin = seat - Vector3.up * depthBelowSeat
                - rotation * new Vector3(FlightCorridorX, 0f, TileSize * 0.5f);
            _landscape.SetPositionAndRotation(_origin, rotation);
            _departureReady = true;
            _positioned = false;
        }

        internal void Show(Vector3 seat, float seconds, Camera vegetationCamera = null)
        {
            if (!_departureReady || _landscape == null) return;
            float elapsed = Mathf.Max(seconds, 0f);
            // Ease the ground into motion as the dragon begins moving.
            float scroll = scrollSpeed * (elapsed - (1f - Mathf.Exp(-elapsed)));
            _landscape.position = _origin - _landscape.forward * scroll;
            Vector3 local = _landscape.InverseTransformPoint(seat);
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / TileSize),
                Mathf.FloorToInt(local.z / TileSize));
            UpdateTiles(cell);
            if (!_landscape.gameObject.activeSelf)
                _landscape.gameObject.SetActive(true);
            _vegetation.Draw(_landscape, local, _landscape.gameObject.layer, vegetationCamera);
        }

        internal void Hide()
        {
            if (_landscape != null) _landscape.gameObject.SetActive(false);
        }

        private void UpdateTiles(Vector2Int cell)
        {
            if (_positioned && _centerCell == cell) return;

            for (int z = cell.y - Radius; z <= cell.y + Radius; z++)
            for (int x = cell.x - Radius; x <= cell.x + Radius; x++)
            {
                if (_positioned && Mathf.Abs(x - _centerCell.x) <= Radius
                    && Mathf.Abs(z - _centerCell.y) <= Radius)
                    continue;

                Tile tile = _pool[Wrap(x, Side), Wrap(z, Side)];
                int variantZ = Wrap(z, Variants);
                tile.Transform.localPosition = new Vector3(x * TileSize, 0f, z * TileSize);
                tile.Terrain.sharedMesh = _terrainMeshes[Wrap(x, Variants), variantZ];

                if (tile.Walls == null) continue;
                bool hasWall = x == 0;
                for (int lane = 0; lane < WallLaneCount; lane++)
                {
                    tile.Walls[lane].gameObject.SetActive(hasWall);
                    tile.Towers[lane].gameObject.SetActive(hasWall);
                    if (!hasWall) continue;

                    tile.Walls[lane].sharedMesh = _wallMeshes[lane, variantZ];
                    Pose pose = _towerPoses[lane, variantZ];
                    tile.Towers[lane].SetLocalPositionAndRotation(pose.position, pose.rotation);
                }
            }

            _centerCell = cell;
            _positioned = true;
        }

        private Pose MakeTowerPose(int variant, int side)
        {
            float offset = variant * TileSize;
            float distance = variant % 2 == 0 ? 200f : 310f;
            distance += side * 62f;
            Vector3 point = WallPoint(offset + distance, side);
            Vector3 direction = WallPoint(point.z + 1f, side) - WallPoint(point.z - 1f, side);
            direction.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(direction);
            var bounds = towerMesh.bounds;
            Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            // Sink the model slightly into the deck to close the join on a slope.
            return new Pose(point - new Vector3(0f, 1.8f, offset)
                - rotation * (pivot * TowerScale), rotation);
        }

        private Material MakeTerrainMaterial()
        {
            var material = MakeScenicMaterial("Part1 Terrain");
            material.SetTexture("_Control", terrainSource.alphamapTextures[0]);
            TerrainLayer[] layers = terrainSource.terrainLayers;
            var scales = Vector4.one;
            for (int i = 0; i < Mathf.Min(layers.Length, 4); i++)
            {
                TerrainLayer layer = layers[i];
                material.SetTexture("_Layer" + i, layer.diffuseTexture);
                scales[i] = TileSize / Mathf.Max(1f, layer.tileSize.x * 3f);
            }
            material.SetVector("_TileFrequency", scales);
            return material;
        }

        private Material MakeMasonryMaterial(Material source)
        {
            Material material = MakeScenicMaterial(source.name);
            material.EnableKeyword("_FLIGHT_MASONRY");
            material.SetTexture("_Layer0", source.mainTexture);
            material.SetTextureScale("_Layer0", source.mainTextureScale);
            material.SetTextureOffset("_Layer0", source.mainTextureOffset);
            material.SetColor("_StoneTint", source.color);
            return material;
        }

        private Material MakeFoliageMaterial(Material source, bool cutout)
        {
            Material material = MakeScenicMaterial(source.name);
            material.EnableKeyword("_FLIGHT_FOLIAGE");
            material.enableInstancing = true;
            material.SetTexture("_Layer0", source.mainTexture);
            material.SetColor("_StoneTint", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            material.SetFloat("_FoliageCutoff", cutout ? 0.32f : 0f);
            material.SetFloat("_Cull", cutout ? (int)CullMode.Off : (int)CullMode.Back);
            if (cutout) material.renderQueue = (int)RenderQueue.AlphaTest;
            return material;
        }

        private Material MakeScenicMaterial(string materialName)
        {
            var material = new Material(terrainShader) { name = "Flight – " + materialName };
            Material sky = horizonSky != null ? horizonSky : RenderSettings.skybox;
            if (sky != null && sky.shader.name == "Skybox/Panoramic"
                && sky.GetFloat("_Mapping") == 1f && sky.GetFloat("_ImageType") == 0f
                && sky.GetFloat("_Layout") == 0f)
            {
                material.SetTexture("_Panorama", sky.GetTexture("_MainTex"));
                material.SetColor("_PanoramaTint", sky.GetColor("_Tint"));
                float angle = sky.GetFloat("_Rotation") * Mathf.Deg2Rad;
                // The panoramic sky shader treats its default 0.5 tint as neutral.
                float tintScale = QualitySettings.activeColorSpace == ColorSpace.Linear ? 4.5947938f : 2f;
                float exposure = sky.GetFloat("_Exposure") * tintScale;
                material.SetVector("_PanoramaRotation", new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), 1f, exposure));
            }
            return material;
        }

        private Mesh MakeTerrain(int tileX, int tileZ)
        {
            int side = Divisions + 1;
            float step = TileSize / Divisions;
            float originX = tileX * TileSize;
            float originZ = tileZ * TileSize;
            var positions = new Vector3[side * side];
            var normals = new Vector3[positions.Length];
            var uv = new Vector2[positions.Length];
            var heights = new float[side + 2, side + 2];
            for (int z = -1; z <= side; z++)
            for (int x = -1; x <= side; x++)
                heights[x + 1, z + 1] = Height(originX + x * step, originZ + z * step);

            var triangles = new int[Divisions * Divisions * 6];
            int triangle = 0;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int v = z * side + x;
                float localX = x * step, localZ = z * step;
                positions[v] = new Vector3(localX, heights[x + 1, z + 1], localZ);
                uv[v] = SourceUv(originX + localX, originZ + localZ);
                normals[v] = new Vector3(heights[x, z + 1] - heights[x + 2, z + 1],
                    2f * step, heights[x + 1, z] - heights[x + 1, z + 2]).normalized;
                if (x == Divisions || z == Divisions) continue;
                triangles[triangle++] = v;
                triangles[triangle++] = v + side;
                triangles[triangle++] = v + 1;
                triangles[triangle++] = v + 1;
                triangles[triangle++] = v + side;
                triangles[triangle++] = v + side + 1;
            }

            return new Mesh
            {
                name = $"Flight Terrain {tileX},{tileZ}", vertices = positions,
                normals = normals, uv = uv, triangles = triangles
            };
        }

        private static Vector2 SourceUv(float x, float z)
        {
            const float phase = Mathf.PI * 2f / (Variants * TileSize);
            // Mirror the Part1 heightmap at the edges. A periodic warp varies the
            // shape of the next hill while keeping the heights and texture continuous.
            return new Vector2(Mathf.PingPong(x / TileSize + 0.12f * Mathf.Sin(z * phase), 1f),
                Mathf.PingPong(z / TileSize + 0.15f * Mathf.Sin(x * phase * 2f), 1f));
        }

        private static Vector2 ProjectSourcePoint(Vector2 uv, int x, int z)
        {
            const float phase = Mathf.PI * 2f / Variants;
            float u = x + (x % 2 == 0 ? uv.x : 1f - uv.x);
            float v = z + (z % 2 == 0 ? uv.y : 1f - uv.y);
            float p = u;
            float q = v;
            for (int i = 0; i < 5; i++)
            {
                p = u - 0.12f * Mathf.Sin(q * phase);
                q = v - 0.15f * Mathf.Sin(p * phase * 2f);
            }
            return new Vector2(p, q) * TileSize;
        }

        private float Height(float x, float z)
        {
            Vector2 uv = SourceUv(x, z) * _heightmapLast;
            int ix = Mathf.Min((int)uv.x, (int)_heightmapLast - 1);
            int iz = Mathf.Min((int)uv.y, (int)_heightmapLast - 1);
            float tx = uv.x - ix, tz = uv.y - iz;
            float lower = Mathf.Lerp(_heights[iz, ix], _heights[iz, ix + 1], tx);
            float upper = Mathf.Lerp(_heights[iz + 1, ix], _heights[iz + 1, ix + 1], tx);
            return Mathf.Lerp(lower, upper, tz) * _heightExtent;
        }

        private float SurfaceHeight(float x, float z)
        {
            const float step = TileSize / Divisions;
            float x0 = Mathf.Floor(x / step) * step;
            float z0 = Mathf.Floor(z / step) * step;
            float u = (x - x0) / step;
            float v = (z - z0) / step;
            float right = Height(x0 + step, z0);
            float far = Height(x0, z0 + step);
            if (u + v <= 1f)
                return (1f - u - v) * Height(x0, z0) + u * right + v * far;
            return (u + v - 1f) * Height(x0 + step, z0 + step) + (1f - v) * right + (1f - u) * far;
        }

        private static float WallX(float z, int side)
        {
            const float period = Mathf.PI * 2f / (Variants * TileSize);
            float sway = 22f * Mathf.Sin(z * period * 3f + side * 0.8f)
                + 9f * Mathf.Sin(z * period * 7f + side * 1.3f);
            return FlightCorridorX + side * (86f + sway);
        }

        private static float DistanceToWall(float x, float z) =>
            Mathf.Min(Mathf.Abs(x - WallX(z, -1)), Mathf.Abs(x - WallX(z, 1)));

        private Vector3 WallPoint(float z, int side)
        {
            float x = WallX(z, side);
            float elevation = 0.25f * SurfaceHeight(WallX(z - 3f, side), z - 3f)
                + 0.5f * SurfaceHeight(x, z)
                + 0.25f * SurfaceHeight(WallX(z + 3f, side), z + 3f);
            return new Vector3(x, elevation + 3.6f, z);
        }

        private static MeshFilter MakeRenderer(string objectName, Transform parent, Material material)
        {
            var item = new GameObject(objectName);
            item.layer = parent.gameObject.layer;
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return item.AddComponent<MeshFilter>();
        }

        private static int Wrap(int value, int size) => ((value % size) + size) % size;

        private void OnDestroy()
        {
            if (_landscape != null) Release(_landscape.gameObject);
            foreach (Mesh mesh in _terrainMeshes) Release(mesh);
            foreach (Mesh mesh in _wallMeshes) Release(mesh);
            Release(_terrainMaterial);
            if (_scenicWallMaterials != null)
                foreach (Material material in _scenicWallMaterials) Release(material);
            Release(_scenicTowerMaterial);
            _vegetation?.Dispose();
        }

        internal static void Release(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }

    }
}
