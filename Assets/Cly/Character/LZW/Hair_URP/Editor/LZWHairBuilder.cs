using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cly.LZW.Editor
{
    public static class LZWHairBuilder
    {
        public const string Root="Assets/Cly/Character/LZW/Hair_URP";
        public const string Body="Assets/Cly/Character/LZW/Materials_URP/Prefabs/LZW_URP.prefab";
        const string Attachment=Root+"/Prefabs/LZW_Hair_HeadAttachment.prefab";
        [MenuItem("Tools/Cly/LZW/Build Hair Materials and Prefab")]
        public static void Build()
        {
            foreach(var path in Directory.GetFiles(Root+"/Textures","*.png"))
            {
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(!importer) continue;
                bool color=path.Contains("BaseColorOpacity");
                importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=color;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency=color;
                importer.mipmapEnabled=true;
                importer.wrapMode=TextureWrapMode.Clamp;
                importer.maxTextureSize=2048;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.mipMapsPreserveCoverage=color;
                importer.alphaTestReferenceValue=.28f;
                importer.isReadable=false;
                importer.SaveAndReimport();
            }
            var shader=Shader.Find("Cly/LZW/Hair Cards URP");
            if(!shader) throw new Exception("Hair shader not imported.");
            var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message).ToArray();
            if(errors.Length>0) throw new Exception(string.Join("\n",errors));
            Directory.CreateDirectory(Root+"/Materials"); Directory.CreateDirectory(Root+"/Prefabs");
            AssetDatabase.Refresh();
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/M_LZW_Hair_SaltPepper.mat");
            if(!material)
            {
                material=new Material(shader){name="M_LZW_Hair_SaltPepper",enableInstancing=true,doubleSidedGI=true};
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/LZW_Hair_BaseColorOpacity.png"));
                material.SetTexture("_FlowMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/LZW_Hair_FlowRoot.png"));
                material.SetFloat("_Roughness",.4f);
                material.SetFloat("_Cutoff",.28f);
                material.renderQueue=(int)RenderQueue.AlphaTest;
                AssetDatabase.CreateAsset(material,Root+"/Materials/M_LZW_Hair_SaltPepper.mat");
            }
            var modelPath=Root+"/Models/Hair_S_SlickBack_LOD1.fbx";
            var importerModel=AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if(importerModel && importerModel.materialImportMode!=ModelImporterMaterialImportMode.None)
            {
                importerModel.materialImportMode=ModelImporterMaterialImportMode.None;
                importerModel.importAnimation=false;
                importerModel.SaveAndReimport();
            }
            var body=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Body)) as GameObject;
            if(!body) throw new Exception("Missing body prefab.");
            try
            {
                var head=body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="head");
                if(!head) throw new Exception("Missing head bone.");
                var hair=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                hair.name="LZW_Hair";
                foreach(var renderer in hair.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                }
                var bounds=hair.GetComponentInChildren<Renderer>().bounds;
                Debug.Log("LZW_HAIR_SOURCE_BOUNDS "+bounds+" head="+head.position+" root="+body.transform.localEulerAngles);
                hair.transform.SetParent(head,true);
                if(!File.Exists(Attachment)) PrefabUtility.SaveAsPrefabAsset(hair,Attachment);
                body.name="LZW_URP_WithHair";
                if(!File.Exists(Root+"/Prefabs/LZW_URP_WithHair.prefab")) PrefabUtility.SaveAsPrefabAsset(body,Root+"/Prefabs/LZW_URP_WithHair.prefab");
                AssetDatabase.SaveAssets();
                var mesh=hair.GetComponentInChildren<MeshFilter>().sharedMesh;
                File.WriteAllText(Root+"/validation.txt","Unity="+Application.unityVersion+"\nHair shader="+shader.name+"\nTriangles="+(mesh.triangles.Length/3)+"\nHairBounds="+bounds+"\nHead="+head.position+"\nShaderErrors="+errors.Length);
            }
            finally {UnityEngine.Object.DestroyImmediate(body);}
        }
        [MenuItem("Tools/Cly/LZW/Add Hair to Selected Character")]
        public static void AddToSelected()
        {
            var root=Selection.activeGameObject;
            if(!root || !root.scene.IsValid()) throw new Exception("Select the LZW character root in Hierarchy.");
            var head=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="head");
            if(!head) throw new Exception("Selected character has no head bone.");
            if(head.Find("LZW_Hair")) {Debug.Log("LZW hair is already attached.");return;}
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Attachment);
            if(!prefab) throw new Exception("Build hair assets first.");
            var hair=PrefabUtility.InstantiatePrefab(prefab,head) as GameObject;
            Undo.RegisterCreatedObjectUndo(hair,"Add LZW hair");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        }
    }
}
