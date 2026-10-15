using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Moves a collection of objects from point A to point B at a constant world speed.
/// Call Fire from a Timeline Signal Receiver or UnityEvent.
/// </summary>
public sealed class AToBSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform[] objects = new Transform[0];
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [Header("Motion")]
    [Min(0.01f)] [SerializeField] private float speed = 25f;
    [SerializeField] private bool faceTravelDirection;

    private readonly List<Tween> activeTweens = new List<Tween>();
    private readonly List<Vector3> startPositions = new List<Vector3>();
    private readonly List<Quaternion> startRotations = new List<Quaternion>();

    private void Awake() => CacheStartState();

    /// <summary>Starts every assigned object at A and moves it to B at the configured speed.</summary>
    [ContextMenu("Test A To B")]
    public void Fire()
    {
        StopActiveTweens();
        RestoreStartState();

        if (pointA == null || pointB == null || objects == null || objects.Length == 0)
        {
            Debug.LogWarning("AToBSystem needs objects, Point A and Point B assigned.", this);
            return;
        }

        float duration = Vector3.Distance(pointA.position, pointB.position) / Mathf.Max(0.01f, speed);
        Vector3 travelDirection = pointB.position - pointA.position;
        Quaternion travelRotation = travelDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(travelDirection.normalized, Vector3.up)
            : Quaternion.identity;

        for (int i = 0; i < objects.Length; i++)
        {
            Transform target = objects[i];
            if (target == null) continue;
            target.position = pointA.position;
            if (faceTravelDirection) target.rotation = travelRotation;
            activeTweens.Add(Tween.Position(target, pointB.position, duration, Ease.Linear));
        }
    }

    /// <summary>Stops active movement and returns every object to point A.</summary>
    [ContextMenu("Reset A To B")]
    public void ResetSystem()
    {
        StopActiveTweens();
        RestoreStartState();
    }

    private void CacheStartState()
    {
        startPositions.Clear();
        startRotations.Clear();
        if (objects == null) return;
        foreach (Transform target in objects)
        {
            startPositions.Add(target != null ? target.position : Vector3.zero);
            startRotations.Add(target != null ? target.rotation : Quaternion.identity);
        }
    }

    private void RestoreStartState()
    {
        if (objects == null || pointA == null) return;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null) continue;
            objects[i].SetPositionAndRotation(pointA.position,
                faceTravelDirection && pointB != null
                    ? Quaternion.LookRotation((pointB.position - pointA.position).normalized, Vector3.up)
                    : startRotations[i]);
        }
    }

    private void StopActiveTweens()
    {
        for (int i = 0; i < activeTweens.Count; i++)
            if (activeTweens[i].isAlive) activeTweens[i].Stop();
        activeTweens.Clear();
    }

    private void OnDisable() => StopActiveTweens();

    private void OnDrawGizmosSelected()
    {
        if (pointA == null || pointB == null) return;
        Gizmos.color = new Color(1f, 0.55f, 0.1f);
        Gizmos.DrawWireSphere(pointA.position, 0.15f);
        Gizmos.color = new Color(0.2f, 0.8f, 1f);
        Gizmos.DrawWireSphere(pointB.position, 0.15f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(pointA.position, pointB.position);
    }
}
