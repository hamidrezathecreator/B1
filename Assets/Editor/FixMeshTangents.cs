using UnityEditor;
using UnityEngine;

public static class FixMeshTangents
{
    [MenuItem("Tools/Fix Tangents On Selected Meshes")]
    static void Fix()
    {
        foreach (var mesh in Selection.GetFiltered<Mesh>(SelectionMode.Assets))
        {
            // Turn Read/Write on
            var so = new SerializedObject(mesh);
            var prop = so.FindProperty("m_IsReadable");
            if (prop != null && !prop.boolValue)
            {
                prop.boolValue = true;
                so.ApplyModifiedProperties();
            }

            if (mesh.vertexCount > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // mesh.RecalculateNormals(); // uncomment only if your normals are also bad
            mesh.RecalculateTangents();

            EditorUtility.SetDirty(mesh);
            Debug.Log($"{mesh.name}: verts={mesh.vertexCount}, tangents={mesh.tangents.Length}", mesh);
        }
        AssetDatabase.SaveAssets();
    }
}