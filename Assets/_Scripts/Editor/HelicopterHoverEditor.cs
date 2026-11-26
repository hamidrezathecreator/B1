using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HelicopterHover))]
public sealed class HelicopterHoverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        DrawCabinAudio("helicopterSfx", "Helicopter SFX");
        DrawCabinAudio("helicopterMusic", "Helicopter Music");
        var hover = (HelicopterHover)target;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Trigger turbulence")) hover.TriggerTurbulence();
        }
    }

    void DrawCabinAudio(string propertyName, string label)
    {
        var source = serializedObject.FindProperty(propertyName).objectReferenceValue as AudioSource;
        if (source == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var clip = (AudioClip)EditorGUILayout.ObjectField("Clip", source.clip, typeof(AudioClip), false);
        float volume = EditorGUILayout.Slider("Volume", source.volume, 0f, 1f);
        float pitch = EditorGUILayout.Slider("Pitch", source.pitch, 0.1f, 3f);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(source, "Edit helicopter audio");
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        EditorUtility.SetDirty(source);
        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
    }

    void OnSceneGUI()
    {
        serializedObject.Update();
        var hover = (HelicopterHover)target;
        var centerTransform = serializedObject.FindProperty("hoverCenter").objectReferenceValue as Transform;
        if (centerTransform == null) return;
        var height = serializedObject.FindProperty("heightAboveCenter");
        bool useStartingRadius = serializedObject.FindProperty("useStartingRadius").boolValue;
        var radius = serializedObject.FindProperty("radius");
        var flightCenter = centerTransform.position + Vector3.up * height.floatValue;
        var horizontal = hover.transform.position - flightCenter;
        horizontal.y = 0f;
        float drawRadius = useStartingRadius ? horizontal.magnitude : radius.floatValue;
        bool clockwise = serializedObject.FindProperty("clockwise").boolValue;

        Handles.color = Color.cyan;
        Handles.Label(centerTransform.position, "Orbit center");
        Handles.Label(flightCenter, $"Altitude: {height.floatValue:0.0} m above center\nRadius: {drawRadius:0.0} m");
        for (int segment = 0; segment < 4; segment++)
        {
            float radians = segment * Mathf.PI * 0.5f;
            var radial = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
            var tangent = new Vector3(-radial.z, 0f, radial.x) * (clockwise ? -1f : 1f);
            Handles.ArrowHandleCap(0, flightCenter + radial * drawRadius, Quaternion.LookRotation(tangent),
                Mathf.Clamp(drawRadius * 0.04f, 0.5f, 8f), EventType.Repaint);
        }

        EditorGUI.BeginChangeCheck();
        var altitudeHandle = Handles.Slider(flightCenter, Vector3.up,
            HandleUtility.GetHandleSize(flightCenter) * 0.6f, Handles.ConeHandleCap, 0.1f);
        if (EditorGUI.EndChangeCheck())
        {
            height.floatValue = altitudeHandle.y - centerTransform.position.y;
            serializedObject.ApplyModifiedProperties();
        }
        if (!useStartingRadius)
        {
            var radiusPoint = flightCenter + Vector3.right * drawRadius;
            EditorGUI.BeginChangeCheck();
            var radiusHandle = Handles.Slider(radiusPoint, Vector3.right,
                HandleUtility.GetHandleSize(radiusPoint) * 0.6f, Handles.CubeHandleCap, 0.1f);
            if (EditorGUI.EndChangeCheck())
            {
                radius.floatValue = Mathf.Max(0.1f, radiusHandle.x - flightCenter.x);
                serializedObject.ApplyModifiedProperties();
            }
        }

        var seat = serializedObject.FindProperty("xrParent").objectReferenceValue as Transform;
        var destination = serializedObject.FindProperty("missionStartSpawn").objectReferenceValue as Transform;
        if (seat != null)
        {
            Handles.color = Color.yellow;
            Handles.Label(seat.position, "XR seat / half height / movement locked");
        }
        if (destination != null)
        {
            Handles.color = Color.green;
            Handles.Label(destination.position, "Tutorial arrival / normal height");
            Handles.ArrowHandleCap(0, destination.position, destination.rotation,
                HandleUtility.GetHandleSize(destination.position), EventType.Repaint);
            if (seat != null) Handles.DrawDottedLine(seat.position, destination.position, 6f);
        }
    }
}
