using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Greatwall.Flight
{
    internal sealed class FlightWallSections
    {
        // Connection profile of Assets/model/budao-fix-V1.prefab. The deck rises
        // 1.6 m through the original module. Remove that baseline before fitting the hill.
        private const float DeckX = 91.2174f;
        private const float StartZ = -14.991178f;
        private const float EndZ = 13.008813f;
        private const float StartDeckY = 6.1641f;
        private const float EndDeckY = 7.7641f;

        private sealed class SourcePart
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] UV;
            public int[] Triangles;
        }

        private readonly SourcePart[] _parts;
        public Material[] Materials { get; }

        public FlightWallSections(GameObject prefab, Func<Material, Material> makeMaterial)
        {
            var parts = new List<SourcePart>();
            var materials = new List<Material>();
            foreach (MeshFilter part in prefab.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = part.sharedMesh;
                if (mesh == null || !mesh.isReadable || !part.TryGetComponent(out MeshRenderer renderer))
                    continue;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var matrix = prefab.transform.worldToLocalMatrix * part.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                    normals[i] = matrix.MultiplyVector(normals[i]).normalized;
                }
                var uv = mesh.uv;
                var sourceMaterials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    parts.Add(new SourcePart
                    {
                        Vertices = vertices, Normals = normals, UV = uv,
                        Triangles = mesh.GetTriangles(submesh)
                    });
                    materials.Add(makeMaterial(sourceMaterials[submesh]));
                }
            }
            _parts = parts.ToArray();
            Materials = materials.ToArray();
            if (_parts.Length == 0)
                Debug.LogWarning($"[Flight] 无法读取城墙模型 {prefab.name} 的网格。", prefab);
        }

        public Mesh BendAlong(float start, float length, Func<float, Vector3> wallPoint)
        {
            int sections = Mathf.RoundToInt(length / (EndZ - StartZ));
            float sectionLength = length / sections;
            int distanceSamples = Mathf.CeilToInt(length);
            var positions = new Vector3[distanceSamples + 1];
            var headings = new Quaternion[positions.Length];
            for (int i = 0; i <= distanceSamples; i++)
            {
                float distance = start + length * i / distanceSamples;
                positions[i] = wallPoint(distance) - Vector3.forward * start;
                Vector3 forward = wallPoint(distance + 0.3f) - wallPoint(distance - 0.3f);
                forward.y = 0f;
                headings[i] = Quaternion.LookRotation(forward);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var submeshes = new int[_parts.Length][];
            for (int partIndex = 0; partIndex < _parts.Length; partIndex++)
            {
                SourcePart part = _parts[partIndex];
                var triangles = new int[part.Triangles.Length * sections];
                for (int section = 0; section < sections; section++)
                {
                    int vertexStart = vertices.Count;
                    for (int i = 0; i < part.Vertices.Length; i++)
                    {
                        Vector3 source = part.Vertices[i];
                        float progress = Mathf.InverseLerp(StartZ, EndZ, source.z);
                        float distance = (section + progress) * sectionLength;
                        float sample = distance / length * distanceSamples;
                        int lower = Mathf.Min((int)sample, distanceSamples - 1);
                        float blend = sample - lower;
                        Vector3 center = Vector3.Lerp(positions[lower], positions[lower + 1], blend);
                        Quaternion heading = Quaternion.Lerp(headings[lower], headings[lower + 1], blend);
                        float elevation = source.y - Mathf.Lerp(StartDeckY, EndDeckY, progress);
                        // Keep the imported wall foot below the ground on a cross slope.
                        elevation -= 3f * (1f - Mathf.SmoothStep(0f, 1f, (source.y - 1.17f) / 3f));
                        vertices.Add(center + heading * new Vector3(source.x - DeckX, elevation, 0f));
                        normals.Add(heading * part.Normals[i]);
                        uv.Add(part.UV[i]);
                    }
                    for (int i = 0; i < part.Triangles.Length; i++)
                        triangles[section * part.Triangles.Length + i] = vertexStart + part.Triangles[i];
                }
                submeshes[partIndex] = triangles;
            }
            var mesh = new Mesh { name = "Flight · budao-fix-V1", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++)
                mesh.SetTriangles(submeshes[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
