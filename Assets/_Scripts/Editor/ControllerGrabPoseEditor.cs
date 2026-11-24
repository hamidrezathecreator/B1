using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[CustomEditor(typeof(ControllerGrabPose))]
public sealed class ControllerGrabPoseEditor : Editor
{
    sealed class PreviewJoint
    {
        public Transform transform;
        public Quaternion rest;
        public string path;
        public int finger;
        public float curlDegrees;
        public int bendSegment;
        public bool motionBase;
        public Vector3 spreadAxis;
        public Vector3 twistAxis;
        public Vector3 oppositionAxis;
        public Quaternion manualOffset = Quaternion.identity;
    }

    static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
    readonly List<PreviewJoint> joints = new List<PreviewJoint>();
    readonly float[] fingerCurls = new float[5];
    readonly ControllerHandPose.FingerAdjustment[] fingerAdjustments = new ControllerHandPose.FingerAdjustment[5];
    GameObject preview;
    SkinnedMeshRenderer previewRenderer;
    Vector3 previewPosition;
    Quaternion previewRotation;
    bool leftHand;
    bool showJoints;
    bool previewDirty;
    bool showAdvanced = true;
    bool editFingerJoints;
    int selectedFinger;
    int selectedJoint;
    string[] jointNames;

    public Transform PreviewRoot => preview != null ? preview.transform : null;

