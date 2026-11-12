#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sza.Editor
{
    public static class BowMaterialsSetup
    {
        private const string Folder = "Assets/sza/model/弓箭";
        private const string Generated = Folder + "/Generated";
        private const string ModelPath = Folder + "/shortbowexport.fbx";
        private static readonly string[] Names = { "Arrow", "Binding", "String", "Wood" };
        private static readonly string[] Prefixes =
        {
            "bowarrow_Arrows", "shortbow_Bindings", "shortbow_String", "shortbow_Wood"
        };

        [MenuItem("Tools/Sza/Bow/Configure Materials")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before configuring bow materials.");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Missing bow model.", ModelPath);

            // Validate the complete input set before changing any asset.
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Folder + "/" + Names[i] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new FileNotFoundException("Missing existing material.", path);
                materials.Add(Names[i], material);
                foreach (string suffix in new[] { "BaseColor", "Normal", "OcclusionRoughnessMetallic" })
                {
                    string texturePath = TexturePath(Prefixes[i], suffix);
                    if (!File.Exists(texturePath) || !(AssetImporter.GetAtPath(texturePath) is TextureImporter))
                        throw new FileNotFoundException("Missing imported bow texture.", texturePath);
                }
            }

            if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder(Folder, "Generated");
            var report = new StringBuilder("Bow materials configured (existing material GUIDs retained).\n");
            for (int i = 0; i < Names.Length; i++)
            {
                string colorPath = TexturePath(Prefixes[i], "BaseColor");
                string normalPath = TexturePath(Prefixes[i], "Normal");
                string ormPath = TexturePath(Prefixes[i], "OcclusionRoughnessMetallic");
                ConfigureSource(colorPath, TextureImporterType.Default, true);
                ConfigureSource(normalPath, TextureImporterType.NormalMap, false);
                ConfigureSource(ormPath, TextureImporterType.Default, false);

                string packedPath = Generated + "/" + Prefixes[i] + "_MetallicSmoothnessAO.1001.png";
                GeneratePackedMap(ormPath, packedPath);
                ConfigurePackedImporter(packedPath, (TextureImporter)AssetImporter.GetAtPath(ormPath));
                ConfigureMaterial(materials[Names[i]], shader,
                    LoadTexture(colorPath), LoadTexture(normalPath), LoadTexture(packedPath));

                report.AppendLine(Names[i] + ".mat:");
                report.AppendLine("  _BaseMap <- " + colorPath);
                report.AppendLine("  _BumpMap <- " + normalPath);
                report.AppendLine("  _MetallicGlossMap + _OcclusionMap <- " + packedPath);
            }

            bool remapped = false;
            var remaps = importer.GetExternalObjectMap();
            foreach (var pair in materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                if (remaps.TryGetValue(id, out var current) && current == pair.Value) continue;
                importer.AddRemap(id, pair.Value);
                remapped = true;
            }
            if (remapped) importer.SaveAndReimport();
            VerifyModel(materials, report);
            report.AppendLine("Packed RGBA = (ORM.B metallic, ORM.R AO, 0, 255 - ORM.G roughness).");
            report.AppendLine("Model importer " + (remapped ? "remapped and reimported." : "already correct; not reimported."));
            Debug.Log(report.ToString());
        }

        private static string TexturePath(string prefix, string suffix)
        {
            return Folder + "/" + prefix + "_" + suffix + ".1001.png";
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Could not load texture: " + path);
            return texture;
        }

        private static void ConfigureSource(string path, TextureImporterType type, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = type == TextureImporterType.NormalMap;
            bool changed = importer.textureType != type || importer.sRGBTexture != srgb ||
                (normal && (importer.convertToNormalmap || importer.flipGreenChannel));
            if (!changed) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            if (normal)
            {
                importer.convertToNormalmap = false;
                importer.flipGreenChannel = false; // glTF/OpenGL tangent-space normals.
            }
            // The original importer's readability, compression and sampler settings stay intact.
            importer.SaveAndReimport();
        }

        private static Color32 PackOrmPixel(Color32 orm)
        {
            // URP Lit samples metallic from R, occlusion from G and smoothness from A.
            return new Color32(orm.b, orm.r, 0, (byte)(255 - orm.g));
        }

        private static void GeneratePackedMap(string sourcePath, string destinationPath)
        {
            Color32[] pixels;
            int width, height;
            // Decode source bytes into a private, linear, readable texture. Reading the imported
            // (possibly compressed/sRGB/non-readable) asset would corrupt or block channel access.
            // No source PNG writes or temporary source-importer readability changes are needed.
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(sourcePath), false))
                    throw new InvalidOperationException("Could not decode ORM PNG: " + sourcePath);
                width = decoded.width;
                height = decoded.height;
                pixels = decoded.GetPixels32();
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }

            // Reuse one byte-sized pixel buffer and process only one material at a time.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = PackOrmPixel(pixels[i]);
            var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                packed.SetPixels32(pixels);
                byte[] png = ImageConversion.EncodeToPNG(packed);
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("Could not encode packed ORM: " + destinationPath);
                if (File.Exists(destinationPath) && SameBytes(File.ReadAllBytes(destinationPath), png)) return;
                File.WriteAllBytes(destinationPath, png);
                AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            finally { UnityEngine.Object.DestroyImmediate(packed); }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void ConfigurePackedImporter(string path, TextureImporter sampler)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing generated texture importer: " + path);
            bool changed = importer.textureType != TextureImporterType.Default || importer.sRGBTexture ||
                importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency ||
                importer.isReadable || !importer.mipmapEnabled || importer.crunchedCompression ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                importer.maxTextureSize != sampler.maxTextureSize || importer.filterMode != sampler.filterMode ||
                importer.wrapModeU != sampler.wrapModeU || importer.wrapModeV != sampler.wrapModeV ||
                importer.anisoLevel != sampler.anisoLevel;
            if (!changed) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.crunchedCompression = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = sampler.maxTextureSize;
            importer.filterMode = sampler.filterMode;
            importer.wrapModeU = sampler.wrapModeU;
            importer.wrapModeV = sampler.wrapModeV;
            importer.anisoLevel = sampler.anisoLevel;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaterial(Material material, Shader shader, Texture2D color, Texture2D normal, Texture2D packed)
        {
            bool changed = material.shader != shader;
            if (changed) material.shader = shader;
            SetTexture("_BaseMap", color);
            SetTexture("_MainTex", color);
            SetTexture("_BumpMap", normal);
            SetTexture("_MetallicGlossMap", packed);
            SetTexture("_OcclusionMap", packed);
            SetColor("_BaseColor", Color.white);
            SetColor("_Color", Color.white);
            SetColor("_EmissionColor", Color.black); // Remove the arrow's untextured white emission.
            SetFloat("_WorkflowMode", 1f);
            SetFloat("_Metallic", 1f);
            SetFloat("_Smoothness", 1f); // Lit multiplies packed alpha by this scalar.
            SetFloat("_SmoothnessTextureChannel", 0f);
            SetFloat("_BumpScale", 1f);
            SetFloat("_OcclusionStrength", 1f);
            SetKeyword("_NORMALMAP", true);
            SetKeyword("_METALLICSPECGLOSSMAP", true);
            SetKeyword("_OCCLUSIONMAP", true);
            SetKeyword("_SPECULAR_SETUP", false);
            SetKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", false);
            SetKeyword("_EMISSION", false);
            if (material.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack)
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material); // Do not save unrelated work in progress.
            }

            void SetTexture(string property, Texture value)
            {
                if (material.GetTexture(property) == value) return;
                material.SetTexture(property, value); changed = true;
            }
            void SetColor(string property, Color value)
            {
                if (material.GetColor(property) == value) return;
                material.SetColor(property, value); changed = true;
            }
            void SetFloat(string property, float value)
            {
                if (material.GetFloat(property) == value) return;
                material.SetFloat(property, value); changed = true;
            }
            void SetKeyword(string keyword, bool enabled)
            {
                if (material.IsKeywordEnabled(keyword) == enabled) return;
                if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
                changed = true;
            }
        }

        private static void VerifyModel(Dictionary<string, Material> materials, StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Could not load bow model for verification.");
            var seen = new HashSet<Material>();
            int slots = 0;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    Material material = shared[i];
                    if (material == null || !materials.ContainsValue(material))
                        throw new InvalidOperationException("Unexpected bow renderer material: " + renderer.name + " slot " + i);
                    seen.Add(material);
                    slots++;
                    report.AppendLine("  Renderer " + renderer.name + "[" + i + "] -> " + AssetDatabase.GetAssetPath(material));
                }
            }
            if (seen.Count != materials.Count)
                throw new InvalidOperationException("Bow model does not reference all four existing materials; found " + seen.Count + ".");
            report.AppendLine("Verified " + slots + " model renderer slots against the four material assets.");
        }
    }
}
#endif
