using UnityEngine;
using UnityEditor;
using System.IO;

public class PivotEditor : EditorWindow
{
    private GameObject selectedObject;
    private MeshFilter meshFilter;
    private Vector3 pivotOffset = Vector3.zero;
    private Vector3 rotation = Vector3.zero;
    private Vector3 scale = Vector3.one;
    private bool showGizmo = true;
    private string meshName = "NewMesh";
    
    // UI State
    private Vector2 scrollPos;
    private bool showAdvanced = false;
    
    // Colors
    private Color headerColor = new Color(0.2f, 0.3f, 0.4f);
    private Color sectionColor = new Color(0.25f, 0.25f, 0.25f);
    private Color accentColor = new Color(0.3f, 0.6f, 0.9f);
    
    [MenuItem("Tools/Pivot, Rotation & Scale Editor")]
    public static void ShowWindow()
    {
        PivotEditor window = GetWindow<PivotEditor>("Pivot, Rotation & Scale");
        window.minSize = new Vector2(350, 450);
    }
    
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }
    
    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }
    
    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        // Header
        DrawHeader();
        
        EditorGUILayout.Space(10);
        
        // Object Selection Section
        DrawObjectSelection();
        
        if (selectedObject == null)
        {
            EditorGUILayout.EndScrollView();
            return;
        }
        
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            DrawHelpBox("Selected object must have a MeshFilter with a valid mesh.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }
        
        EditorGUILayout.Space(5);
        
        // Pivot Section
        DrawPivotSection();
        
        EditorGUILayout.Space(5);
        
        // Rotation Section
        DrawRotationSection();
        
        EditorGUILayout.Space(5);
        
        // Scale Section
        DrawScaleSection();
        
        EditorGUILayout.Space(5);
        
        // Preview Section
        DrawPreviewSection();
        
        EditorGUILayout.Space(5);
        
        // Output Section
        DrawOutputSection();
        
        EditorGUILayout.Space(10);
        
        // Bake Button
        DrawBakeButton();
        
        EditorGUILayout.Space(10);
        
        EditorGUILayout.EndScrollView();
        
        SceneView.RepaintAll();
    }
    
    private void DrawHeader()
    {
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        
        GUI.backgroundColor = headerColor;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = Color.white;
        
        GUILayout.Space(8);
        GUILayout.Label("✦ PIVOT, ROTATION & SCALE EDITOR ✦", headerStyle);
        GUILayout.Space(8);
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawObjectSelection()
    {
        DrawSectionHeader("🎯 Target Object");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        GameObject newSelection = (GameObject)EditorGUILayout.ObjectField(
            "GameObject", selectedObject, typeof(GameObject), true);
        
        if (newSelection != selectedObject)
        {
            selectedObject = newSelection;
            if (selectedObject != null)
            {
                meshFilter = selectedObject.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    meshName = meshFilter.sharedMesh.name + "_Modified";
                }
            }
        }
        
        if (selectedObject != null && meshFilter != null && meshFilter.sharedMesh != null)
        {
            EditorGUILayout.Space(3);
            GUI.enabled = false;
            EditorGUILayout.TextField("Mesh", meshFilter.sharedMesh.name);
            EditorGUILayout.IntField("Vertices", meshFilter.sharedMesh.vertexCount);
            GUI.enabled = true;
        }
        else if (selectedObject == null)
        {
            DrawHelpBox("Select a GameObject with a MeshFilter component", MessageType.Info);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawPivotSection()
    {
        DrawSectionHeader("📍 Pivot Position");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Current pivot display
        if (selectedObject != null)
        {
            EditorGUILayout.LabelField("Current World Position", 
                selectedObject.transform.position.ToString("F3"), EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
        }
        
        // Offset controls
        EditorGUILayout.LabelField("Local Offset", EditorStyles.boldLabel);
        pivotOffset = EditorGUILayout.Vector3Field("", pivotOffset);
        
        EditorGUILayout.Space(5);
        
        // Quick preset buttons
        EditorGUILayout.BeginHorizontal();
        
        if (DrawColoredButton("Center", new Color(0.4f, 0.7f, 0.4f)))
            pivotOffset = GetMeshCenter();
        
        if (DrawColoredButton("Bottom", new Color(0.4f, 0.6f, 0.8f)))
            pivotOffset = GetBottomCenter();
        
        if (DrawColoredButton("Reset", new Color(0.8f, 0.5f, 0.4f)))
            pivotOffset = Vector3.zero;
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawRotationSection()
    {
        DrawSectionHeader("🔄 Rotation");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Euler Angles (Degrees)", EditorStyles.boldLabel);
        rotation = EditorGUILayout.Vector3Field("", rotation);
        
        EditorGUILayout.Space(5);
        
        // Quick rotation presets
        EditorGUILayout.BeginHorizontal();
        
        if (DrawColoredButton("90° X", new Color(0.9f, 0.4f, 0.4f)))
            rotation.x += 90f;
        
        if (DrawColoredButton("90° Y", new Color(0.4f, 0.9f, 0.4f)))
            rotation.y += 90f;
        
        if (DrawColoredButton("90° Z", new Color(0.4f, 0.4f, 0.9f)))
            rotation.z += 90f;
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(3);
        
        EditorGUILayout.BeginHorizontal();
        
        if (DrawColoredButton("180° Y", new Color(0.5f, 0.8f, 0.5f)))
            rotation.y += 180f;
        
        if (DrawColoredButton("Reset", new Color(0.8f, 0.5f, 0.4f)))
            rotation = Vector3.zero;
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawScaleSection()
    {
        DrawSectionHeader("📏 Scale");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Scale Factor", EditorStyles.boldLabel);
        scale = EditorGUILayout.Vector3Field("", scale);
        
        EditorGUILayout.Space(5);
        
        // Quick scale presets
        EditorGUILayout.BeginHorizontal();
        
        if (DrawColoredButton("×0.5", new Color(0.7f, 0.5f, 0.9f)))
            scale *= 0.5f;
        
        if (DrawColoredButton("×2", new Color(0.5f, 0.7f, 0.9f)))
            scale *= 2f;
        
        if (DrawColoredButton("Uniform", new Color(0.7f, 0.7f, 0.5f)))
        {
            float avg = (scale.x + scale.y + scale.z) / 3f;
            scale = Vector3.one * avg;
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(3);
        
        EditorGUILayout.BeginHorizontal();
        
        if (DrawColoredButton("Invert", new Color(0.9f, 0.6f, 0.5f)))
            scale = new Vector3(1f/scale.x, 1f/scale.y, 1f/scale.z);
        
        if (DrawColoredButton("Reset", new Color(0.8f, 0.5f, 0.4f)))
            scale = Vector3.one;
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawPreviewSection()
    {
        DrawSectionHeader("👁 Preview");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        showGizmo = EditorGUILayout.Toggle("Show Preview Gizmo", showGizmo);
        
        if (showGizmo)
        {
            EditorGUILayout.HelpBox("Yellow cross shows new pivot position\nCyan sphere marks the exact point", MessageType.None);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawOutputSection()
    {
        DrawSectionHeader("💾 Output Settings");
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        meshName = EditorGUILayout.TextField("Mesh Name", meshName);
        
        EditorGUILayout.Space(3);
        
        EditorGUILayout.LabelField("Save Location:", "Assets/Meshes/", EditorStyles.miniLabel);
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawBakeButton()
    {
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            fixedHeight = 45
        };
        
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        
        if (GUILayout.Button("⚡ BAKE MESH WITH CHANGES ⚡", buttonStyle))
        {
            BakeMeshWithNewPivot();
        }
        
        GUI.backgroundColor = Color.white;
        
        EditorGUILayout.HelpBox(
            "• New mesh will be saved to Assets/Meshes/\n" +
            "• Pivot, rotation, and scale will be baked into mesh vertices\n" +
            "• Object position will be adjusted to maintain visual position\n" +
            "• Original mesh will remain unchanged",
            MessageType.Info);
    }
    
    private void DrawSectionHeader(string title)
    {
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            normal = { textColor = accentColor }
        };
        
        EditorGUILayout.LabelField(title, headerStyle);
    }
    
    private bool DrawColoredButton(string label, Color color)
    {
        GUI.backgroundColor = color;
        bool result = GUILayout.Button(label);
        GUI.backgroundColor = Color.white;
        return result;
    }
    
    private void DrawHelpBox(string message, MessageType type)
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(message, type);
        EditorGUILayout.Space(5);
    }
    
    private Vector3 GetMeshCenter()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null) return Vector3.zero;
        return -meshFilter.sharedMesh.bounds.center;
    }
    
    private Vector3 GetBottomCenter()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null) return Vector3.zero;
        Bounds bounds = meshFilter.sharedMesh.bounds;
        return new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
    }
    
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!showGizmo || selectedObject == null || meshFilter == null) return;
        
        // Calculate world pivot position
        Vector3 scaledOffset = Vector3.Scale(pivotOffset, selectedObject.transform.lossyScale);
        Vector3 worldPivotPos = selectedObject.transform.position + 
            selectedObject.transform.rotation * scaledOffset;
        
        // Draw axes
        float size = HandleUtility.GetHandleSize(worldPivotPos) * 2f;
        
        Handles.color = new Color(1f, 0.8f, 0f, 0.9f); // Yellow
        Handles.DrawLine(worldPivotPos + Vector3.up * size, worldPivotPos - Vector3.up * size, 10f);
        Handles.DrawLine(worldPivotPos + Vector3.right * size, worldPivotPos - Vector3.right * size, 10f);
        Handles.DrawLine(worldPivotPos + Vector3.forward * size, worldPivotPos - Vector3.forward * size, 10f);
        
        // Draw sphere at pivot
        Handles.color = new Color(0f, 0.8f, 1f, 0.8f); // Cyan
        Handles.SphereHandleCap(0, worldPivotPos, Quaternion.identity, size * 0.3f, EventType.Repaint);
        
        // Draw rotation preview
        if (rotation != Vector3.zero)
        {
            Quaternion rot = Quaternion.Euler(rotation);
            Handles.color = new Color(0.5f, 1f, 0.5f, 0.5f);
            Handles.ArrowHandleCap(0, worldPivotPos, rot * Quaternion.LookRotation(Vector3.forward), size * 1.5f, EventType.Repaint);
        }
        
        // Draw labels
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        
        Handles.Label(worldPivotPos + Vector3.up * size * 2.5f, "New Pivot", labelStyle);
        
        if (rotation != Vector3.zero)
        {
            Handles.Label(worldPivotPos + Vector3.up * size * 3.5f, 
                $"Rot: ({rotation.x:F0}°, {rotation.y:F0}°, {rotation.z:F0}°)", labelStyle);
        }
        
        if (scale != Vector3.one)
        {
            float labelOffset = rotation != Vector3.zero ? 4.5f : 3.5f;
            Handles.Label(worldPivotPos + Vector3.up * size * labelOffset, 
                $"Scale: ({scale.x:F2}, {scale.y:F2}, {scale.z:F2})", labelStyle);
        }
    }
    
    private void BakeMeshWithNewPivot()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "No valid mesh found!", "OK");
            return;
        }
        
        Mesh originalMesh = meshFilter.sharedMesh;
        Mesh newMesh = new Mesh();
        newMesh.name = meshName;
        
        // Copy mesh data
        Vector3[] vertices = originalMesh.vertices;
        Vector3[] normals = originalMesh.normals;
        Vector4[] tangents = originalMesh.tangents;
        Vector2[] uv = originalMesh.uv;
        Vector2[] uv2 = originalMesh.uv2;
        int[] triangles = originalMesh.triangles;
        Color[] colors = originalMesh.colors;
        
        // Create rotation quaternion
        Quaternion rot = Quaternion.Euler(rotation);
        
        // Apply transformations to vertices: scale, rotate, then pivot offset
        for (int i = 0; i < vertices.Length; i++)
        {
            // First apply scale
            vertices[i] = Vector3.Scale(vertices[i], scale);
            // Then rotate around origin
            vertices[i] = rot * vertices[i];
            // Finally apply pivot offset
            vertices[i] -= pivotOffset;
        }
        
        // Rotate normals
        if (normals != null && normals.Length > 0)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = rot * normals[i];
            }
        }
        
        // Rotate tangents
        if (tangents != null && tangents.Length > 0)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 tangentVec = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangentVec = rot * tangentVec;
                tangents[i] = new Vector4(tangentVec.x, tangentVec.y, tangentVec.z, tangents[i].w);
            }
        }
        
        // Apply to new mesh
        newMesh.vertices = vertices;
        newMesh.normals = normals;
        newMesh.tangents = tangents;
        newMesh.uv = uv;
        newMesh.uv2 = uv2;
        newMesh.triangles = triangles;
        newMesh.colors = colors;
        
        newMesh.RecalculateBounds();
        
        // Save mesh asset
        string folderPath = "Assets/Meshes";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Meshes");
        }
        
        string path = $"{folderPath}/{meshName}.asset";
        path = AssetDatabase.GenerateUniqueAssetPath(path);
        
        AssetDatabase.CreateAsset(newMesh, path);
        AssetDatabase.SaveAssets();
        
        // Update object
        Undo.RecordObject(meshFilter, "Change Mesh Pivot, Rotation and Scale");
        Undo.RecordObject(selectedObject.transform, "Adjust Transform");
        
        // Calculate the combined scale effect
        Vector3 originalScale = selectedObject.transform.lossyScale;
        Vector3 bakedScale = Vector3.Scale(originalScale, scale);
        
        // Adjust world position and rotation to maintain visual position
        Vector3 scaledPivotOffset = Vector3.Scale(pivotOffset, bakedScale);
        Vector3 worldPivotOffset = selectedObject.transform.rotation * scaledPivotOffset;
        
        // Apply rotation to object transform
        selectedObject.transform.rotation = selectedObject.transform.rotation * rot;
        
        // Adjust position to account for pivot change
        selectedObject.transform.position += selectedObject.transform.rotation * 
            Quaternion.Inverse(rot) * worldPivotOffset;
        
        // Update the transform scale to compensate for baked scale
        // We need to divide by the baked scale to maintain visual size
        selectedObject.transform.localScale = new Vector3(
            selectedObject.transform.localScale.x / scale.x,
            selectedObject.transform.localScale.y / scale.y,
            selectedObject.transform.localScale.z / scale.z
        );
        
        // Apply new mesh
        meshFilter.sharedMesh = newMesh;
        
        EditorUtility.DisplayDialog("Success! 🎉", 
            $"Mesh baked successfully with pivot, rotation, and scale changes!\n\nSaved to: {path}", "Awesome!");
        
        Selection.activeObject = newMesh;
        EditorGUIUtility.PingObject(newMesh);
    }
}