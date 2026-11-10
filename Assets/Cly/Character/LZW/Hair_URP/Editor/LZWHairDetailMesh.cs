using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cly.LZW.Editor
{
    public static class LZWHairDetailMesh
    {
        const string Root=LZWHairBuilder.Root;
        public const string MeshPath=Root+"/Models/Hair_S_SlickBack_DetailedBacking.asset";
        public static bool Available=>File.Exists(Root+"/Source/LZW_HairBacking_Height.png");
        [MenuItem("Tools/Cly/LZW/Rebuild Detailed Hair Mesh")]
        public static void Build()
        {
            if(!Available) throw new Exception("Missing source Groom depth texture.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/Hair_S_SlickBack_Helmet_LOD5.fbx");
            var filter=source.GetComponentInChildren<MeshFilter>();
            var original=filter.sharedMesh;
            if(original.subMeshCount!=1) throw new Exception("Unexpected backing submesh layout.");
            var positions=new List<Vector3>(original.vertices);
            var normals=new List<Vector3>(original.normals);
            var uv=new List<Vector2>(original.uv);
            var tangents=new List<Vector4>(original.tangents);
            if(normals.Count!=positions.Count || uv.Count!=positions.Count || tangents.Count!=positions.Count)
                throw new Exception("Source backing is missing vertex attributes.");
            var triangles=new List<int>(original.triangles);
            for(int level=0;level<2;level++)
            {
                var edges=new Dictionary<ulong,int>();
                int Mid(int a,int b)
                {
                    uint low=(uint)Math.Min(a,b), high=(uint)Math.Max(a,b);
                    ulong key=((ulong)low<<32)|high;
                    if(edges.TryGetValue(key,out var index)) return index;
                    index=positions.Count; edges.Add(key,index);
                    positions.Add((positions[a]+positions[b])*.5f);
                    normals.Add((normals[a]+normals[b]).normalized);
                    uv.Add((uv[a]+uv[b])*.5f);
                    var t=(tangents[a]+tangents[b])*.5f;
                    var direction=new Vector3(t.x,t.y,t.z).normalized;
                    tangents.Add(new Vector4(direction.x,direction.y,direction.z,tangents[a].w));
                    return index;
                }
                var next=new List<int>(triangles.Count*4);
                for(int i=0;i<triangles.Count;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    int ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);
                    next.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});
                }
                triangles=next;
            }
            var height=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                if(!height.LoadImage(File.ReadAllBytes(Root+"/Source/LZW_HairBacking_Height.png"))) throw new Exception("Cannot decode Groom depth.");
                for(int i=0;i<positions.Count;i++)
                {
                    float depth=height.GetPixelBilinear(uv[i].x,uv[i].y).r;
                    float metresPerUnit=filter.transform.TransformVector(normals[i]).magnitude;
                    positions[i]+=normals[i]*((depth-.5f)*.0012f/Mathf.Max(metresPerUnit,1e-6f));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(height); }
            var result=new Mesh{name="LZW_Hair_DetailedBacking_PreservedNormals",indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            result.SetVertices(positions); result.SetNormals(normals); result.SetUVs(0,uv);
            result.SetTangents(tangents); result.SetTriangles(triangles,0); result.RecalculateBounds();
            // Keep the authored normals/tangents: recalculating or FBX conversion
            // changes the inset of small edge cards and exposes stray fragments.
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if(existing) {EditorUtility.CopySerialized(result,existing); UnityEngine.Object.DestroyImmediate(result); EditorUtility.SetDirty(existing);}
            else AssetDatabase.CreateAsset(result,MeshPath);
            File.WriteAllText(Root+"/detail_mesh_validation.txt","SourceTriangles="+original.triangles.Length/3+"\nDetailedTriangles="+triangles.Count/3+"\nVertices="+positions.Count+"\nAuthored normals, tangents and UV preserved/interpolated\nDepth relief=1.2mm peak-to-peak from source Groom\n");
            AssetDatabase.SaveAssets();
        }
    }
}
