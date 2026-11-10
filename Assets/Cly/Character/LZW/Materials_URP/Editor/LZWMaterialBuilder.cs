using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cly.LZW.Editor
{
    // Generates new assets only. Existing scene objects are changed only by the explicit menu action.
    public static class LZWMaterialBuilder
    {
        public const string Root = "Assets/Cly/Character/LZW/Materials_URP";
        public const string Model = "Assets/Cly/Character/LZW/LZW_Assembled_ue.FBX";
        [Serializable] public class Spec { public string name, slot, role, color, normal, roughness; public float roughnessValue = .5f; public float tintR = 1, tintG = 1, tintB = 1; public float uvOffsetX, uvOffsetY; }
        [Serializable] public class Manifest { public Spec[] materials; public string[] normalMaps; public string[] linearMaps; }
        [Serializable] class Check { public string unityVersion, pipeline, prefab; public int materialCount, rendererCount, assignedSlots; public string[] errors, warnings, rendererSlots; }

        [MenuItem("Tools/Cly/LZW/Build URP Materials and Prefab")]
        public static void Build()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/material_manifest.json"));
            ConfigureTextures(manifest);
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            var shader = Shader.Find("Cly/LZW/Character URP");
            if (shader == null) throw new InvalidOperationException("LZW URP shader was not imported.");
            var shaderErrors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToList();
            if (shaderErrors.Count > 0) throw new InvalidOperationException(string.Join("\n", shaderErrors));
            var materials = new List<Material>();
            foreach (var spec in manifest.materials)
            {
                string path = Root + "/Materials/" + spec.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                // Preserve user tuning on later runs.
                if (mat == null)
                {
                    mat = new Material(shader) { name = spec.name, enableInstancing = true };
                    if (spec.role == "hidden") ConfigureHidden(mat);
                    else if (spec.role == "wet") ConfigureWet(mat);
                    else
                    {
                        SetTexture(mat, "_BaseMap", spec.color);
                        SetTexture(mat, "_BumpMap", spec.normal);
                        SetTexture(mat, "_RoughnessMap", spec.roughness);
                        mat.SetFloat("_Roughness", spec.roughnessValue);
                        mat.SetColor("_BaseColor",new Color(spec.tintR,spec.tintG,spec.tintB,1));
                        // MetaHuman body uses UDIM tile 1002 (U=1..2). Our shared UV
                        // drives both albedo and normal, so bring that tile into 0..1.
                        mat.SetTextureOffset("_BaseMap",new Vector2(spec.uvOffsetX,spec.uvOffsetY));
                        bool skin = spec.role == "skin";
                        mat.SetFloat("_SkinWrap", skin ? .12f : 0);
                        mat.SetFloat("_Transmission", skin ? .06f : 0);
                        if (spec.role == "eye")
                        {
                            mat.EnableKeyword("_LZW_EYE");
                            mat.SetFloat("_EyeMode", 1);
                            SetTexture(mat, "_IrisMap", "Eye_Iris.png");
                            SetTexture(mat, "_IrisNormalMap", "Eye_Iris_Normal.png");
                            mat.SetColor("_BaseColor", new Color(.8322913f,.7403927f,.6944434f,1));
                            mat.SetColor("_SpecColor", new Color(.04f,.04f,.04f,1));
                        }
                    }
                    AssetDatabase.CreateAsset(mat, path);
                }
                materials.Add(mat);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (model == null) throw new InvalidOperationException("Missing model: " + Model);
            GameObject instance = UnityEngine.Object.Instantiate(model);
            try
            {
                instance.name = "LZW_URP";
                var warnings = new List<string>();
                int assigned = Assign(instance, manifest, materials.ToArray(), warnings);
                string path = Root + "/Prefabs/LZW_URP.prefab";
                // Do not overwrite an existing prefab on repeat invocations.
                if (!File.Exists(path)) PrefabUtility.SaveAsPrefabAsset(instance, path);
                AssetDatabase.SaveAssets();
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                var check = new Check {
                    unityVersion = Application.unityVersion,
                    pipeline = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in (URP materials require URP)",
                    prefab = path, materialCount = materials.Count, rendererCount = renderers.Length, assignedSlots = assigned,
                    errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray(),
                    warnings = warnings.ToArray(),
                    rendererSlots = renderers.Select(r => r.name + ": " + string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "MISSING"))).ToArray()
                };
                File.WriteAllText(Root + "/validation.json", JsonUtility.ToJson(check, true));
                Debug.Log("LZW_MATERIAL_BUILD " + JsonUtility.ToJson(check));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static void ConfigureTextures(Manifest manifest)
        {
            var normals = new HashSet<string>(manifest.normalMaps);
            var linear = new HashSet<string>(manifest.linearMaps);
            foreach (string path in Directory.GetFiles(Root + "/Textures", "*.png"))
            {
                var importer = AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if (importer == null) continue;
                string filename = Path.GetFileName(path);
                bool normal = normals.Contains(filename);
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                // Unreal exports DirectX tangent-space maps; Unity expects the opposite green axis.
                importer.flipGreenChannel = normal;
                importer.sRGBTexture = !normal && !linear.Contains(filename);
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }
        static void SetTexture(Material mat, string property, string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + file);
            if (texture == null) throw new InvalidOperationException("Missing texture: " + file);
            mat.SetTexture(property,texture);
        }
        static void ConfigureHidden(Material mat)
        {
            mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", new Color(0,0,0,0));
            mat.SetFloat("_Surface",1);
            mat.SetFloat("_Blend",0);
            mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite",0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType","Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("ShadowCaster",false);
            mat.SetShaderPassEnabled("DepthOnly",false);
            mat.SetShaderPassEnabled("DepthNormals",false);
        }
        static void ConfigureWet(Material mat)
        {
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor",new Color(1,1,1,.015f));
            mat.SetFloat("_Smoothness",.95f);
            mat.SetFloat("_Surface",1);
            mat.SetFloat("_Blend",0);
            mat.SetFloat("_SrcBlend",(float)BlendMode.One);
            mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite",0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType","Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("ShadowCaster",false);
            mat.SetShaderPassEnabled("DepthOnly",false);
            mat.SetShaderPassEnabled("DepthNormals",false);
        }
        static int Assign(GameObject root, Manifest manifest, Material[] materials, List<string> warnings)
        {
            int assigned = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i=0;i<slots.Length;i++)
                {
                    string name = slots[i] ? slots[i].name.Replace(" (Instance)","") : "";
                    int index = Array.FindIndex(manifest.materials,s => string.Equals(s.slot,name,StringComparison.OrdinalIgnoreCase));
                    if (index < 0 && slots.Length == materials.Length) index = i;
                    if (index < 0) { warnings.Add(renderer.name + ": unmapped slot " + i + " (" + name + ")"); continue; }
                    slots[i] = materials[index];
                    assigned++;
                }
                renderer.sharedMaterials = slots;
            }
            return assigned;
        }
        [MenuItem("Tools/Cly/LZW/Apply URP Materials to Selected Character")]
        public static void ApplySelected()
        {
            if (!Selection.activeGameObject) throw new InvalidOperationException("Select the LZW character in Hierarchy first.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/material_manifest.json"));
            var materials = manifest.materials.Select(s => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/"+s.name+".mat")).ToArray();
            if (materials.Any(m => m == null)) throw new InvalidOperationException("Build materials first.");
            foreach (var renderer in Selection.activeGameObject.GetComponentsInChildren<Renderer>(true)) Undo.RecordObject(renderer,"Apply LZW URP Materials");
            var warnings = new List<string>();
            int count = Assign(Selection.activeGameObject,manifest,materials,warnings);
            foreach (var r in Selection.activeGameObject.GetComponentsInChildren<Renderer>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
            Debug.Log("LZW: assigned " + count + " material slots. " + string.Join("; ",warnings));
        }
    }
}
