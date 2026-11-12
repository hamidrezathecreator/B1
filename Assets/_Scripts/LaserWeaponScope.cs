using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Grabbable laser weapon with a zoom scope: a camera just past the front of the weapon looks
/// where it aims, and its picture is shown on the weapon's white screen through a render texture.
/// Added at runtime to the "LaserWeapon" object by the level setup, or add it by hand to tune it.
/// </summary>
[AddComponentMenu("B1/Laser Weapon Scope")]
public sealed class LaserWeaponScope : MonoBehaviour
{
    public enum Axis { Auto, PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }

    [Header("Scope camera")]
    [Tooltip("Scene camera that renders the scope picture and aims the lock ray. Empty = found by name below, " +
             "or a camera is made in front of the weapon.")]
    [SerializeField] Camera laserCamera;
    [SerializeField] string[] laserCameraNames = { "Camera Lase", "Camera Laser", "CameraLaser", "Laser Camera" };
    [Tooltip("Smaller = more zoom. Only for the camera made by this script; a scene camera keeps its own.")]
    [Range(1f, 60f)] [SerializeField] float fieldOfView = 12f;
    [Tooltip("How far past the front end of the weapon the camera sits, meters.")]
    [Min(0f)] [SerializeField] float cameraAhead = 0.3f;
    [Tooltip("Direction the weapon fires in its own space. Auto = its long axis, away from the screen.")]
    [SerializeField] Axis aimAxis = Axis.Auto;
    [Tooltip("Extra yaw on the scope aim, degrees. Positive = turn right. SM_LaserWeapon needs +90.")]
    [SerializeField] float aimYawOffset = 90f;
    [Min(1f)] [SerializeField] float farClip = 3000f;
    [Tooltip("Render texture height in pixels; the width follows the screen's shape.")]
    [Range(128, 1024)] [SerializeField] int textureHeight = 512;

    [Header("Screen")]
    [Tooltip("Material slot of the white screen on the weapon mesh (M_Glass).")]
    [Min(0)] [SerializeField] int screenMaterialIndex = 1;

    [Header("Grab")]
    [SerializeField] bool grabbable = true;

    [Header("Target lock")]
    [Tooltip("What the scope locks onto. Empty = SM_Shahed149, else the Fighter Jet Flight in the scene.")]
    [SerializeField] Transform target;
    [SerializeField] string targetName = "SM_Shahed149";
    [Tooltip("Seconds the target must stay in the crosshair before the missile goes.")]
    [Min(0.1f)] [SerializeField] float lockSeconds = 2f;
    [Tooltip("How far off the camera's line the target may be and still count, degrees. 0 = the ray must hit it.")]
    [Range(0f, 15f)] [SerializeField] float aimToleranceDegrees = 2.5f;
    [Tooltip("Only lock while the weapon is in the player's hand.")]
    [SerializeField] bool requireHeld = true;
    [SerializeField] Color crosshairColor = Color.white;
    [SerializeField] Color lockedColor = new Color(0.2f, 1f, 0.3f, 1f);
    [Range(0.1f, 1f)] [SerializeField] float crosshairScale = 0.6f;

    [Header("Missile on lock")]
    [Tooltip("Missile model, nose along +Z. Empty = a simple built-in missile.")]
    [SerializeField] GameObject missilePrefab;
    [Min(0.01f)] [SerializeField] float missileScale = 0.33333334f;
    [SerializeField] string missileLaunchOriginName = "SM_AirDefense (1)";
    [SerializeField] Vector3 missileLaunchOffset = new Vector3(0f, 0f, 0.5f);
    [Min(0.1f)] [SerializeField] float missileSeconds = 1.5f;
    [SerializeField] AudioClip missileLaunchSound;
    [Range(0f, 1f)] [SerializeField] float missileLaunchVolume = 1f;

