using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRGallery
{
    /// <summary>
    /// Put this on the ROOT of a picture frame (the object that holds the image + optional close button).
    /// When opened, the panel appears in front of the player and follows their
    /// horizontal heading, with limited movement when the head changes height.
    ///
    /// Modes:
    ///   Lazy   - stays put until you turn away / walk away, then glides back in front of you (most comfortable in VR).
    ///   Locked - always smoothly glued in front of your view.
    ///   Static - appears in front of you once, then stays there.
    /// </summary>
    [DisallowMultipleComponent]
    public class VRFollowFrame : MonoBehaviour
    {
        public enum FollowMode { Lazy, Locked, Static }

        /// <summary>Which local axis of the panel's transform is the visible front face.</summary>
        public enum FrontAxis { PosZ, NegZ, PosX, NegX, PosY, NegY }

        [System.Serializable]
        public class PictureSlot
        {
            [Tooltip("The picture to show.")]
            public Texture texture;
            [Tooltip("Optional: show/hide this complete side when its picture changes.")]
            public GameObject panelRoot;
            [Tooltip("The quad / mesh that displays the picture.")]
            public Renderer display;
            [Tooltip("...or a UI Raw Image that displays the picture. Use either this or Display.")]
            public RawImage image;
        }

        [Header("Target")]
        [Tooltip("Player's head (the XR camera). Leave empty to use Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Optional body/yaw anchor (usually XR Origin). Leave empty to follow headset yaw, including physical turns.")]
        [SerializeField] private Transform bodyHeading;

        [Header("Placement")]
        [Tooltip("Meters in front of the player.")]
        [SerializeField, Min(0.3f)] private float distance = 1.6f;
        [Tooltip("Vertical offset from eye level (meters).")]
        [SerializeField] private float heightOffset = 0f;
        [Tooltip("Sideways offset (meters). Useful to place several frames next to each other.")]
        [SerializeField] private float lateralOffset = 0f;
        [Tooltip("Ignore looking up/down when placing the frame, so it stays at eye level.")]
        [SerializeField] private bool yawOnly = true;
        [Tooltip("Maximum vertical movement from the initial eye height (crouching won't pull the panel down).")]
        [SerializeField, Min(0f)] private float maxVerticalFollow = 0.18f;
        [Tooltip("Follow the camera's full height, smoothed by Position Smooth Time.")]
        [SerializeField] private bool followCameraHeight;

        [Header("Follow")]
        [SerializeField] private FollowMode followMode = FollowMode.Locked;
        [Tooltip("Lazy mode: how far (degrees) you can look away before the frame comes after you.")]
        [SerializeField, Range(5f, 90f)] private float lazyAngle = 30f;
        [Tooltip("Lazy mode: how far (meters) the frame can drift from its ideal spot before it follows.")]
        [SerializeField, Min(0.05f)] private float lazyDistance = 0.5f;
        [Tooltip("Lower = snappier, higher = floatier.")]
        [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.35f;
        [Tooltip("Keep the panel facing the player as it follows them.")]
        [SerializeField] private bool facePlayer = true;
        [Tooltip("Which local axis of THIS transform is the visual front of the panel (the side the picture actually faces). " +
                 "Unity's default Quad faces -Z, but a Plane faces +Y, and imported models vary. " +
                 "If the frame faces the sky/floor/sideways instead of the player, change this until it's right.")]
        [SerializeField] private FrontAxis frontAxis = FrontAxis.PosZ;
        [Tooltip("Fine-tune rotation (degrees, applied last) for any small residual tilt after the facing axis is set correctly.")]
        [SerializeField] private Vector3 facingRotationOffset = Vector3.zero;
        [SerializeField, Min(0.01f)] private float rotationSmoothTime = 0.2f;

        [Header("Open / Close")]
        [Tooltip("If true the frame is hidden when the scene starts.")]
        [SerializeField] private bool startClosed = true;
        [Tooltip("Optional. A button on the frame that closes it.")]
        [SerializeField] private Button closeButton;

        [Header("Pictures (swapped in when this frame opens)")]
        [SerializeField] private PictureSlot leftPicture = new PictureSlot();
        [SerializeField] private PictureSlot rightPicture = new PictureSlot();

        [Header("Single-content layout")]
        [Tooltip("Optional single-frame geometry. Keep picture/video display objects outside these geometry roots.")]
        [SerializeField] private GameObject singleFrameRoot;
        [Tooltip("Optional two-picture frame geometry. Hidden for one picture or a video.")]
        [SerializeField] private GameObject twoPictureFrameRoot;
        [Tooltip("Local X coordinate for a lone picture or video. Their original X positions return for two pictures.")]
        [SerializeField] private float singleContentX = 0f;

        [Header("Video (optional, for video entries)")]
        [Tooltip("Optional existing VideoPlayer. If empty, one is added to this frame when needed.")]
        [SerializeField] private VideoPlayer videoPlayer;
        [Tooltip("UI video screen (world-space RawImage). Use this or Video Display.")]
        [SerializeField] private RawImage videoImage;
        [Tooltip("Mesh video screen. Use this or Video Image.")]
        [SerializeField] private Renderer videoDisplay;
        [Tooltip("Optional container for the video screen; hidden when showing pictures.")]
        [SerializeField] private GameObject videoPanelRoot;
        [Tooltip("Optional supplied RenderTexture. Otherwise a private one is created from the clip size.")]
        [SerializeField] private RenderTexture videoTexture;
        [SerializeField] private bool loopVideo = true;

        [Header("Events")]
        public UnityEvent onOpened;
        public UnityEvent onClosed;

        public bool IsOpen { get; private set; }

        private Vector3 velocity;
        private Vector3 lastFlatForward = Vector3.forward;
        private bool chasing;
        private bool pendingSnap;
        private Vector3 followedHeadPosition;
        private float followedYaw;
        private float yawVelocity;
        private float openingEyeHeight;
        private float openingBodyHeight;
        private bool showVideo;
        private VideoClip currentVideo;
        private VideoPlayer subscribedPlayer;
        private RenderTexture ownedVideoTexture;
        private bool warnedNoHead;
        private bool placementsCaptured;
        private ContentPlacement leftPlacement;
        private ContentPlacement rightPlacement;
        private ContentPlacement videoPlacement;

        private struct ContentPlacement
        {
            public Transform target;
            public Vector3 localPosition;
            public RectTransform rect;
            public Vector2 anchoredPosition;
        }

        // ───────────────────────── Unity lifecycle ─────────────────────────

        private void Awake()
        {
            if (videoPlayer != null)
            {
                videoPlayer.playOnAwake = false;
                videoPlayer.Stop();
            }
            if (IsOpen) return; // Open() was called before Awake ran (frame started inactive)

            if (startClosed)
            {
                gameObject.SetActive(false);
            }
            else
            {
                IsOpen = true;
                pendingSnap = true;
                ApplyContent();
            }
        }

        private void OnEnable()
        {
            ConfigureUI();
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            StopVideo();
            IsOpen = false;
            pendingSnap = true;
        }

        private void ConfigureUI()
        {
            ResolveHead();
            Camera eventCamera = head != null ? head.GetComponent<Camera>() : null;
            foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                if (eventCamera != null) canvas.worldCamera = eventCamera;
                var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (raycaster == null)
                    raycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                raycaster.enabled = true;
                // Frame geometry must not occlude its own controls.
                raycaster.checkFor2DOcclusion = false;
                raycaster.checkFor3DOcclusion = false;
            }
            if (videoImage != null) videoImage.raycastTarget = false;
            if (closeButton == null) return;
            Graphic hitArea = closeButton.GetComponent<Graphic>();
            if (hitArea != null)
            {
                hitArea.enabled = true;
                hitArea.raycastTarget = true;
            }
            if (closeButton.targetGraphic != null)
                closeButton.targetGraphic.raycastTarget = true;
        }

        private void LateUpdate()
        {
            if (!IsOpen || !ResolveHead()) return;

            if (pendingSnap)
            {
                Recenter();
                pendingSnap = false;
                return;
            }

            Vector3 desired = GetDesiredPosition();

            switch (followMode)
            {
                case FollowMode.Locked: chasing = true; break;
                case FollowMode.Lazy: UpdateChaseState(desired); break;
                default: chasing = false; break;
            }

            if (chasing)
            {
                if (followMode == FollowMode.Locked)
                {
                    // Smooth heading around the viewer, so turns preserve viewing distance.
                    Vector3 forward = GetPlacementForward();
                    float targetYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                    followedYaw = Mathf.SmoothDampAngle(followedYaw, targetYaw, ref yawVelocity,
                        positionSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                    followedHeadPosition = Vector3.SmoothDamp(followedHeadPosition,
                        GetPlacementAnchor(), ref velocity, positionSmoothTime,
                        Mathf.Infinity, Time.unscaledDeltaTime);
                    Quaternion heading = Quaternion.Euler(0f, followedYaw, 0f);
                    transform.position = followedHeadPosition + heading *
                        new Vector3(lateralOffset, 0f, distance);
                }
                else
                    transform.position = Vector3.SmoothDamp(
                        transform.position, desired, ref velocity,
                        positionSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                if (facePlayer)
                    transform.rotation = Quaternion.Slerp(transform.rotation, GetDesiredRotation(),
                        1f - Mathf.Exp(-Time.unscaledDeltaTime / rotationSmoothTime));
            }
            else
            {
                velocity = Vector3.zero;
            }
        }

        // ───────────────────────── Public API ─────────────────────────

        /// <summary>Shows the frame in front of the player.</summary>
        public void Open()
        {
            if (IsOpen && gameObject.activeInHierarchy)
            {
                Recenter();
                return;
            }

            IsOpen = true;                  // set BEFORE activating so Awake doesn't hide us again
            pendingSnap = true;
            if (ResolveHead()) Recenter();
            gameObject.SetActive(true);
            ApplyContent();
            if (showVideo) StartVideo();
            onOpened?.Invoke();
        }

        /// <summary>Hides the frame.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            StopVideo();
            gameObject.SetActive(false);
            onClosed?.Invoke();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Instantly puts the frame in front of the player (no gliding).</summary>
        public void Recenter()
        {
            if (!ResolveHead()) return;
            openingEyeHeight = head.position.y;
            openingBodyHeight = bodyHeading != null ? bodyHeading.position.y : 0f;
            followedHeadPosition = GetPlacementAnchor();
            Vector3 forward = GetPlacementForward();
            followedYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            yawVelocity = 0f;
            pendingSnap = false;
            transform.position = GetDesiredPosition();
            if (facePlayer) transform.rotation = GetDesiredRotation();
            velocity = Vector3.zero;
            chasing = false;
        }

        // ───────────────────────── Pictures ─────────────────────────

        /// <summary>Changes both pictures at runtime. If the frame is open they update immediately.</summary>
        public void SetPictures(Texture left, Texture right)
        {
            StopVideo();
            showVideo = false;
            if (leftPicture == null) leftPicture = new PictureSlot();
            if (rightPicture == null) rightPicture = new PictureSlot();
            leftPicture.texture = left;
            rightPicture.texture = right;
            if (IsOpen) ApplyContent();
        }

        /// <summary>Choose a video for this frame. It starts when the frame opens.</summary>
        public void SetVideo(VideoClip clip)
        {
            StopVideo();
            currentVideo = clip;
            showVideo = clip != null;
            if (IsOpen)
            {
                ApplyContent();
                if (showVideo && gameObject.activeInHierarchy) StartVideo();
            }
        }

        /// <summary>Puts the left and right textures onto their displays.</summary>
        public void ApplyPictures()
        {
            ApplySlot(leftPicture, !showVideo);
            ApplySlot(rightPicture, !showVideo);
        }

        private void ApplyContent()
        {
            if (!placementsCaptured)
            {
                leftPlacement = CapturePlacement(leftPicture?.panelRoot,
                    leftPicture?.image, leftPicture?.display);
                rightPlacement = CapturePlacement(rightPicture?.panelRoot,
                    rightPicture?.image, rightPicture?.display);
                videoPlacement = CapturePlacement(videoPanelRoot, videoImage, videoDisplay);
                placementsCaptured = true;
            }

            bool leftVisible = !showVideo && leftPicture?.texture != null;
            bool rightVisible = !showVideo && rightPicture?.texture != null;
            bool single = showVideo || leftVisible != rightVisible;

            if (singleFrameRoot != null && singleFrameRoot != gameObject &&
                singleFrameRoot != twoPictureFrameRoot)
                singleFrameRoot.SetActive(single);
            if (twoPictureFrameRoot != null && twoPictureFrameRoot != gameObject &&
                twoPictureFrameRoot != singleFrameRoot)
                twoPictureFrameRoot.SetActive(!single);

            SetCentered(leftPlacement, single && leftVisible);
            SetCentered(rightPlacement, single && rightVisible);
            SetCentered(videoPlacement, showVideo);
            ApplyPictures();
            if (videoPanelRoot != null && videoPanelRoot != gameObject)
                videoPanelRoot.SetActive(showVideo);
            if (videoImage != null) videoImage.enabled = showVideo;
            if (videoDisplay != null) videoDisplay.enabled = showVideo;
        }

        private ContentPlacement CapturePlacement(GameObject panelRoot, RawImage image,
            Renderer display)
        {
            Transform target = panelRoot != null && panelRoot != gameObject
                ? panelRoot.transform : image != null ? image.transform
                : display != null ? display.transform : null;
            if (target == transform) target = null;
            var placement = new ContentPlacement { target = target };
            if (target == null) return placement;

            placement.localPosition = target.localPosition;
            placement.rect = target as RectTransform;
            if (placement.rect != null) placement.anchoredPosition = placement.rect.anchoredPosition;
            return placement;
        }

        private void SetCentered(ContentPlacement placement, bool center)
        {
            if (placement.target == null) return;
            placement.target.localPosition = placement.localPosition;
            if (placement.rect != null)
                placement.rect.anchoredPosition = placement.anchoredPosition;
            if (!center) return;

            Vector3 worldCenter = placement.rect != null
                ? placement.rect.TransformPoint(placement.rect.rect.center)
                : placement.target.position;
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            localCenter.x = singleContentX;
            placement.target.position += transform.TransformPoint(localCenter) - worldCenter;
        }

        private void ApplySlot(PictureSlot slot, bool pictureMode)
        {
            if (slot == null) return;
            bool visible = pictureMode && slot.texture != null;
            if (slot.panelRoot != null && slot.panelRoot != gameObject)
                slot.panelRoot.SetActive(visible);
            if (slot.image != null)
            {
                slot.image.texture = slot.texture;
                slot.image.enabled = visible;
            }
            if (slot.display != null)
            {
                slot.display.enabled = visible;
                var block = new MaterialPropertyBlock();
                slot.display.GetPropertyBlock(block);
                block.SetTexture("_BaseMap", slot.texture != null ? slot.texture : Texture2D.blackTexture);
                block.SetTexture("_MainTex", slot.texture != null ? slot.texture : Texture2D.blackTexture);
                slot.display.SetPropertyBlock(block);
            }
        }

        private void StartVideo()
        {
            if (currentVideo == null) return;
            if (videoImage == null && videoDisplay == null)
            {
                Debug.LogWarning("VRFollowFrame: Assign a Video Image or Video Display to show videos.", this);
                return;
            }

            if (videoPlayer == null) videoPlayer = gameObject.AddComponent<VideoPlayer>();
            if (subscribedPlayer != videoPlayer)
            {
                if (subscribedPlayer != null)
                {
                    subscribedPlayer.prepareCompleted -= OnVideoPrepared;
                    subscribedPlayer.errorReceived -= OnVideoError;
                }
                subscribedPlayer = videoPlayer;
                subscribedPlayer.prepareCompleted += OnVideoPrepared;
                subscribedPlayer.errorReceived += OnVideoError;
            }

            RenderTexture target = videoTexture != null ? videoTexture : GetOrCreateVideoTexture(currentVideo);
            videoPlayer.Stop();
            videoPlayer.playOnAwake = false;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = currentVideo;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = target;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.isLooping = loopVideo;
            if (videoImage != null) videoImage.texture = target;
            if (videoDisplay != null)
            {
                var block = new MaterialPropertyBlock();
                videoDisplay.GetPropertyBlock(block);
                block.SetTexture("_BaseMap", target);
                block.SetTexture("_MainTex", target);
                videoDisplay.SetPropertyBlock(block);
            }
            videoPlayer.Prepare();
        }

        private RenderTexture GetOrCreateVideoTexture(VideoClip clip)
        {
            int width = Mathf.Clamp((int)clip.width, 256, 2048);
            int height = Mathf.Clamp((int)clip.height, 256, 2048);
            if (ownedVideoTexture != null &&
                (ownedVideoTexture.width != width || ownedVideoTexture.height != height))
            {
                ownedVideoTexture.Release();
                Destroy(ownedVideoTexture);
                ownedVideoTexture = null;
            }
            if (ownedVideoTexture == null)
            {
                ownedVideoTexture = new RenderTexture(width, height, 0);
                ownedVideoTexture.name = "Gallery Video " + name;
                ownedVideoTexture.Create();
            }
            return ownedVideoTexture;
        }

        private void OnVideoPrepared(VideoPlayer prepared)
        {
            if (IsOpen && showVideo && gameObject.activeInHierarchy && prepared == videoPlayer)
                prepared.Play();
        }

        private void OnVideoError(VideoPlayer player, string message)
        {
            Debug.LogWarning($"VRFollowFrame: Video could not play: {message}", this);
        }

        private void StopVideo()
        {
            if (videoPlayer != null) videoPlayer.Stop();
        }

        private void OnDestroy()
        {
            if (subscribedPlayer != null)
            {
                subscribedPlayer.prepareCompleted -= OnVideoPrepared;
                subscribedPlayer.errorReceived -= OnVideoError;
            }
            if (ownedVideoTexture == null) return;
            if (videoPlayer != null && videoPlayer.targetTexture == ownedVideoTexture)
                videoPlayer.targetTexture = null;
            ownedVideoTexture.Release();
            Destroy(ownedVideoTexture);
        }

        // ───────────────────────── Placement helpers ─────────────────────────

        private bool ResolveHead()
        {
            if (head != null) return true;
            if (Camera.main != null) head = Camera.main.transform;
            if (head == null && !warnedNoHead)
            {
                Debug.LogWarning($"[{nameof(VRFollowFrame)}] No head assigned and no camera tagged MainCamera found.", this);
                warnedNoHead = true;
            }
            return head != null;
        }

        private Vector3 GetPlacementForward()
        {
            Vector3 f = bodyHeading != null ? bodyHeading.forward : head.forward;

            if (yawOnly)
            {
                f.y = 0f;
                if (f.sqrMagnitude < 0.01f)
                {
                    // Looking almost straight up/down: use the head's up vector to keep a sensible direction.
                    Transform heading = bodyHeading != null ? bodyHeading : head;
                    f = heading.forward.y < 0f ? heading.up : -heading.up;
                    f.y = 0f;
                }
                if (f.sqrMagnitude < 0.0001f) f = lastFlatForward;
                f.Normalize();
                lastFlatForward = f;
            }
            else
            {
                f.Normalize();
            }
            return f;
        }

        private Vector3 GetDesiredPosition()
        {
            Vector3 fwd = GetPlacementForward();
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = lastFlatForward;
            fwd.Normalize();
            lastFlatForward = fwd;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            return GetPlacementAnchor() + fwd * distance + right * lateralOffset;
        }

        private Vector3 GetPlacementAnchor()
        {
            if (followCameraHeight)
                return head.position + Vector3.up * heightOffset;
            float bodyRise = bodyHeading != null ? bodyHeading.position.y - openingBodyHeight : 0f;
            float crouch = Mathf.Clamp(head.position.y - openingEyeHeight - bodyRise,
                -maxVerticalFollow, maxVerticalFollow);
            float y = openingEyeHeight + bodyRise + crouch + heightOffset;
            return new Vector3(head.position.x, y, head.position.z);
        }

        private Quaternion GetDesiredRotation()
        {
            Vector3 direction = head.position - transform.position;
            if (yawOnly) direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -GetPlacementForward();
            direction.Normalize();

            // Rotation that points world +Z at the player.
            Quaternion lookAtPlayer = Quaternion.LookRotation(direction, Vector3.up);

            // Correct for whichever local axis is actually the panel's visible front,
            // so the chosen frontAxis (not just +Z) ends up pointing at the player.
            Quaternion axisCorrection = Quaternion.FromToRotation(FrontAxisVector(frontAxis), Vector3.forward);

            return lookAtPlayer * axisCorrection * Quaternion.Euler(facingRotationOffset);
        }

        private static Vector3 FrontAxisVector(FrontAxis axis)
        {
            switch (axis)
            {
                case FrontAxis.PosZ: return Vector3.forward;
                case FrontAxis.NegZ: return Vector3.back;
                case FrontAxis.PosX: return Vector3.right;
                case FrontAxis.NegX: return Vector3.left;
                case FrontAxis.PosY: return Vector3.up;
                case FrontAxis.NegY: return Vector3.down;
                default: return Vector3.forward;
            }
        }

        private void UpdateChaseState(Vector3 desired)
        {
            Vector3 toDesired = desired - head.position;
            Vector3 toFrame = transform.position - head.position;
            if (yawOnly) { toDesired.y = 0f; toFrame.y = 0f; }

            float angle = Vector3.Angle(toDesired, toFrame);
            float dist = Vector3.Distance(transform.position, desired);

            if (!chasing && (angle > lazyAngle || dist > lazyDistance))
                chasing = true;                                   // you looked / walked away
            else if (chasing && angle < 3f && dist < 0.05f)
                chasing = false;                                  // back in front of you, settle
        }
    }
}