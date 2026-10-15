using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VrBlinkEffect))]
public sealed class VrBlinkEffectEditor : Editor
{
    public override bool RequiresConstantRepaint() => ((VrBlinkEffect)target).IsAnimating;

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        if (!Application.isPlaying && target != null)
            ((VrBlinkEffect)target).ResetBlink();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (target != null) ((VrBlinkEffect)target).ResetBlink();
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VrBlinkEffect effect = (VrBlinkEffect)target;
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "0 = fully open, 1 = fully closed. The slider follows the actual animation. Scrubbing stops it. Blink buttons restart from zero in Edit Mode and Play Mode. View the effect in the Game view.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        float nextAmount = EditorGUILayout.Slider("Blink Amount", effect.Amount, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            SetPreview(effect, nextAmount);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open (0)")) SetPreview(effect, 0f);
            if (GUILayout.Button("Half (0.5)")) SetPreview(effect, 0.5f);
            if (GUILayout.Button("Closed (1)")) SetPreview(effect, 1f);
        }

        using (new EditorGUI.DisabledScope(!effect.isActiveAndEnabled))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Half Blink")) effect.HalfBlink();
                if (GUILayout.Button("Full Blink")) effect.FullBlink();
            }
        }
        if (GUILayout.Button("Stop & Reset (Fully Open)"))
        {
            effect.ResetBlink();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    private void SetPreview(VrBlinkEffect effect, float value)
    {
        effect.PreviewAmount(value);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        Repaint();
    }
}
