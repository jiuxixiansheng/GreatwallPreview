using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Greatwall.Flight
{
    internal sealed class FlightVegetation : IDisposable
    {
        private const int BlocksPerTile = 5;
        private const int CoverageSize = 256;
        private const float DetailRange = 235f;
        private const float TreeRange = 460f;
        private const float DetailSpacing = 4f;

        private sealed class DrawPart
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public Matrix4x4 LocalMatrix;
        }

        private sealed class Batch
        {
            public DrawPart[] Parts;
            public Matrix4x4[] LocalMatrices;
            public Vector2 Center;
            public float Range;
        }

        private readonly List<Material> _materials = new List<Material>();
        private readonly Batch[][] _tiles;
        private readonly Matrix4x4[] _drawMatrices = new Matrix4x4[1023];
        private readonly int _variants;
        private readonly float _tileSize;
        private readonly float _blockSize;
        public Texture2D DetailCoverage { get; }

        public FlightVegetation(TerrainData source, Func<Material, bool, Material> makeMaterial,
            Func<float, float, Vector2> sourceUv, Func<Vector2, int, int, Vector2> projectSource,
            Func<float, float, float> surfaceHeight, Func<float, float, float> distanceToWall,
            int variants, float tileSize)
        {
            _variants = variants;
            _tileSize = tileSize;
            _blockSize = tileSize / BlocksPerTile;
            _tiles = new Batch[variants * variants][];

            // Each channel comes from one of Part1's painted grass/flower density layers.
            Color32[] coverage = ReadCoverage(source);
            DetailCoverage = new Texture2D(CoverageSize, CoverageSize, TextureFormat.RGBA32, true, true)
            {
                name = "Part1 flight vegetation coverage",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            DetailCoverage.SetPixels32(coverage);
            DetailCoverage.Apply(true, true);

            TreePrototype[] trees = source.treePrototypes;
            TreeInstance[] instances = source.treeInstances;
            DetailPrototype[] details = source.detailPrototypes;
            int detailCount = Mathf.Min(details.Length, 3);
            int speciesCount = trees.Length + detailCount;
            var parts = new DrawPart[speciesCount][];
            for (int i = 0; i < trees.Length; i++)
                parts[i] = GetParts(trees[i].prefab, makeMaterial, useLowestLod: true);
            for (int i = 0; i < detailCount; i++)
                parts[trees.Length + i] = GetParts(details[i].prototype, makeMaterial, useLowestLod: false);

            for (int z = 0; z < variants; z++)
            for (int x = 0; x < variants; x++)
            {
                var lists = new List<Matrix4x4>[speciesCount, BlocksPerTile * BlocksPerTile];
                Vector2 origin = new Vector2(x * tileSize, z * tileSize);
                foreach (TreeInstance instance in instances)
                {
                    Vector2 uv = new Vector2(instance.position.x, instance.position.z);
                    // The terrain warp can pull a painted instance across a tile edge.
                    for (int mirrorZ = z - 1; mirrorZ <= z + 1; mirrorZ++)
                    for (int mirrorX = x - 1; mirrorX <= x + 1; mirrorX++)
                    {
                        Vector2 point = projectSource(uv, mirrorX, mirrorZ);
                        Vector2 local = point - origin;
                        if (local.x < 0 || local.x >= tileSize || local.y < 0 || local.y >= tileSize
                            || (x == 0 && distanceToWall(point.x, point.y) < 4.5f))
                            continue;
                        Quaternion yaw = Quaternion.Euler(0f, instance.rotation * Mathf.Rad2Deg, 0f);
                        Vector3 scale = new Vector3(instance.widthScale, instance.heightScale, instance.widthScale);
                        Vector3 position = new Vector3(local.x, surfaceHeight(point.x, point.y), local.y);
                        Add(lists, instance.prototypeIndex, local, Matrix4x4.TRS(position, yaw, scale));
                    }
                }

                var random = new System.Random(22371 + 317 * x + 1213 * z);
                for (float dz = 0; dz < tileSize; dz += DetailSpacing)
                for (float dx = 0; dx < tileSize; dx += DetailSpacing)
                {
                    Vector2 local = new Vector2(dx + Range(random, 0.3f, 3.7f), dz + Range(random, 0.3f, 3.7f));
                    Vector2 point = origin + local;
                    if (x == 0 && distanceToWall(point.x, point.y) < 3.6f) continue;
                    Vector2 uv = sourceUv(point.x, point.y);
                    int row = Mathf.Min((int)(uv.y * CoverageSize), CoverageSize - 1);
                    int column = Mathf.Min((int)(uv.x * CoverageSize), CoverageSize - 1);
                    Color32 density = coverage[row * CoverageSize + column];
                    int total = density.r + density.g + density.b;
                    if (total == 0 || Range(random, 0, 760f) > total) continue;

                    float pick = Range(random, 0, total);
                    int layer = pick < density.r ? 0 : pick < density.r + density.g ? 1 : 2;
                    if (layer >= detailCount) continue;
                    DetailPrototype detail = details[layer];
                    float width = Range(random, detail.minWidth, detail.maxWidth) * 1.5f;
                    float height = Range(random, detail.minHeight, detail.maxHeight) * 1.4f;
                    Vector3 position = new Vector3(local.x, surfaceHeight(point.x, point.y) - 0.03f, local.y);
                    var matrix = Matrix4x4.TRS(position, Quaternion.Euler(0, Range(random, 0, 360f), 0),
                        new Vector3(width, height, width));
                    Add(lists, trees.Length + layer, local, matrix);
                }

                var batches = new List<Batch>();
                for (int block = 0; block < BlocksPerTile * BlocksPerTile; block++)
                for (int species = 0; species < speciesCount; species++)
                {
                    if (lists[species, block] == null || parts[species].Length == 0) continue;
                    batches.Add(new Batch
                    {
                        Parts = parts[species],
                        LocalMatrices = lists[species, block].ToArray(),
                        Center = new Vector2(block % BlocksPerTile + 0.5f, block / BlocksPerTile + 0.5f) * _blockSize,
                        Range = species < trees.Length ? TreeRange : DetailRange
                    });
                }
                _tiles[z * variants + x] = batches.ToArray();
            }
        }

        private static Color32[] ReadCoverage(TerrainData data)
        {
            var coverage = new Color32[CoverageSize * CoverageSize];
            int width = data.detailWidth;
            int height = data.detailHeight;
            if (width == 0 || height == 0) return coverage;
            for (int layer = 0; layer < Mathf.Min(data.detailPrototypes.Length, 3); layer++)
            {
                int[,] original = data.GetDetailLayer(0, 0, width, height, layer);
                int stepX = Mathf.Max(1, width / CoverageSize);
                int stepZ = Mathf.Max(1, height / CoverageSize);
                for (int z = 0; z < CoverageSize; z++)
                for (int x = 0; x < CoverageSize; x++)
                {
                    int firstX = x * width / CoverageSize;
                    int firstZ = z * height / CoverageSize;
                    int sum = 0;
                    for (int iz = firstZ; iz < Mathf.Min(firstZ + stepZ, height); iz++)
                    for (int ix = firstX; ix < Mathf.Min(firstX + stepX, width); ix++)
                        sum += original[iz, ix];
                    byte density = (byte)Mathf.Clamp(sum / (stepX * stepZ), 0, 255);
                    int index = z * CoverageSize + x;
                    coverage[index][layer] = density;
                }
            }
            return coverage;
        }

        private DrawPart[] GetParts(GameObject prototype, Func<Material, bool, Material> makeMaterial, bool useLowestLod)
        {
            if (prototype == null) return Array.Empty<DrawPart>();
            Renderer[] renderers = prototype.GetComponentsInChildren<MeshRenderer>();
            if (useLowestLod && prototype.TryGetComponent(out LODGroup lod))
            {
                LOD[] levels = lod.GetLODs();
                if (levels.Length > 0) renderers = levels[levels.Length - 1].renderers;
            }

            var parts = new List<DrawPart>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    continue;
                Mesh mesh = filter.sharedMesh;
                Matrix4x4 local = prototype.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Material[] sourceMaterials = renderer.sharedMaterials;
                for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, sourceMaterials.Length); sub++)
                {
                    Material source = sourceMaterials[sub];
                    if (source == null) continue;
                    Material material = makeMaterial(source, source.renderQueue >= (int)RenderQueue.AlphaTest);
                    material.SetFloat("_FoliageDistance", useLowestLod ? TreeRange : DetailRange);
                    _materials.Add(material);
                    parts.Add(new DrawPart { Mesh = mesh, Submesh = sub, Material = material, LocalMatrix = local });
                }
            }
            return parts.ToArray();
        }

        private void Add(List<Matrix4x4>[,] lists, int species, Vector2 position, Matrix4x4 matrix)
        {
            int blockX = Mathf.Min((int)(position.x / _blockSize), BlocksPerTile - 1);
            int blockZ = Mathf.Min((int)(position.y / _blockSize), BlocksPerTile - 1);
            int block = blockZ * BlocksPerTile + blockX;
            if (lists[species, block] == null) lists[species, block] = new List<Matrix4x4>();
            lists[species, block].Add(matrix);
        }

        public void Draw(Transform landscape, Vector3 seat, int layer, Camera camera)
        {
            if (!SystemInfo.supportsInstancing) return;
            var view = new Vector2(seat.x, seat.z);
            int minX = Mathf.FloorToInt((seat.x - TreeRange) / _tileSize);
            int maxX = Mathf.FloorToInt((seat.x + TreeRange) / _tileSize);
            int minZ = Mathf.FloorToInt((seat.z - TreeRange) / _tileSize);
            int maxZ = Mathf.FloorToInt((seat.z + TreeRange) / _tileSize);
            float halfBlock = _blockSize * 0.5f;

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                var offset = new Vector2(x * _tileSize, z * _tileSize);
                Matrix4x4 tileToWorld = landscape.localToWorldMatrix * Matrix4x4.Translate(new Vector3(offset.x, 0, offset.y));
                int variant = Wrap(z) * _variants + Wrap(x);
                foreach (Batch batch in _tiles[variant])
                {
                    Vector2 delta = batch.Center + offset - view;
                    float nearestX = Mathf.Max(0f, Mathf.Abs(delta.x) - halfBlock);
                    float nearestZ = Mathf.Max(0f, Mathf.Abs(delta.y) - halfBlock);
                    if (nearestX * nearestX + nearestZ * nearestZ > batch.Range * batch.Range) continue;

                    foreach (DrawPart part in batch.Parts)
                    {
                        int count = batch.LocalMatrices.Length;
                        for (int i = 0; i < count; i++)
                            _drawMatrices[i] = tileToWorld * batch.LocalMatrices[i] * part.LocalMatrix;
                        Graphics.DrawMeshInstanced(part.Mesh, part.Submesh, part.Material, _drawMatrices, count,
                            null, ShadowCastingMode.Off, false, layer, camera, LightProbeUsage.Off);
                    }
                }
            }
        }

        private int Wrap(int n) => (n % _variants + _variants) % _variants;

        private static float Range(System.Random random, float min, float max) =>
            Mathf.Lerp(min, max, (float)random.NextDouble());

        public void Dispose()
        {
            foreach (Material material in _materials) FlightLandscape.Release(material);
            FlightLandscape.Release(DetailCoverage);
        }
    }
}
