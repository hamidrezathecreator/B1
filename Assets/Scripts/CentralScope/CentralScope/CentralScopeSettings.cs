using System;
using UnityEngine;

[CreateAssetMenu(menuName = "B1/CentralScope Settings")]
public sealed class CentralScopeSettings : ScriptableObject
{
    [Serializable]
    public sealed class Profile
    {
        [Range(0f, 1f)] public float radialBlurAmount = 0.12f;
        [Range(0f, 1f)] public float radialBlurRadius = 0.06f;
        [Range(0f, 1f)] public float vignetteAmount = 0.04f;
        [Range(0f, 0.8f)] public float featherInnerRadius = 0.3f;
        [Range(0f, 0.8f)] public float featherOuterRadius = 0.65f;
        [Range(0.5f, 4f)] public float featherCurve = 2f;
    }

    [Header("Shake: subtle")]
    public Profile shake = new Profile();

    [Header("Fall: extreme")]
    public Profile fall = new Profile
    {
        radialBlurAmount = 1f,
        radialBlurRadius = 0.85f,
        vignetteAmount = 0.9f,
        featherInnerRadius = 0.02f,
        featherOuterRadius = 0.35f,
        featherCurve = 1f
    };
}
