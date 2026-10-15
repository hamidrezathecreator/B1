using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public sealed class SpriteCrossfadeClip : PlayableAsset
{
    public ExposedReference<SpriteCrossfadeController> controller;
    public bool triggerOnReverse;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<SpriteCrossfadeBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();
        behaviour.controller = controller.Resolve(graph.GetResolver());
        behaviour.triggerOnReverse = triggerOnReverse;
        return playable;
    }
}

public sealed class SpriteCrossfadeBehaviour : PlayableBehaviour
{
    [System.NonSerialized] public SpriteCrossfadeController controller;
    [System.NonSerialized] public bool triggerOnReverse;
    private bool fired;

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        if (fired || controller == null) return;
        if (info.evaluationType == FrameData.EvaluationType.Evaluate && !triggerOnReverse) return;
        controller.BroadcastCrossfade();
        fired = true;
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (playable.GetTime() <= 0.0001) fired = false;
    }
}

[TrackClipType(typeof(SpriteCrossfadeClip))]
[TrackBindingType(typeof(SpriteCrossfadeController))]
public sealed class SpriteCrossfadeTrack : TrackAsset { }
