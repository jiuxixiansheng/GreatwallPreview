using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cly.LZW.Editor
{
    public static class LZWHairDensityUpgrade
    {
        const string Root=LZWHairBuilder.Root;
        const string Attachment=Root+"/Prefabs/LZW_Hair_HeadAttachment.prefab";
        const string Combined=Root+"/Prefabs/LZW_URP_WithHair.prefab";
        [MenuItem("Tools/Cly/LZW/Upgrade Hair Density")]
        public static void Build()
        {
            var shader=Shader.Find("Cly/LZW/Hair Cards URP");
            if(!shader) throw new Exception("Missing LZW hair shader.");
            foreach(var path in Directory.GetFiles(Root+"/Textures","*.png"))
            {
                var ti=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(!ti) continue;
                bool color=path.Contains("BaseColorOpacity");
                ti.textureType=TextureImporterType.Default;
                ti.sRGBTexture=color; ti.alphaIsTransparency=color;
                ti.mipmapEnabled=true; ti.mipMapsPreserveCoverage=color;
                ti.alphaTestReferenceValue=path.Contains("Backing")?.12f:.18f;
                ti.maxTextureSize=2048; ti.wrapMode=TextureWrapMode.Clamp;
                ti.textureCompression=TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            var cards=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/M_LZW_Hair_SaltPepper.mat");
            if(!cards) throw new Exception("Build original hair materials first.");
            cards.SetFloat("_Cutoff",.18f);
            cards.SetFloat("_SurfaceOffset",0);
            EditorUtility.SetDirty(cards);
            string backingPath=Root+"/Materials/M_LZW_Hair_Backing.mat";
            var backing=AssetDatabase.LoadAssetAtPath<Material>(backingPath);
            if(!backing)
            {
                backing=new Material(shader){name="M_LZW_Hair_Backing",enableInstancing=true,doubleSidedGI=true};
                backing.SetFloat("_WhiteLevel",.18f);
                AssetDatabase.CreateAsset(backing,backingPath);
            }
            backing.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/LZW_HairBacking_BaseColorOpacity.png"));
            backing.SetTexture("_FlowMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/LZW_HairBacking_FlowRoot.png"));
            backing.SetFloat("_Cutoff",.12f); backing.SetFloat("_Roughness",.6f);
            backing.SetFloat("_Anisotropy",.12f); backing.SetFloat("_Transmission",0);
            backing.SetFloat("_SurfaceOffset",-.004f);
            var detail=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/LZW_HairBacking_DetailNormal.png");
            if(detail)
            {
                if(!backing.GetTexture("_DetailNormalMap")) backing.SetFloat("_DetailNormalStrength",.4f);
                backing.SetTexture("_DetailNormalMap",detail);
            }
            backing.renderQueue=(int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(backing);
            if(LZWHairDetailMesh.Available) LZWHairDetailMesh.Build();
            var body=PrefabUtility.LoadPrefabContents(Combined);
            try
            {
                var head=body.GetComponentsInChildren<Transform>(true).First(t=>t.name=="head");
                var old=head.Find("LZW_Hair");
                // Reuse the existing root to keep its prefab identity and scene overrides.
                var group=old?old.gameObject:new GameObject("LZW_Hair");
                foreach(Transform child in group.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach(var component in group.GetComponents<Component>()) if(!(component is Transform)) UnityEngine.Object.DestroyImmediate(component);
                group.transform.SetParent(null,false); group.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                group.transform.localScale=Vector3.one;
                AddMesh(group,"Hair_S_SlickBack_LOD0.fbx","Outer_Strands",cards);
                var detailMesh=AssetDatabase.LoadAssetAtPath<Mesh>(LZWHairDetailMesh.MeshPath);
                bool detailed=detailMesh;
                var inner=AddMesh(group,"Hair_S_SlickBack_Helmet_LOD5.fbx","Density_Backing",backing);
                if(detailed) inner.GetComponentInChildren<MeshFilter>().sharedMesh=detailMesh;
                group.transform.SetParent(head,true);
                PrefabUtility.SaveAsPrefabAsset(group,Attachment);
                PrefabUtility.SaveAsPrefabAsset(body,Combined);
                var count=group.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3);
                File.WriteAllText(Root+"/density_validation.txt","TotalTriangles="+count+"\nSource=Original Groom LOD0 cards + "+(detailed?"depth-displaced detailed backing":"original LOD5 shell")+"\n");
                foreach(var r in group.GetComponentsInChildren<Renderer>()) Debug.Log("DENSE_HAIR_BOUNDS "+r.name+" "+r.bounds);
            }
            finally { PrefabUtility.UnloadPrefabContents(body); }
            AssetDatabase.SaveAssets();
        }
        static GameObject AddMesh(GameObject parent,string file,string name,Material material)
        {
            string path=Root+"/Models/"+file;
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer && importer.materialImportMode!=ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode=ModelImporterMaterialImportMode.None;
                importer.importAnimation=false; importer.SaveAndReimport();
            }
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!asset) throw new Exception("Missing hair model: "+path);
            var mesh=UnityEngine.Object.Instantiate(asset);
            mesh.name=name; mesh.transform.SetParent(parent.transform,true);
            foreach(var r in mesh.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode=ShadowCastingMode.TwoSided;
            }
            return mesh;
        }
        [MenuItem("Tools/Cly/LZW/Update Hair on Selected Character")]
        public static void UpdateSelected()
        {
            var root=Selection.activeGameObject;
            if(!root || !root.scene.IsValid()) throw new Exception("Select the LZW character root in Hierarchy.");
            var head=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="head");
            if(!head) throw new Exception("Selected character has no head bone.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Attachment);
            if(!prefab) throw new Exception("Missing hair attachment prefab.");
            Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Update LZW hair density");
            var old=head.Find("LZW_Hair");
            if(old) Undo.DestroyObjectImmediate(old.gameObject);
            var instance=PrefabUtility.InstantiatePrefab(prefab,head) as GameObject;
            Undo.RegisterCreatedObjectUndo(instance,"Update LZW hair density");
            Undo.CollapseUndoOperations(undo);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject=root;
        }
    }
}
