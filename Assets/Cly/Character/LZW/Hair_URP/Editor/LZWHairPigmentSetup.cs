using System;
using UnityEditor;
using UnityEngine;

namespace Cly.LZW.Editor
{
    public static class LZWHairPigmentSetup
    {
        const string Root=LZWHairBuilder.Root;
        [MenuItem("Tools/Cly/LZW/Enable Strand White Hair Control")]
        public static void Build()
        {
            Setup("M_LZW_Hair_SaltPepper","LZW_Hair_Pigment",.014f);
            Setup("M_LZW_Hair_Backing","LZW_HairBacking_Pigment",.025f);
            AssetDatabase.SaveAssets();
        }
        static void Setup(string name,string map,float black)
        {
            string path=Root+"/Textures/"+map+".png";
            var ti=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!ti) throw new Exception("Missing pigment map: "+path);
            ti.textureType=TextureImporterType.Default; ti.sRGBTexture=false;
            ti.alphaIsTransparency=false; ti.mipmapEnabled=true;
            ti.mipMapsPreserveCoverage=false;
            ti.wrapMode=TextureWrapMode.Clamp;
            ti.maxTextureSize=map.Contains("Backing")?1024:2048;
            ti.textureCompression=TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
            if(!mat || !mat.HasProperty("_PigmentMap")) throw new Exception("Missing updated hair material shader.");
            if(!mat.GetTexture("_PigmentMap"))
            {
                mat.SetFloat("_WhiteAmount",.85f); mat.SetFloat("_BlackLevel",black);
                if(map.Contains("Backing")) mat.SetFloat("_WhiteLevel",.10f);
            }
            mat.SetTexture("_PigmentMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            mat.SetFloat("_UseSeedColor",1); mat.SetFloat("_WhiteBoost",0);
            EditorUtility.SetDirty(mat);
        }
    }
}
