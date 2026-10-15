using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ExplosionQuads
{
    public enum ExplosionFacing
    {
        PrefabRotation,
        FaceCamera,
        FaceCameraYawOnly,
        FaceCameraPitchOnly
    }

    [Serializable]
    public class ExplosionBurst
    {
        [Min(1)] public int count = 1;
        [Tooltip("Position relative to the emitter. Additional instances are scattered around this point.")]
        public Vector3 localPosition;
        [Min(0f)] public float delay;
        [Min(0f)] public float yOffset;
        [Min(0f)] public float radius;
        public Vector3 randomRotation = new Vector3(0f, 180f, 0f);
        public Vector3 scale = Vector3.one;
        public bool randomizeScale;
        public Vector3 scaleVariation = new Vector3(.15f, .15f, .15f);
    }

    /// <summary>
    /// Event-driven flipbook explosion emitter. Put this on a scene object, assign one quad
    /// prefab, then call Play from UnityEvents, code, or by activating the object from Timeline.
    /// </summary>
    public sealed class ExplosionQuadEmitter : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private GameObject quadPrefab;
        [Tooltip("Optional material variants. A variant replaces the prefab renderer material per shot.")]
        [SerializeField] private Material[] explosionVariants = Array.Empty<Material>();
        [SerializeField] private bool randomizeVariants = true;

        [Header("Collection")]
        [SerializeField] private List<ExplosionBurst> explosions = new List<ExplosionBurst> { new ExplosionBurst() };
        [SerializeField] private bool useEmitterTransform = true;
        [SerializeField] private bool useUnscaledTime;
        [SerializeField] private int randomSeed;

        [Header("Presentation")]
        [SerializeField] private ExplosionFacing facing = ExplosionFacing.FaceCamera;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool faceCameraContinuously = true;
        [SerializeField] private bool flipFacing;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool clearPreviousOnPlay = true;
        [SerializeField] private Transform outputParent;

        [Header("Events")]
        public UnityEvent onExplosionStarted = new UnityEvent();
        public UnityEvent onExplosionCompleted = new UnityEvent();
        public UnityEvent<int> onShotSpawned = new UnityEvent<int>();

        private readonly List<GameObject> spawned = new List<GameObject>();
        private Coroutine playback;
        private System.Random random;
        private int shotCount;

        public GameObject QuadPrefab { get => quadPrefab; set => quadPrefab = value; }
        public IReadOnlyList<ExplosionBurst> Explosions => explosions;
        public bool IsPlaying => playback != null;

        private void Awake()
        {
            random = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            if (playback != null) StopCoroutine(playback);
            playback = null;
            ClearSpawned();
        }

        [ContextMenu("Play Explosions")]
        public void Play()
        {
            if (!isActiveAndEnabled) return;
            if (playback != null) StopCoroutine(playback);
            if (clearPreviousOnPlay) ClearSpawned();
            playback = StartCoroutine(PlayRoutine());
        }

        public void Trigger() => Play();

        public void Stop()
        {
            if (playback != null) StopCoroutine(playback);
            playback = null;
            ClearSpawned();
        }

        public void ClearSpawned()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Destroy(spawned[i]);
            spawned.Clear();
        }

        private IEnumerator PlayRoutine()
        {
            if (quadPrefab == null)
            {
                Debug.LogWarning("ExplosionQuadEmitter needs a quad prefab.", this);
                playback = null;
                yield break;
            }

            onExplosionStarted.Invoke();
            shotCount = 0;
            var ordered = new List<ExplosionBurst>(explosions ?? new List<ExplosionBurst>());
            ordered.Sort((a, b) => a.delay.CompareTo(b.delay));
            float elapsed = 0f;
            foreach (ExplosionBurst burst in ordered)
            {
                if (burst == null || burst.count < 1) continue;
                float wait = Mathf.Max(0f, burst.delay - elapsed);
                if (wait > 0f) yield return Wait(wait);
                elapsed = burst.delay;
                for (int i = 0; i < burst.count; i++)
                {
                    Spawn(burst);
                    yield return null;
                }
            }

            float longest = 0f;
            foreach (GameObject instance in spawned)
            {
                if (instance == null) continue;
                QuadSequncerController sequence = instance.GetComponent<QuadSequncerController>();
                if (sequence != null) longest = Mathf.Max(longest, sequence.GetLifetime());
            }
            if (longest > 0f) yield return Wait(longest);
            playback = null;
            ClearSpawned();
            onExplosionCompleted.Invoke();
        }

        private IEnumerator Wait(float seconds)
        {
            if (useUnscaledTime) yield return new WaitForSecondsRealtime(seconds);
            else yield return new WaitForSeconds(seconds);
        }

        private void Spawn(ExplosionBurst burst)
        {
            Transform parent = outputParent != null ? outputParent : transform;
            Vector3 center = useEmitterTransform ? transform.TransformPoint(burst.localPosition) : burst.localPosition;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * burst.radius;
            Vector3 position = center + new Vector3(offset.x, burst.yOffset, offset.y);
            GameObject instance = Instantiate(quadPrefab, position, transform.rotation, parent);
            spawned.Add(instance);

            Vector3 scale = burst.scale;
            if (burst.randomizeScale)
                scale += new Vector3(RandomRange(-burst.scaleVariation.x, burst.scaleVariation.x), RandomRange(-burst.scaleVariation.y, burst.scaleVariation.y), RandomRange(-burst.scaleVariation.z, burst.scaleVariation.z));
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, scale);
            instance.transform.rotation = Quaternion.Euler(burst.randomRotation.x * RandomRange(-1f, 1f), burst.randomRotation.y * RandomRange(-1f, 1f), burst.randomRotation.z * RandomRange(-1f, 1f)) * instance.transform.rotation;

            Renderer renderer = instance.GetComponentInChildren<Renderer>();
            Material material = PickVariant(renderer);
            QuadSequncerController sequence = instance.GetComponent<QuadSequncerController>();
            if (sequence == null) sequence = instance.GetComponentInChildren<QuadSequncerController>();
            if (sequence != null && renderer != null) sequence.Initialize(material != null ? material : renderer.sharedMaterial);
            sequence?.Play();

            ExplosionQuadBillboard billboard = instance.GetComponent<ExplosionQuadBillboard>();
            if (billboard == null && facing != ExplosionFacing.PrefabRotation)
                billboard = instance.AddComponent<ExplosionQuadBillboard>();
            if (billboard != null) billboard.Configure(GetCamera(), facing, flipFacing, faceCameraContinuously);
            onShotSpawned.Invoke(++shotCount);
        }

        private Material PickVariant(Renderer renderer)
        {
            if (explosionVariants == null || explosionVariants.Length == 0) return renderer != null ? renderer.sharedMaterial : null;
            int index = randomizeVariants ? random.Next(explosionVariants.Length) : (shotCount % explosionVariants.Length);
            return explosionVariants[index];
        }

        private Camera GetCamera() => targetCamera != null ? targetCamera : Camera.main;
        private float RandomRange(float min, float max) => (float)(min + (max - min) * random.NextDouble());
    }

    public sealed class ExplosionQuadBillboard : MonoBehaviour
    {
        private Camera target;
        private ExplosionFacing mode;
        private bool flip;
        private bool continuous;
        public void Configure(Camera camera, ExplosionFacing facing, bool flipFacing, bool updateContinuously)
        { target = camera; mode = facing; flip = flipFacing; continuous = updateContinuously; Apply(); }
        private void LateUpdate() { if (continuous) Apply(); }
        private void Apply()
        {
            if (target == null || mode == ExplosionFacing.PrefabRotation) return;
            Vector3 toCamera = target.transform.position - transform.position;
            if (mode == ExplosionFacing.FaceCameraYawOnly) toCamera.y = 0f;
            if (mode == ExplosionFacing.FaceCameraPitchOnly) toCamera = new Vector3(0f, toCamera.y, toCamera.z);
            if (toCamera.sqrMagnitude < .0001f) return;
            Quaternion rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            if (flip) rotation *= Quaternion.Euler(0f, 180f, 0f);
            transform.rotation = rotation;
        }
    }
}