    void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        StopPreview();
    }

    [MenuItem("B1/Hands/Add Grab Pose To Selected")]
    static void AddToSelected()
    {
        foreach (var selected in Selection.gameObjects)
        {
            if (EditorUtility.IsPersistent(selected)) continue;
            if (selected.GetComponent<ControllerGrabPose>() == null)
                Undo.AddComponent<ControllerGrabPose>(selected);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("handRigTemplate"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            StopPreview();
        }

        var component = (ControllerGrabPose)target;
        var saved = component.GetPose(leftHand).saved;
        EditorGUILayout.HelpBox("Preview a hand, move and rotate its wrist in the Scene view, adjust fingers, then Save Pose. " +
            "The saved pose controls object alignment and fingers while held with the grip button.", MessageType.Info);

        using (new EditorGUI.DisabledScope(Application.isPlaying || EditorUtility.IsPersistent(component)))
        {
            var side = GUILayout.Toolbar(leftHand ? 0 : 1, new[] { "Left Hand", "Right Hand" });
            if ((side == 0) != leftHand)
            {
                var wasPreviewing = preview != null;
                StopPreview();
                leftHand = side == 0;
                if (wasPreviewing) BeginPreview(leftHand);
                saved = component.GetPose(leftHand).saved;
            }

            EditorGUILayout.LabelField("Saved pose", saved ? "Ready" : "Not saved; normal grab behavior");
            if (GUILayout.Button(preview != null ? "Hide Hand Preview" : "Show Hand Preview"))
            {
                if (preview != null) StopPreview();
                else BeginPreview(leftHand);
            }

            if (preview != null)
            {
                EditorGUI.BeginChangeCheck();
                var position = EditorGUILayout.Vector3Field("Wrist Position", previewPosition);
                var rotation = EditorGUILayout.Vector3Field("Wrist Rotation", previewRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) SetPreviewPose(position, Quaternion.Euler(rotation));

                EditorGUILayout.Space();
                for (var finger = 0; finger < FingerNames.Length; finger++)
                {
                    EditorGUI.BeginChangeCheck();
                    var curl = EditorGUILayout.Slider(FingerNames[finger], fingerCurls[finger], 0f, 1f);
                    if (EditorGUI.EndChangeCheck()) SetFingerCurl(finger, curl);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Hand")) ResetFingerMovement(0f);
                    if (GUILayout.Button("Closed Hand")) ResetFingerMovement(1f);
                }

                DrawAdvancedMovement();

                showJoints = EditorGUILayout.Foldout(showJoints, "Individual Joint Rotations", true);
                if (showJoints)
                {
                    EditorGUI.indentLevel++;
                    foreach (var joint in joints)
                    {
                        EditorGUI.BeginChangeCheck();
                        var angles = EditorGUILayout.Vector3Field(joint.transform.name, joint.transform.localEulerAngles);
                        if (!EditorGUI.EndChangeCheck()) continue;
                        SetJointRotation(joint.path, Quaternion.Euler(angles));
                    }
                    EditorGUI.indentLevel--;
                }

                if (GUILayout.Button(previewDirty ? "Save Pose *" : "Save Pose")) SavePreview();
                EditorGUILayout.HelpBox(editFingerJoints
                    ? "Click a finger joint in the Scene view and rotate its rings. Turn off Edit Finger Joints to move the wrist. Save Pose keeps all joint rotations."
                    : "Use W to move the wrist and E to rotate it. Preview changes persist only after Save Pose.", MessageType.None);
            }

            if (saved && GUILayout.Button("Clear Saved Pose"))
            {
                Undo.RecordObject(component, "Clear Hand Grab Pose");
                component.GetPose(leftHand).saved = false;
                MarkChanged(component);
                StopPreview();
            }

            var grab = component.GetComponent<XRGrabInteractable>();
            if (grab.GetComponentsInChildren<Collider>().Length == 0)
            {
                EditorGUILayout.HelpBox("This object needs a collider to be grabbed.", MessageType.Warning);
                if (GUILayout.Button("Add Box Collider")) Undo.AddComponent<BoxCollider>(component.gameObject);
            }
            if (grab.selectMode != InteractableSelectMode.Single)
                EditorGUILayout.HelpBox("Saved alignment is applied to single-hand grabs. Multiple-hand grabs use the object's existing transformers.", MessageType.Info);
        }
        serializedObject.ApplyModifiedProperties();
    }

    public bool BeginPreview(bool isLeft)
    {
        StopPreview();
        leftHand = isLeft;
        var component = (ControllerGrabPose)target;
        var template = component.HandRigTemplate;
        var visual = template != null ? template.GetHandVisualPrefab(isLeft) : null;
        if (visual == null)
        {
            Debug.LogWarning("Assign a hand rig template with a hand visual prefab before previewing.", component);
            return false;
        }

        preview = new GameObject(isLeft ? "Left Hand Pose Preview" : "Right Hand Pose Preview");
        preview.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(preview, component.gameObject.scene);
        var source = Instantiate(visual, preview.transform);
        foreach (var behaviour in source.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var collider in source.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in source.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (var child in preview.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
        previewRenderer = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (previewRenderer == null || previewRenderer.sharedMesh == null)
        {
            StopPreview();
            Debug.LogWarning("The preview hand prefab needs a skinned hand mesh.", component);
            return false;
        }
        for (var parent = previewRenderer.transform; parent != null && parent != preview.transform; parent = parent.parent)
            parent.gameObject.SetActive(true);
        previewRenderer.enabled = true;
        previewRenderer.forceRenderingOff = false;
        previewRenderer.updateWhenOffscreen = true;

        var driver = source.GetComponentInChildren<XRHandSkeletonDriver>(true);
        var wrist = driver != null && driver.rootTransform != null ? driver.rootTransform : previewRenderer.rootBone;
        if (wrist == null) wrist = previewRenderer.transform;
        source.transform.rotation = preview.transform.rotation * Quaternion.Inverse(wrist.rotation) * source.transform.rotation;
        source.transform.position += preview.transform.position - wrist.position;

        if (template.HandMaterial != null)
        {
            var materials = new Material[Mathf.Max(1, previewRenderer.sharedMesh.subMeshCount)];
            for (var index = 0; index < materials.Length; index++) materials[index] = template.HandMaterial;
            previewRenderer.sharedMaterials = materials;
            if (template.HandTexture != null)
            {
                var properties = new MaterialPropertyBlock();
                if (template.HandMaterial.HasProperty("_BaseMap")) properties.SetTexture("_BaseMap", template.HandTexture);
                if (template.HandMaterial.HasProperty("_MainTex")) properties.SetTexture("_MainTex", template.HandTexture);
                previewRenderer.SetPropertyBlock(properties);
            }
        }

        var seen = new HashSet<Transform>();
        foreach (var bone in previewRenderer.bones)
        {
            if (bone == null || !seen.Add(bone)) continue;
            var boneName = bone.name.ToLowerInvariant();
            var finger = FingerIndex(boneName);
            if (finger < 0) continue;
            joints.Add(new PreviewJoint
            {
                transform = bone,
                path = AnimationUtility.CalculateTransformPath(bone, source.transform),
                rest = bone.localRotation,
                finger = finger,
                bendSegment = finger == 0 && boneName.Contains("metacarpal") ? 0 :
                    finger == 0 && boneName.Contains("proximal") ? 1 :
                    boneName.Contains("distal") ? 2 : boneName.Contains("intermediate") ? 1 :
                    boneName.Contains("proximal") ? 0 : -1,
                curlDegrees = boneName.Contains("metacarpal") || boneName.Contains("tip") || boneName.Contains("end") ? 0f :
                    finger == 0 ? 40f : boneName.Contains("proximal") ? 70f : boneName.Contains("distal") ? 60f : 85f
            });
        }

        ConfigureMovementAxes(wrist);
        jointNames = new string[joints.Count];
        for (var jointIndex = 0; jointIndex < joints.Count; jointIndex++) jointNames[jointIndex] = joints[jointIndex].transform.name;
        selectedJoint = 0;

        var pose = component.GetPose(isLeft);
        for (var finger = 0; finger < fingerCurls.Length; finger++)
        {
            fingerCurls[finger] = pose.saved && pose.fingerCurls != null && finger < pose.fingerCurls.Length ? pose.fingerCurls[finger] : 0f;
            fingerAdjustments[finger] = pose.saved && pose.fingerAdjustments != null && finger < pose.fingerAdjustments.Length
                ? pose.fingerAdjustments[finger] : default;
        }
        if (pose.saved)
            foreach (var savedJoint in pose.joints)
            {
                if (string.IsNullOrEmpty(savedJoint.path)) continue;
                var bone = source.transform.Find(savedJoint.path);
                if (bone != null) bone.localRotation = savedJoint.rotation;
            }
        foreach (var joint in joints)
            joint.manualOffset = Quaternion.Inverse(joint.rest * MovementRotation(joint)) * joint.transform.localRotation;
        SetPreviewPose(pose.position, pose.rotation);
        previewDirty = false;
        SceneView.RepaintAll();
        return true;
    }

    static int FingerIndex(string boneName)
    {
        if (boneName.Contains("thumb")) return 0;
        if (boneName.Contains("index")) return 1;
        if (boneName.Contains("middle")) return 2;
        if (boneName.Contains("ring")) return 3;
        if (boneName.Contains("little") || boneName.Contains("pinky")) return 4;
        return -1;
    }

    void ConfigureMovementAxes(Transform wrist)
    {
        var indexBase = FindMotionBase(1);
        var littleBase = FindMotionBase(4);
        var forward = indexBase != null && littleBase != null
            ? (indexBase.transform.position + littleBase.transform.position) * 0.5f - wrist.position : wrist.forward;
        var across = indexBase != null && littleBase != null
            ? indexBase.transform.position - littleBase.transform.position : wrist.right;
        var normal = Vector3.Cross(forward, across).normalized;
        if (normal.sqrMagnitude < 0.001f) normal = wrist.up;
        if (forward.sqrMagnitude < 0.001f) forward = wrist.forward;
        for (var finger = 0; finger < FingerNames.Length; finger++)
        {
            var motionBase = FindMotionBase(finger);
            if (motionBase == null) continue;
            motionBase.motionBase = true;
            motionBase.spreadAxis = motionBase.transform.InverseTransformDirection(normal).normalized;
            motionBase.oppositionAxis = motionBase.transform.InverseTransformDirection(forward).normalized;
            var direction = motionBase.transform.childCount > 0
                ? motionBase.transform.GetChild(0).position - motionBase.transform.position : forward;
            motionBase.twistAxis = motionBase.transform.InverseTransformDirection(direction).normalized;
        }
    }

    PreviewJoint FindMotionBase(int finger)
    {
        var segment = finger == 0 ? "metacarpal" : "proximal";
        return joints.Find(joint => joint.finger == finger && joint.transform.name.ToLowerInvariant().Contains(segment))
            ?? joints.Find(joint => joint.finger == finger);
    }

    Quaternion MovementRotation(PreviewJoint joint)
    {
        var adjustment = fingerAdjustments[joint.finger];
        var bend = joint.bendSegment >= 0 ? adjustment.jointBends[joint.bendSegment] : 0f;
        var rotation = Quaternion.Euler(joint.curlDegrees * fingerCurls[joint.finger] + bend, 0f, 0f);
        if (!joint.motionBase) return rotation;
        return Quaternion.AngleAxis(adjustment.spread, joint.spreadAxis) *
            Quaternion.AngleAxis(joint.finger == 0 ? adjustment.opposition : 0f, joint.oppositionAxis) *
            Quaternion.AngleAxis(adjustment.twist, joint.twistAxis) * rotation;
    }

    void ApplyFingerMovement(int finger)
    {
        foreach (var joint in joints)
            if (joint.finger == finger)
                joint.transform.localRotation = joint.rest * MovementRotation(joint) * joint.manualOffset;
        previewDirty = true;
        SceneView.RepaintAll();
        Repaint();
    }

    void DrawAdvancedMovement()
    {
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Finger Movement", true);
        if (showAdvanced)
        {
            selectedFinger = GUILayout.Toolbar(selectedFinger, FingerNames);
            var adjustment = fingerAdjustments[selectedFinger];
            EditorGUI.BeginChangeCheck();
            adjustment.spread = EditorGUILayout.Slider("Sideways / Spread", adjustment.spread, -45f, 45f);
            adjustment.twist = EditorGUILayout.Slider("Finger Roll", adjustment.twist, -90f, 90f);
            if (selectedFinger == 0)
                adjustment.opposition = EditorGUILayout.Slider("Thumb Opposition", adjustment.opposition, -90f, 90f);
            adjustment.jointBends.x = EditorGUILayout.Slider("Base Bend Offset", adjustment.jointBends.x, -90f, 120f);
            adjustment.jointBends.y = EditorGUILayout.Slider("Middle Bend Offset", adjustment.jointBends.y, -90f, 120f);
            adjustment.jointBends.z = EditorGUILayout.Slider("Tip Bend Offset", adjustment.jointBends.z, -90f, 120f);
            if (EditorGUI.EndChangeCheck()) SetFingerAdjustment(selectedFinger, adjustment);
            if (GUILayout.Button("Reset " + FingerNames[selectedFinger] + " Adjustments"))
            {
                foreach (var joint in joints)
                    if (joint.finger == selectedFinger) joint.manualOffset = Quaternion.identity;
                SetFingerAdjustment(selectedFinger, default);
            }
        }

        editFingerJoints = GUILayout.Toggle(editFingerJoints, "Edit Finger Joints In Scene", "Button");
        if (editFingerJoints && joints.Count > 0)
        {
            var selection = EditorGUILayout.Popup("Selected Joint", selectedJoint, jointNames);
            if (selection != selectedJoint)
            {
                selectedJoint = selection;
                SceneView.RepaintAll();
            }
        }
    }

    public void SetPreviewPose(Vector3 position, Quaternion rotation)
    {
        if (preview == null) return;
        previewPosition = position;
        previewRotation = rotation;
        UpdatePreviewTransform();
        previewDirty = true;
        SceneView.RepaintAll();
    }

    void UpdatePreviewTransform()
    {
        var component = (ControllerGrabPose)target;
        preview.transform.SetPositionAndRotation(component.transform.TransformPoint(previewPosition),
            component.transform.rotation * previewRotation);
    }

    public void SetFingerCurl(int finger, float amount)
    {
        if (preview == null || finger < 0 || finger >= fingerCurls.Length) return;
        fingerCurls[finger] = Mathf.Clamp01(amount);
        ApplyFingerMovement(finger);
    }

    public void SetFingerAdjustment(int finger, ControllerHandPose.FingerAdjustment adjustment)
    {
        if (preview == null || finger < 0 || finger >= fingerAdjustments.Length) return;
        fingerAdjustments[finger] = adjustment;
        ApplyFingerMovement(finger);
    }

    public void SetJointRotation(string path, Quaternion rotation)
    {
        var joint = joints.Find(candidate => candidate.path == path);
        if (joint == null) return;
        joint.manualOffset = Quaternion.Inverse(joint.rest * MovementRotation(joint)) * rotation;
        joint.transform.localRotation = rotation;
        previewDirty = true;
        SceneView.RepaintAll();
        Repaint();
    }

    void ResetFingerMovement(float curl)
    {
        foreach (var joint in joints) joint.manualOffset = Quaternion.identity;
        for (var finger = 0; finger < FingerNames.Length; finger++)
        {
            fingerAdjustments[finger] = default;
            SetFingerCurl(finger, curl);
        }
    }

    public void SavePreview()
    {
        if (preview == null) return;
        var component = (ControllerGrabPose)target;
        Undo.RecordObject(component, "Save Hand Grab Pose");
        var pose = component.GetPose(leftHand);
        pose.position = previewPosition;
        pose.rotation = previewRotation;
        pose.fingerCurls = (float[])fingerCurls.Clone();
        pose.fingerAdjustments = (ControllerHandPose.FingerAdjustment[])fingerAdjustments.Clone();
        pose.joints.Clear();
        foreach (var joint in joints)
            pose.joints.Add(new ControllerHandPose.Joint { path = joint.path, rotation = joint.transform.localRotation });
        pose.saved = true;
        MarkChanged(component);
        previewDirty = false;
        Repaint();
    }

    static void MarkChanged(ControllerGrabPose component)
    {
        EditorUtility.SetDirty(component);
        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
    }

    void OnSceneGUI()
    {
        if (preview == null) return;
        UpdatePreviewTransform();
        if (editFingerJoints)
        {
            DrawJointHandles();
            return;
        }
        Handles.Label(preview.transform.position, (leftHand ? "Left" : "Right") + " hand pose" + (previewDirty ? " (unsaved)" : ""));
        EditorGUI.BeginChangeCheck();
        var position = preview.transform.position;
        var rotation = preview.transform.rotation;
        if (Tools.current == Tool.Rotate) rotation = Handles.RotationHandle(rotation, position);
        else position = Handles.PositionHandle(position, rotation);
        if (!EditorGUI.EndChangeCheck()) return;
        var component = (ControllerGrabPose)target;
        SetPreviewPose(component.transform.InverseTransformPoint(position), Quaternion.Inverse(component.transform.rotation) * rotation);
        Repaint();
    }

    void DrawJointHandles()
    {
        for (var jointIndex = 0; jointIndex < joints.Count; jointIndex++)
        {
            var joint = joints[jointIndex];
            Handles.color = jointIndex == selectedJoint ? Color.yellow : new Color(0.2f, 0.8f, 1f);
            if (joint.transform.parent != null) Handles.DrawLine(joint.transform.parent.position, joint.transform.position);
            if (!Handles.Button(joint.transform.position, joint.transform.rotation, 0.0035f, 0.0045f, Handles.SphereHandleCap)) continue;
            selectedJoint = jointIndex;
            selectedFinger = joint.finger;
            Repaint();
        }
        Handles.color = Color.white;
        if (selectedJoint < 0 || selectedJoint >= joints.Count) return;
        var selected = joints[selectedJoint];
        Handles.Label(selected.transform.position, selected.transform.name);
        EditorGUI.BeginChangeCheck();
        var rotation = Handles.RotationHandle(selected.transform.rotation, selected.transform.position);
        if (EditorGUI.EndChangeCheck())
            SetJointRotation(selected.path, Quaternion.Inverse(selected.transform.parent.rotation) * rotation);
    }

    void OnUndoRedo()
    {
        if (preview != null) BeginPreview(leftHand);
        Repaint();
    }

    void OnPlayModeChanged(PlayModeStateChange state) => StopPreview();

    public void StopPreview()
    {
        if (preview != null) DestroyImmediate(preview);
        preview = null;
        previewRenderer = null;
        joints.Clear();
        jointNames = null;
        previewDirty = false;
        SceneView.RepaintAll();
    }
}