    [Header("Kill feedback")]
    [Tooltip("Shown above the right controller after SM_Shahed149 is destroyed.")]
    [SerializeField] string killMessage = "پهباد جاسوسی رو زدی\nآفرین";
    [SerializeField] TMPro.TMP_FontAsset killMessageFont;
    [Min(0.5f)] [SerializeField] float killMessageSeconds = 4f;
    [Min(0.5f)] [SerializeField] float killExplosionScale = 1.4f;

    Camera scopeCamera;
    bool createdCamera = true;
    readonly System.Collections.Generic.List<UnityEngine.UI.Image> crosshair = new System.Collections.Generic.List<UnityEngine.UI.Image>();
    Renderer[] targetRenderers;
    Transform missileLaunchOrigin;
    float lockTimer;
    bool missileAway;
    bool targetDown;
    RenderTexture texture;
    Renderer screenRenderer;
    XRGrabInteractable grab;
    Rigidbody body;
    Vector3 homePosition;
    Quaternion homeRotation;

    /// <summary>Sets up the first object found with one of the names (once).</summary>
    public static void SetupByName(AudioClip launchSound, TMPro.TMP_FontAsset font, params string[] names)
    {
        foreach (var n in names)
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            var scope = go.GetComponent<LaserWeaponScope>();
            if (scope == null) scope = go.AddComponent<LaserWeaponScope>();
            if (launchSound != null) scope.missileLaunchSound = launchSound;
            if (font != null) scope.killMessageFont = font;
            return;
        }
    }

    /// <summary>Sets up the first object found with one of the names (once).</summary>
    public static void SetupByName(AudioClip launchSound, params string[] names) =>
        SetupByName(launchSound, null, names);

    void Start()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;

        var filter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || meshRenderer == null)
        {
            Debug.LogWarning(name + ": Laser Weapon Scope needs the weapon's MeshFilter and MeshRenderer on this object.", this);
            return;
        }

        var mesh = filter.sharedMesh;
        var whole = mesh.bounds;
        var hasScreen = screenMaterialIndex < mesh.subMeshCount;
        var screenBounds = hasScreen ? mesh.GetSubMesh(screenMaterialIndex).bounds : whole;
        var bodyBounds = mesh.subMeshCount > 1 && screenMaterialIndex != 0 ? mesh.GetSubMesh(0).bounds : whole;
        if (bodyBounds.size.sqrMagnitude < 1e-8f) bodyBounds = whole;

        var aimLocal = AimDirection(bodyBounds, screenBounds, hasScreen);
        var screenNormal = hasScreen ? ScreenNormal(screenBounds, whole) : -aimLocal;
        var screenUp = ScreenUp(screenNormal, aimLocal);

        texture = CreateTexture(screenBounds, screenNormal, screenUp, hasScreen);
        if (!UseSceneCamera(bodyBounds, aimLocal, screenUp)) CreateCamera(bodyBounds, aimLocal, screenUp);
        if (hasScreen)
        {
            CreateScreen(meshRenderer, screenBounds, screenNormal, screenUp);
            CreateCrosshair(screenBounds, screenNormal, screenUp);
        }
        if (grabbable) MakeGrabbable(whole);
        FindTarget();
        FindMissileLaunchOrigin();
    }

    /// <summary>Renders the scope with the scene's laser camera, if there is one.</summary>
    bool UseSceneCamera(Bounds bodyBounds, Vector3 aimLocal, Vector3 upLocal)
    {
        if (laserCamera == null) laserCamera = FindLaserCamera();
        if (laserCamera == null) return false;

        createdCamera = false;
        scopeCamera = laserCamera;
        // Child of the weapon so it turns with the grip; identity world pose was often +Z, not the barrel.
        scopeCamera.transform.SetParent(transform, false);
        AlignScopeCamera(scopeCamera.transform, bodyBounds, aimLocal, upLocal);

        // Level setup switches off every MainCamera except the headset; this one only draws the scope.
        if (scopeCamera.CompareTag("MainCamera")) scopeCamera.tag = "Untagged";
        var listener = scopeCamera.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;

        scopeCamera.gameObject.SetActive(true);
        scopeCamera.enabled = true;
        scopeCamera.stereoTargetEye = StereoTargetEyeMask.None;
        scopeCamera.targetTexture = texture;
        scopeCamera.depth = -10f;
        var data = scopeCamera.GetComponent<UniversalAdditionalCameraData>();
        if (data != null) data.allowXRRendering = false;
        farClip = Mathf.Max(farClip, scopeCamera.farClipPlane);
        scopeCamera.farClipPlane = farClip;

        // Extra laser cameras under the weapon must not also render into the headset.
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            if (cam == scopeCamera) continue;
            if (!MatchesLaserName(cam.name)) continue;
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            var otherListener = cam.GetComponent<AudioListener>();
            if (otherListener != null) otherListener.enabled = false;
            var otherData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (otherData != null) otherData.allowXRRendering = false;
        }

        Debug.Log(name + ": scope uses the scene camera '" + scopeCamera.name + "' along aim " + aimLocal + ".", this);
        return true;
    }

    void AlignScopeCamera(Transform cam, Bounds bodyBounds, Vector3 aimLocal, Vector3 upLocal)
    {
        var aimWorld = transform.TransformDirection(aimLocal).normalized;
        var upWorld = transform.TransformDirection(upLocal).normalized;
        if (aimWorld.sqrMagnitude < 1e-8f) aimWorld = transform.forward;
        if (Mathf.Abs(Vector3.Dot(upWorld, aimWorld)) > 0.98f)
            upWorld = Vector3.Cross(aimWorld, transform.right).normalized;
        if (upWorld.sqrMagnitude < 1e-8f) upWorld = Vector3.up;

        // Turn the aim around "up" so the RT matches the barrel (mesh Auto was 90° left).
        if (Mathf.Abs(aimYawOffset) > 0.01f)
            aimWorld = Quaternion.AngleAxis(aimYawOffset, upWorld) * aimWorld;

        var muzzle = transform.TransformPoint(bodyBounds.center + Vector3.Scale(aimLocal, bodyBounds.extents));
        cam.SetPositionAndRotation(muzzle + aimWorld * cameraAhead, Quaternion.LookRotation(aimWorld, upWorld));
    }

    Camera FindLaserCamera()
    {
        Camera fallback = null;
        // Prefer a camera under the weapon named exactly "Camera Lase" over "Camera Lase (1)".
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            if (!MatchesLaserName(cam.name)) continue;
            if (Normalize(cam.name) == "cameralase") return cam;
            if (fallback == null) fallback = cam;
        }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!cam.gameObject.scene.IsValid() || !MatchesLaserName(cam.name)) continue;
            if (Normalize(cam.name) == "cameralase") return cam;
            if (fallback == null) fallback = cam;
        }
        return fallback;
    }

    bool MatchesLaserName(string camName)
    {
        var n = Normalize(camName);
        foreach (var wanted in laserCameraNames)
            if (!string.IsNullOrWhiteSpace(wanted) && n == Normalize(wanted)) return true;
        return false;
    }

    static string Normalize(string s) => s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

    void FindTarget()
    {
        if (target == null && !string.IsNullOrWhiteSpace(targetName))
        {
            var named = GameObject.Find(targetName);
            if (named != null) target = named.transform;
        }
        if (target == null)
        {
            var jet = FindAnyObjectByType<FighterJetFlight>();
            if (jet != null) target = jet.transform;
        }
        if (target == null) return;
        targetRenderers = target.GetComponentsInChildren<Renderer>();
        var targetJet = target.GetComponent<FighterJetFlight>();
        if (missilePrefab == null && targetJet != null)
            missilePrefab = targetJet.MissilePrefab;

        // Something for the scope's raycast to hit; a trigger, so it never pushes anything.
        if (target.GetComponentInChildren<Collider>() == null && TargetBounds(out var bounds))
        {
            var box = target.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = target.InverseTransformPoint(bounds.center);
            var scale = target.lossyScale;
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }
    }

    bool TargetBounds(out Bounds bounds)
    {
        bounds = default;
        if (targetRenderers == null) return false;
        var has = false;
        foreach (var r in targetRenderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    /// <summary>White cross in the middle of the scope picture, drawn with UI on top of the screen.</summary>
    void CreateCrosshair(Bounds screen, Vector3 normal, Vector3 up)
    {
        var right = Vector3.Cross(up, normal);
        var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right))) * 0.96f;
        var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up))) * 0.96f;
        var thickness = Mathf.Abs(Vector3.Dot(screen.extents, Abs(normal)));
        var lift = 0.004f / Mathf.Max(0.0001f, transform.lossyScale.magnitude / 1.732f);
        if (height < 1e-5f) return;

        const float canvasHeight = 200f;
        var go = new GameObject("Scope Crosshair", typeof(RectTransform), typeof(Canvas));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.localPosition = screen.center + normal * (thickness + lift);
        rect.localRotation = Quaternion.LookRotation(-normal, up);
        rect.localScale = Vector3.one * (height / canvasHeight * crosshairScale);
        rect.sizeDelta = new Vector2(canvasHeight * width / height, canvasHeight);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        const float gap = 12f;
        const float length = 26f;
        const float bar = 4f;
        AddBar(rect, new Vector2(0f, gap + length * 0.5f), new Vector2(bar, length));
        AddBar(rect, new Vector2(0f, -gap - length * 0.5f), new Vector2(bar, length));
        AddBar(rect, new Vector2(gap + length * 0.5f, 0f), new Vector2(length, bar));
        AddBar(rect, new Vector2(-gap - length * 0.5f, 0f), new Vector2(length, bar));
        AddBar(rect, Vector2.zero, new Vector2(bar, bar));
        SetCrosshairColor(crosshairColor);
    }

    void AddBar(RectTransform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        crosshair.Add(image);
    }

    void SetCrosshairColor(Color color)
    {
        foreach (var image in crosshair)
            if (image != null) image.color = color;
    }

    Vector3 AimDirection(Bounds bodyBounds, Bounds screenBounds, bool hasScreen)
    {
        switch (aimAxis)
        {
            case Axis.PlusX: return Vector3.right;
            case Axis.MinusX: return Vector3.left;
            case Axis.PlusY: return Vector3.up;
            case Axis.MinusY: return Vector3.down;
            case Axis.PlusZ: return Vector3.forward;
            case Axis.MinusZ: return Vector3.back;
        }

        // Barrel = longest local axis that stays mostly horizontal in the world.
        // SM_LaserWeapon's mesh is taller in Y, so "longest = Y" wrongly aimed the scope at the sky.
        var size = bodyBounds.size;
        var axis = Vector3.forward;
        var bestScore = float.NegativeInfinity;
        foreach (var candidate in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            var length = Vector3.Dot(size, Abs(candidate));
            var world = transform.TransformDirection(candidate).normalized;
            // 1 = flat along the ground, 0 = straight up/down (the green arrow the user rejected).
            var horizontal = 1f - Mathf.Abs(world.y);
            var score = length * (0.35f + horizontal);
            if (score <= bestScore) continue;
            bestScore = score;
            axis = candidate;
        }

        var half = Vector3.Dot(bodyBounds.extents, axis);
        if (hasScreen)
        {
            // Muzzle = the end where the screen sits behind the camera (LCD toward the grip).
            var muzzle = bodyBounds.center + axis * half;
            if (Vector3.Dot(muzzle - screenBounds.center, axis) < 0f) return -axis;
            return axis;
        }

        // Prefer the end that points along the object's forward, else the flatter world direction.
        var worldAxis = transform.TransformDirection(axis);
        if (Mathf.Abs(Vector3.Dot(worldAxis, transform.forward)) > 0.15f)
            return Vector3.Dot(worldAxis, transform.forward) >= 0f ? axis : -axis;
        return worldAxis.y >= 0f ? axis : -axis;
    }

    /// <summary>The thin side of the flat screen, pointing out of the weapon.</summary>
    static Vector3 ScreenNormal(Bounds screen, Bounds whole)
    {
        var s = screen.size;
        var axis = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
        var side = Vector3.Dot(screen.center - whole.center, axis);
        return side >= 0f ? axis : -axis;
    }

    /// <summary>Up of the picture: the screen's side axis that runs least along the barrel.</summary>
    static Vector3 ScreenUp(Vector3 normal, Vector3 aim)
    {
        Vector3 best = Vector3.up;
        var bestScore = float.MaxValue;
        foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.5f) continue;
            // Prefer the axis across the barrel; on a tie, the mesh's own up.
            var score = Mathf.Abs(Vector3.Dot(axis, aim)) - (axis == Vector3.up ? 0.01f : 0f);
            if (score >= bestScore) continue;
            bestScore = score;
            best = axis;
        }
        return best;
    }

    RenderTexture CreateTexture(Bounds screen, Vector3 normal, Vector3 up, bool hasScreen)
    {
        var aspect = 1f;
        if (hasScreen)
        {
            var right = Vector3.Cross(up, normal);
            var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right)));
            var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up)));
            if (width > 1e-5f && height > 1e-5f) aspect = Mathf.Clamp(width / height, 0.25f, 4f);
        }
        var rt = new RenderTexture(Mathf.RoundToInt(textureHeight * aspect), textureHeight, 24)
        {
            name = "Laser Weapon Scope",
            antiAliasing = 1,
        };
        rt.Create();
        return rt;
    }

    void CreateCamera(Bounds bodyBounds, Vector3 aimLocal, Vector3 upLocal)
    {
        var go = new GameObject("Scope Camera");
        go.transform.SetParent(transform, false);
        AlignScopeCamera(go.transform, bodyBounds, aimLocal, upLocal);

        scopeCamera = go.AddComponent<Camera>();
        scopeCamera.stereoTargetEye = StereoTargetEyeMask.None;
        scopeCamera.targetTexture = texture;
        scopeCamera.fieldOfView = fieldOfView;
        scopeCamera.nearClipPlane = 0.1f;
        scopeCamera.farClipPlane = farClip;
        scopeCamera.depth = -10f;

        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.allowXRRendering = false;
    }

    void CreateScreen(MeshRenderer weaponRenderer, Bounds screen, Vector3 normal, Vector3 up)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Scope Screen";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);

        var right = Vector3.Cross(up, normal);
        var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right)));
        var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up)));
        var thickness = Mathf.Abs(Vector3.Dot(screen.extents, Abs(normal)));
        // A millimetre or two above the glass so it never flickers through it.
        var lift = 0.002f / Mathf.Max(0.0001f, transform.lossyScale.magnitude / 1.732f);

        // The quad's visible side is -Z, so -Z points out of the screen.
        quad.transform.localPosition = screen.center + normal * (thickness + lift);
        quad.transform.localRotation = Quaternion.LookRotation(-normal, up);
        quad.transform.localScale = new Vector3(width * 0.96f, height * 0.96f, 1f);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        var material = new Material(shader) { name = "Scope Screen" };
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

        screenRenderer = quad.GetComponent<MeshRenderer>();
        screenRenderer.sharedMaterial = material;
        screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        screenRenderer.receiveShadows = false;
    }

    void MakeGrabbable(Bounds localBounds)
    {
        if (GetComponent<XRGrabInteractable>() != null) return;

        gameObject.isStatic = false;
        foreach (var meshCollider in GetComponents<MeshCollider>()) meshCollider.enabled = false;
        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.center = localBounds.center;
        box.size = localBounds.size;

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin != null)
            foreach (var own in origin.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(box, own, true);

        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.mass = 2f;
        // Stays where it lies until picked up.
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        grab = gameObject.AddComponent<XRGrabInteractable>();
        grab.interactionLayers = -1;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.useDynamicAttach = true;
        grab.throwOnDetach = true;
        grab.forceGravityOnDetach = true;
        grab.colliders.Clear();
        grab.colliders.Add(box);
        NearOnlyGrabFilter.AddTo(grab);
        // The grab restores the body's start state on release; it was kinematic, so let it fall instead.
        grab.selectExited.AddListener(_ =>
        {
            if (grab.isSelected) return;
            body.isKinematic = false;
            body.useGravity = true;
        });
    }

    void LateUpdate()
    {
        if (scopeCamera == null) return;
        if (createdCamera) scopeCamera.fieldOfView = fieldOfView;
        // Only render while someone can see the screen (or it is in hand).
        var held = grab != null && grab.isSelected;
        scopeCamera.enabled = held || screenRenderer == null || screenRenderer.isVisible;
        UpdateLock(held);

        // Dropped through the ground or thrown far away: back to where it started.
        if (body != null && !held && transform.position.y < homePosition.y - 30f)
        {
            body.isKinematic = true;
            body.useGravity = false;
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }
    }

    void UpdateLock(bool held)
    {
        var onTarget = !missileAway && !targetDown && (held || !requireHeld) && IsOnTarget();
        lockTimer = onTarget ? lockTimer + Time.deltaTime : 0f;
        SetCrosshairColor(onTarget ? lockedColor : crosshairColor);

        if (lockTimer < lockSeconds) return;
        lockTimer = 0f;
        StartCoroutine(MissileToTarget());
    }

    bool IsOnTarget()
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        var from = scopeCamera.transform.position;

        // Straight down the crosshair.
        var hits = Physics.RaycastAll(from, scopeCamera.transform.forward, farClip, ~0, QueryTriggerInteraction.Collide);
        var nearestOther = float.MaxValue;
        var targetHit = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(target)) targetHit = Mathf.Min(targetHit, hit.distance);
            else if (!hit.collider.isTrigger && !hit.collider.transform.IsChildOf(transform))
                nearestOther = Mathf.Min(nearestOther, hit.distance);
        }
        if (targetHit < nearestOther) return true;

        // Or just off that line (a far Shahed is tiny), as long as nothing (ground, hills) is in the way.
        if (aimToleranceDegrees <= 0f || !TargetBounds(out var bounds)) return false;
        var toTarget = bounds.center - from;
        if (toTarget.magnitude > farClip) return false;
        if (Vector3.Angle(scopeCamera.transform.forward, toTarget) > aimToleranceDegrees) return false;

        foreach (var hit in Physics.RaycastAll(from, toTarget.normalized, toTarget.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(target) || hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    System.Collections.IEnumerator MissileToTarget()
    {
        missileAway = true;
        if (missileLaunchOrigin == null)
            FindMissileLaunchOrigin();
        if (missileLaunchOrigin == null || target == null || !target.gameObject.activeInHierarchy)
        {
            Debug.LogWarning(name + ": cannot launch the lock-on missile because its launcher or target is missing.", this);
            missileAway = false;
            yield break;
        }

        var start = missileLaunchOrigin.TransformPoint(missileLaunchOffset);
        var aim = TargetBounds(out var targetBounds) ? targetBounds.center : target.position;
        var heading = aim - start;
        if (heading.sqrMagnitude < 0.0001f) heading = missileLaunchOrigin.forward;
        heading.Normalize();
        if (missileLaunchSound != null)
            AudioSource.PlayClipAtPoint(missileLaunchSound, start, missileLaunchVolume);

        GameObject missile;
        if (missilePrefab != null)
        {
            missile = Instantiate(missilePrefab, start, Quaternion.LookRotation(heading));
            missile.transform.localScale *= missileScale;
        }
        else
        {
            missile = BuildMissile(start, heading);
        }
        var trail = LaunchSmoke.CreateTrail(0.6f);
        trail.transform.SetPositionAndRotation(start, Quaternion.LookRotation(-heading));

        // Homing: speeds up each frame so it always reaches the moving target on time.
        var elapsed = 0f;
        var position = start;
        while (missile != null && target != null && target.gameObject.activeInHierarchy)
        {
            elapsed += Time.deltaTime;
            aim = TargetBounds(out var b) ? b.center : target.position;
            var toAim = aim - position;
            var remaining = Mathf.Max(0.05f, missileSeconds - elapsed);
            var step = toAim.magnitude * Mathf.Clamp01(Time.deltaTime / remaining);
            if (toAim.magnitude <= 1f || step >= toAim.magnitude) { position = aim; break; }

            // A little of the launch heading at first, then straight in.
            var direction = Vector3.Slerp(heading, toAim.normalized, Mathf.Clamp01(elapsed / (missileSeconds * 0.4f)));
            position += direction * step;
            missile.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            trail.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-direction));
            yield return null;
        }

        LaunchSmoke.StopAndRelease(trail);
        if (missile != null) Destroy(missile);
        DestroyTarget(position);
    }

    void DestroyTarget(Vector3 at)
    {
        if (target == null || !target.gameObject.activeInHierarchy) { missileAway = false; return; }
        var size = TargetBounds(out var bounds)
            ? Mathf.Clamp(bounds.size.magnitude / 4f, 0.8f, 6f) * killExplosionScale
            : 2f * killExplosionScale;
        var center = TargetBounds(out bounds) ? bounds.center : at;

        // Blast stays in world space so it is not dragged by the drone before it disappears.
        LaunchSmoke.CreateExplosionBurst(center, size);
        LaunchSmoke.CreateFire(center, size * 0.35f, size * 0.9f, 2.5f);

        FreezeAndHideTarget();
        targetDown = true;
        missileAway = false;
        SetCrosshairColor(crosshairColor);
        StartCoroutine(ShowKillMessage());
    }

    void FreezeAndHideTarget()
    {
        if (target == null) return;

        // Stop every mover so the wreck does not keep drifting after the hit.
        foreach (var jet in target.GetComponentsInChildren<FighterJetFlight>(true))
        {
            jet.StopFlying();
            jet.enabled = false;
        }
        foreach (var animator in target.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
            animator.speed = 0f;
        }
        foreach (var rb in target.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (var col in target.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        target.gameObject.SetActive(false);
    }

    System.Collections.IEnumerator ShowKillMessage()
    {
        if (string.IsNullOrWhiteSpace(killMessage)) yield break;

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null || origin.Camera == null) yield break;
        var hand = XrHandsRig.FindNamed(origin, "Right Controller");
        if (hand == null) hand = origin.Camera.transform;

        var banner = HandHintBanner.Create(origin.Camera, killMessageFont, killMessage);
        banner.SetHand(hand);
        yield return new WaitForSeconds(killMessageSeconds);
        if (banner != null) banner.Hide();
    }

    void FindMissileLaunchOrigin()
    {
        if (missileLaunchOrigin != null || string.IsNullOrWhiteSpace(missileLaunchOriginName)) return;
        var launcher = GameObject.Find(missileLaunchOriginName);
        if (launcher != null)
            missileLaunchOrigin = launcher.transform;
        else
            Debug.LogWarning(name + ": missile launcher '" + missileLaunchOriginName + "' was not found in the scene.", this);
    }

    static GameObject BuildMissile(Vector3 position, Vector3 heading)
    {
        var root = new GameObject("Scope Missile");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
            body.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.8f, 0.8f, 0.78f) };
        return root;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void OnDestroy()
    {
        if (texture == null) return;
        texture.Release();
        Destroy(texture);
    }
}
