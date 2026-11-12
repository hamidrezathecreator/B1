using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// B1 → Lvl1 → Setup Bridge Blast Timeline: creates the two bridge blasts and the player + Plane
/// fall in the open Lvl_1 scene, their signals, and a signal track for each on the Director's
/// timeline with a starting time. Drag the signals on the timeline to change the timing.
/// Safe to run again: existing objects, signals and tracks are reused.
/// </summary>
static class Lvl1BridgeSequenceSetup
{
    const string SignalFolder = "Assets/Director/Scene_1/Signals";
    const string ExplosionPrefabPath = "Assets/DAVFX/Realistic 6D Lighting Explosions/URP/Prefabs/Explosion 13.prefab";
    const string SoundPath = "Assets/Audio/Red_Complete.ogg";

    const string BlastObjectsTrack = "Blast Objects";
    const string BlastOneTrack = "Blast Bridge One";
    const string FallTrack = "Player And Plane Fall";

    [MenuItem("B1/Lvl1/Setup Bridge Blast Timeline")]
    static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "Lvl_1")
        {
            EditorUtility.DisplayDialog("Bridge Blast", "Open the Lvl_1 scene first.", "OK");
            return;
        }

        var directorGo = GameObject.Find("Director");
        var director = directorGo != null ? directorGo.GetComponent<PlayableDirector>() : null;
        var timeline = director != null ? director.playableAsset as TimelineAsset : null;
        if (timeline == null)
        {
            EditorUtility.DisplayDialog("Bridge Blast", "No 'Director' with a Timeline in the scene.", "OK");
            return;
        }

        var scatterObjects = FindScatter("BridgeFallScatterObjects");
        var scatterOne = FindScatter("BridgeFallScatterOne");
        if (scatterObjects == null || scatterOne == null)
        {
            EditorUtility.DisplayDialog("Bridge Blast",
                "BridgeFallScatterObjects or BridgeFallScatterOne (with Explosion Scatter) is missing.", "OK");
            return;
        }

        var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);
        var sound = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);

        var root = GameObject.Find("Bridge Sequence");
        if (root == null)
        {
            root = new GameObject("Bridge Sequence");
            Undo.RegisterCreatedObjectUndo(root, "Bridge Sequence");
        }

        var blastObjects = Child<BridgeBlast>(root.transform, "Blast Objects");
        blastObjects.Configure(scatterObjects, explosion, sound);
        var blastOne = Child<BridgeBlast>(root.transform, "Blast Bridge One");
        blastOne.Configure(scatterOne, explosion, sound);
        var fall = Child<PlayerPlaneFall>(root.transform, "Player And Plane Fall");
        EditorUtility.SetDirty(blastObjects);
        EditorUtility.SetDirty(blastOne);
        EditorUtility.SetDirty(fall);

        var signalObjects = Signal("BlastObjects");
        var signalOne = Signal("BlastBridgeOne");
        var signalFall = Signal("PlayerPlaneFall");

        Wire(blastObjects.gameObject, signalObjects, blastObjects.Play);
        Wire(blastOne.gameObject, signalOne, blastOne.Play);
        Wire(fall.gameObject, signalFall, fall.Fall);

        // Default timing from the case snap: objects at 1 s, bridge one 3 s later, then the fall.
        Track(timeline, director, BlastObjectsTrack, blastObjects.gameObject, signalObjects, 1f);
        Track(timeline, director, BlastOneTrack, blastOne.gameObject, signalOne, 4f);
        Track(timeline, director, FallTrack, fall.gameObject, signalFall, 4.5f);

        var muted = MuteOldTracks(timeline, director, scatterObjects, scatterOne);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        EditorUtility.DisplayDialog("Bridge Blast",
            "Ready. On the Timeline:\n" +
            "• " + BlastObjectsTrack + " at 1 s\n• " + BlastOneTrack + " at 4 s\n• " + FallTrack + " at 4.5 s\n\n" +
            (muted.Count > 0 ? "Muted (old timing, would clash): " + string.Join(", ", muted) + "\n\n" : "") +
            "Drag the signals to change the timing, then save the scene.", "OK");
    }

    const string SoundTrack = "Explosion Sounds";

    /// <summary>
    /// Puts the explosion sound on the Timeline as an Audio Track: one Red_Complete clip where each
    /// Explosion_* activation starts. Drag the clips to move the sound; trim the start with Clip In.
    /// </summary>
    [MenuItem("B1/Lvl1/Add Explosion Sounds To Timeline")]
    static void AddExplosionSounds()
    {
        var scene = SceneManager.GetActiveScene();
        var directorGo = GameObject.Find("Director");
        var director = directorGo != null ? directorGo.GetComponent<PlayableDirector>() : null;
        var timeline = director != null ? director.playableAsset as TimelineAsset : null;
        var sound = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
        if (scene.name != "Lvl_1" || timeline == null || sound == null)
        {
            EditorUtility.DisplayDialog("Explosion Sounds",
                "Open Lvl_1 (with its Director timeline). Assets/Audio/Red_Complete.ogg must exist.", "OK");
            return;
        }

        var track = timeline.GetOutputTracks().OfType<AudioTrack>().FirstOrDefault(t => t.name == SoundTrack);
        var added = new List<string>();
        if (track == null)
        {
            track = timeline.CreateTrack<AudioTrack>(null, SoundTrack);
            foreach (var activation in timeline.GetOutputTracks().OfType<ActivationTrack>())
            {
                if (activation.muted) continue;
                var bound = director.GetGenericBinding(activation) as GameObject;
                if (bound == null || !bound.name.StartsWith("Explosion")) continue;
                var first = activation.GetClips().OrderBy(c => c.start).FirstOrDefault();
                if (first == null) continue;
                var clip = track.CreateClip(sound);
                clip.start = first.start;
                clip.displayName = "Sound " + bound.name;
                added.Add(bound.name + " at " + first.start.ToString("0.00") + " s");
            }
        }

        // A plain 2D source, so the blast is heard at full volume wherever the player stands.
        var sourceGo = GameObject.Find("Timeline Explosion Audio");
        if (sourceGo == null)
        {
            sourceGo = new GameObject("Timeline Explosion Audio");
            Undo.RegisterCreatedObjectUndo(sourceGo, "Timeline Explosion Audio");
        }
        var source = sourceGo.GetComponent<AudioSource>();
        if (source == null) source = Undo.AddComponent<AudioSource>(sourceGo);
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        director.SetGenericBinding(track, source);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);

        EditorUtility.DisplayDialog("Explosion Sounds",
            (added.Count > 0
                ? "Audio Track \"" + SoundTrack + "\" added:\n• " + string.Join("\n• ", added)
                : "Audio Track \"" + SoundTrack + "\" already exists; nothing added.") +
            "\n\nDrag a clip to play the sound earlier or later. If the file starts with silence, drag the " +
            "clip's left edge in (Clip In) to cut it. Then save the scene.", "OK");
    }

    static ExplosionScatter FindScatter(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go.GetComponent<ExplosionScatter>() : null;
    }

    static T Child<T>(Transform parent, string name) where T : Component
    {
        var child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            child = go.transform;
        }
        var component = child.GetComponent<T>();
        if (component == null) component = Undo.AddComponent<T>(child.gameObject);
        return component;
    }

    static SignalAsset Signal(string name)
    {
        var path = SignalFolder + "/" + name + ".signal";
        var signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
        if (signal != null) return signal;
        if (!AssetDatabase.IsValidFolder(SignalFolder))
            AssetDatabase.CreateFolder("Assets/Director/Scene_1", "Signals");
        signal = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(signal, path);
        return signal;
    }

    static void Wire(GameObject go, SignalAsset signal, UnityAction call)
    {
        var receiver = go.GetComponent<SignalReceiver>();
        if (receiver == null) receiver = Undo.AddComponent<SignalReceiver>(go);
        if (receiver.GetReaction(signal) != null) return;
        var reaction = new UnityEvent();
        UnityEventTools.AddVoidPersistentListener(reaction, call);
        receiver.AddReaction(signal, reaction);
        EditorUtility.SetDirty(receiver);
    }

    static void Track(TimelineAsset timeline, PlayableDirector director, string name, GameObject receiverGo,
        SignalAsset signal, double time)
    {
        var track = timeline.GetOutputTracks().OfType<SignalTrack>().FirstOrDefault(t => t.name == name);
        if (track == null)
        {
            track = timeline.CreateTrack<SignalTrack>(null, name);
            var emitter = track.CreateMarker<SignalEmitter>(time);
            emitter.asset = signal;
            emitter.retroactive = false;
            emitter.emitOnce = false;
        }
        director.SetGenericBinding(track, receiverGo.GetComponent<SignalReceiver>());
    }

    /// <summary>The old signals threw these two scatters a quarter at a time and hid the Plane at 2.2 s.</summary>
    static List<string> MuteOldTracks(TimelineAsset timeline, PlayableDirector director,
        ExplosionScatter scatterObjects, ExplosionScatter scatterOne)
    {
        var muted = new List<string>();
        var plane = GameObject.Find("Plane");
        foreach (var track in timeline.GetOutputTracks())
        {
            if (track.muted || track.name == BlastObjectsTrack || track.name == BlastOneTrack || track.name == FallTrack)
                continue;
            var binding = director.GetGenericBinding(track) as Object;
            var bound = binding is Component c ? c.gameObject : binding as GameObject;
            if (bound == null) continue;
            var clash = bound == scatterObjects.gameObject || bound == scatterOne.gameObject ||
                        (plane != null && bound == plane && track is ActivationTrack);
            if (!clash) continue;
            track.muted = true;
            muted.Add(track.name);
        }
        return muted;
    }
}
