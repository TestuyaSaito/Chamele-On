using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A package-free, collision-aware third-person orbit camera.
/// AddOrbitInput receives yaw/pitch deltas in degrees. Call Tick manually after
/// SetAutoUpdate(false), or leave automatic updating enabled (the default).
/// </summary>
[DisallowMultipleComponent]
public sealed class ThirdPersonCameraRig : MonoBehaviour
{
    [Header("Framing")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.15f, 0f);
    [SerializeField] private float shoulderOffset = 0.30f;
    [SerializeField] private float orbitDistance = 3.5f;
    [SerializeField] private float normalFieldOfView = 58f;
    [SerializeField] private float focusedShoulderOffset = -0.45f;
    [SerializeField] private float focusedDistance = 2.9f;
    [SerializeField] private float focusedFieldOfView = 50f;
    [SerializeField] private float attachedShoulderOffset;
    [SerializeField] private float attachedDistance = 2.6f;
    [SerializeField] private float attachedFieldOfView = 52f;

    [Header("Orbit")]
    [SerializeField] private float minimumPitch = -12f;
    [SerializeField] private float maximumPitch = 67f;
    [SerializeField] private float initialPitch = 10f;
    [SerializeField] private float mouseSensitivity = 0.18f;
    [SerializeField] private float touchSensitivity = 0.12f;
    [SerializeField] private int mouseOrbitButton = 1;
    [SerializeField] private float orbitSmoothTime = 0.055f;
    [SerializeField] private float targetSmoothTime = 0.035f;

    [Header("Movement Follow (Mobile Assist)")]
    [SerializeField] private bool followMovementHeading = true;
    [SerializeField] private float movementFollowDelay = 0.60f;
    [SerializeField] private float movementFollowSpeed = 110f;
    [SerializeField] private float movementFollowDeadZone = 3.5f;
    [SerializeField] private float movementFollowMinimumStrength = 0.15f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.24f;
    [SerializeField] private float collisionPadding = 0.08f;
    [SerializeField] private float minimumDistance = 0.42f;
    [SerializeField] private float collisionReleaseSmoothTime = 0.18f;

    [Header("Runtime")]
    [SerializeField] private bool autoUpdate = true;
    [SerializeField] private bool readLegacyPointerInput = true;

    private const int CollisionHitCapacity = 48;

    private readonly RaycastHit[] collisionHits = new RaycastHit[CollisionHitCapacity];
    private readonly HashSet<int> uiBlockedTouches = new HashSet<int>();

    private Camera drivenCamera;
    private Transform followTarget;
    private bool initialized;
    private bool orbitEnabled = true;
    private bool focusedMode;
    private bool attachedMode;
    private bool mouseDragging;
    private bool mouseDragBlocked;
    private bool movementFollowActive;
    private int orbitTouchId = -1;

    private float desiredYaw;
    private float desiredPitch;
    private float currentYaw;
    private float currentPitch;
    private float yawVelocity;
    private float pitchVelocity;
    private float currentDistance;
    private float distanceVelocity;
    private float fieldOfViewVelocity;
    private float movementHeadingYaw;
    private float movementStrength;
    private float manualOrbitGraceRemaining;
    private Vector3 smoothedTargetPosition;
    private Vector3 targetPositionVelocity;
    private Vector2 queuedOrbitDegrees;
    private Vector3 lastMousePosition;

    public Camera DrivenCamera => drivenCamera;
    public Transform Target => followTarget;
    public float Yaw => desiredYaw;
    public float Pitch => desiredPitch;
    public bool IsFollowingMovement => movementFollowActive;
    public float MovementHeadingYaw => movementHeadingYaw;

    public void SetMovementFollowEnabled(bool isEnabled)
    {
        followMovementHeading = isEnabled;
        if (!isEnabled)
        {
            ClearMovementHeading();
        }
    }

    /// <summary>Camera-relative forward projected onto the horizontal plane.</summary>
    public Vector3 PlanarForward
    {
        get
        {
            Vector3 forward = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
            return forward.normalized;
        }
    }

    /// <summary>Camera-relative right projected onto the horizontal plane.</summary>
    public Vector3 PlanarRight => Vector3.Cross(Vector3.up, PlanarForward).normalized;

    public void Initialize(Camera cameraToDrive, Transform targetToFollow)
    {
        drivenCamera = cameraToDrive;
        followTarget = targetToFollow;
        initialized = drivenCamera != null && followTarget != null;

        if (!initialized)
        {
            Debug.LogError("ThirdPersonCameraRig requires both a Camera and a target Transform.", this);
            enabled = false;
            return;
        }

        enabled = true;
        drivenCamera.orthographic = false;

        Vector3 anchor = followTarget.position + targetOffset;
        Vector3 lookDirection = anchor - drivenCamera.transform.position;
        if (lookDirection.sqrMagnitude > 0.01f)
        {
            Vector3 angles = Quaternion.LookRotation(lookDirection.normalized, Vector3.up).eulerAngles;
            desiredYaw = currentYaw = angles.y;
            desiredPitch = currentPitch = ClampPitch(NormalizeSignedAngle(angles.x));
        }
        else
        {
            desiredYaw = currentYaw = followTarget.eulerAngles.y;
            desiredPitch = currentPitch = ClampPitch(initialPitch);
        }

        smoothedTargetPosition = CalculateRawAnchor(currentYaw);
        targetPositionVelocity = Vector3.zero;
        currentDistance = GetModeDistance();
        distanceVelocity = 0f;
        drivenCamera.fieldOfView = GetModeFieldOfView();
        fieldOfViewVelocity = 0f;
        queuedOrbitDegrees = Vector2.zero;
        movementFollowActive = false;
        movementHeadingYaw = desiredYaw;
        movementStrength = 0f;
        manualOrbitGraceRemaining = 0f;

        ApplyCameraPose(0f, true);
    }

    public void SetOrbitEnabled(bool isEnabled)
    {
        orbitEnabled = isEnabled;
        if (!isEnabled)
        {
            queuedOrbitDegrees = Vector2.zero;
            ResetPointerTracking();
        }
    }

    public void SetFocusedMode(bool isFocused)
    {
        focusedMode = isFocused;
    }

    /// <summary>
    /// Enables the closer wall-attachment framing. Attachment controls distance
    /// and field of view; focused paint composition still wins for the lateral
    /// shoulder offset when both modes are active.
    /// </summary>
    public void SetAttachedMode(bool isAttached)
    {
        attachedMode = isAttached;
        if (isAttached)
        {
            ClearMovementHeading();
        }
    }

    /// <summary>
    /// Supplies the character's intended world-space travel heading. When the
    /// player is not manually orbiting, the camera eases behind this heading.
    /// </summary>
    public void SetMovementHeading(float worldYaw, float strength)
    {
        movementHeadingYaw = NormalizeSignedAngle(worldYaw);
        movementStrength = Mathf.Clamp01(strength);
        movementFollowActive = followMovementHeading && movementStrength >= movementFollowMinimumStrength;
    }

    /// <summary>Stops automatic yaw changes until a new movement heading arrives.</summary>
    public void ClearMovementHeading()
    {
        movementFollowActive = false;
        movementStrength = 0f;
    }

    public void SetTargetOffset(Vector3 worldOffset, bool snapImmediately)
    {
        targetOffset = worldOffset;
        if (snapImmediately)
        {
            SnapToTarget();
        }
    }

    public void SetOrbitAngles(float yaw, float pitch, bool snapImmediately)
    {
        desiredYaw = yaw;
        desiredPitch = ClampPitch(pitch);
        queuedOrbitDegrees = Vector2.zero;
        if (snapImmediately)
        {
            currentYaw = desiredYaw;
            currentPitch = desiredPitch;
            SnapToTarget();
        }
    }

    /// <summary>
    /// Queues an orbit change in degrees. Positive X turns right; positive Y looks up.
    /// The change is consumed by the next Tick/LateUpdate.
    /// </summary>
    public void AddOrbitInput(Vector2 orbitDeltaDegrees)
    {
        if (orbitEnabled && orbitDeltaDegrees.sqrMagnitude > 0.000001f)
        {
            queuedOrbitDegrees += orbitDeltaDegrees;
            manualOrbitGraceRemaining = movementFollowDelay;
        }
    }

    public void SetAutoUpdate(bool shouldAutoUpdate)
    {
        autoUpdate = shouldAutoUpdate;
    }

    public void SetLegacyPointerInputEnabled(bool shouldReadInput)
    {
        readLegacyPointerInput = shouldReadInput;
        if (!shouldReadInput)
        {
            ResetPointerTracking();
        }
    }

    /// <summary>
    /// Runs one camera update. For controller-driven updates, call SetAutoUpdate(false)
    /// once and invoke this from LateUpdate.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!initialized || drivenCamera == null || followTarget == null)
        {
            return;
        }

        if (readLegacyPointerInput && orbitEnabled)
        {
            CollectLegacyPointerInput();
        }

        desiredYaw += queuedOrbitDegrees.x;
        desiredPitch = ClampPitch(desiredPitch + queuedOrbitDegrees.y);
        queuedOrbitDegrees = Vector2.zero;

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        // A finger that has claimed the look area (or a held mouse button) owns
        // the camera until it is released, even when it is momentarily still.
        // This prevents movement follow from fighting a deliberate two-thumb
        // camera composition while the player keeps running.
        bool manualPointerHeld = orbitTouchId >= 0 || (mouseDragging && !mouseDragBlocked);
        if (!manualPointerHeld)
        {
            if (manualOrbitGraceRemaining > 0f)
            {
                manualOrbitGraceRemaining = Mathf.Max(0f, manualOrbitGraceRemaining - safeDeltaTime);
                if (manualOrbitGraceRemaining <= 0f)
                {
                    FollowMovementHeading(safeDeltaTime);
                }
            }
            else
            {
                FollowMovementHeading(safeDeltaTime);
            }
        }

        ApplyCameraPose(safeDeltaTime, false);
    }

    /// <summary>Immediately places the camera at its current desired orbit.</summary>
    public void SnapToTarget()
    {
        if (!initialized)
        {
            return;
        }

        currentYaw = desiredYaw;
        currentPitch = desiredPitch;
        smoothedTargetPosition = CalculateRawAnchor(currentYaw);
        targetPositionVelocity = Vector3.zero;
        currentDistance = GetModeDistance();
        distanceVelocity = 0f;
        ApplyCameraPose(0f, true);
    }

    private void LateUpdate()
    {
        if (autoUpdate)
        {
            Tick(Time.unscaledDeltaTime);
        }
    }

    private void CollectLegacyPointerInput()
    {
        CollectMouseInput();
        CollectTouchInput();
    }

    private void CollectMouseInput()
    {
        if (ChameleonInput.GetMouseButtonDown(mouseOrbitButton))
        {
            mouseDragging = true;
            mouseDragBlocked = IsPointerOverUi();
            lastMousePosition = ChameleonInput.MousePosition;
        }

        if (mouseDragging && ChameleonInput.GetMouseButton(mouseOrbitButton))
        {
            Vector3 mousePosition = ChameleonInput.MousePosition;
            Vector2 delta = mousePosition - lastMousePosition;
            lastMousePosition = mousePosition;

            if (!mouseDragBlocked)
            {
                AddOrbitInput(new Vector2(delta.x * mouseSensitivity, -delta.y * mouseSensitivity));
            }
        }

        if (mouseDragging && ChameleonInput.GetMouseButtonUp(mouseOrbitButton))
        {
            mouseDragging = false;
            mouseDragBlocked = false;
        }
    }

    private void CollectTouchInput()
    {
        for (int i = 0; i < ChameleonInput.TouchCount; i++)
        {
            ChameleonTouch touch = ChameleonInput.GetTouch(i);

            if (touch.Phase == TouchPhase.Began)
            {
                bool overUi = ChameleonInput.IsPointerOverUi(touch.Position);
                if (overUi)
                {
                    uiBlockedTouches.Add(touch.FingerId);
                }
                else if (orbitTouchId < 0)
                {
                    orbitTouchId = touch.FingerId;
                }
            }

            if (touch.FingerId == orbitTouchId &&
                (touch.Phase == TouchPhase.Moved || touch.Phase == TouchPhase.Stationary))
            {
                if (!uiBlockedTouches.Contains(touch.FingerId))
                {
                    Vector2 delta = touch.DeltaPosition;
                    AddOrbitInput(new Vector2(delta.x * touchSensitivity, -delta.y * touchSensitivity));
                }
            }

            if (touch.Phase == TouchPhase.Ended || touch.Phase == TouchPhase.Canceled)
            {
                uiBlockedTouches.Remove(touch.FingerId);
                if (touch.FingerId == orbitTouchId)
                {
                    orbitTouchId = -1;
                }
            }
        }
    }

    private void ApplyCameraPose(float deltaTime, bool immediate)
    {
        float smoothDelta = Mathf.Max(0.0001f, deltaTime);
        Vector3 rawAnchor = CalculateRawAnchor(currentYaw);

        if (immediate || deltaTime <= 0f)
        {
            currentYaw = desiredYaw;
            currentPitch = desiredPitch;
            smoothedTargetPosition = rawAnchor;
        }
        else
        {
            currentYaw = Mathf.SmoothDampAngle(currentYaw, desiredYaw, ref yawVelocity,
                orbitSmoothTime, Mathf.Infinity, smoothDelta);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, desiredPitch, ref pitchVelocity,
                orbitSmoothTime, Mathf.Infinity, smoothDelta);
            smoothedTargetPosition = Vector3.SmoothDamp(smoothedTargetPosition, rawAnchor,
                ref targetPositionVelocity, targetSmoothTime, Mathf.Infinity, smoothDelta);
        }

        Quaternion orbit = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 castDirection = -(orbit * Vector3.forward);
        float requestedDistance = GetModeDistance();
        float safeDistance = FindSafeDistance(smoothedTargetPosition, castDirection, requestedDistance);

        if (immediate || safeDistance < currentDistance)
        {
            // Obstructions must pull the camera in immediately; easing inward can cross a wall.
            currentDistance = safeDistance;
            distanceVelocity = 0f;
        }
        else
        {
            currentDistance = Mathf.SmoothDamp(currentDistance, safeDistance, ref distanceVelocity,
                collisionReleaseSmoothTime, Mathf.Infinity, smoothDelta);
        }

        float targetFov = GetModeFieldOfView();
        if (immediate || deltaTime <= 0f)
        {
            drivenCamera.fieldOfView = targetFov;
        }
        else
        {
            drivenCamera.fieldOfView = Mathf.SmoothDamp(drivenCamera.fieldOfView, targetFov,
                ref fieldOfViewVelocity, 0.14f, Mathf.Infinity, smoothDelta);
        }

        Vector3 cameraPosition = smoothedTargetPosition + castDirection * currentDistance;
        Quaternion cameraRotation = Quaternion.LookRotation(smoothedTargetPosition - cameraPosition, Vector3.up);
        drivenCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
    }

    private void FollowMovementHeading(float deltaTime)
    {
        if (!movementFollowActive || !followMovementHeading || focusedMode || attachedMode ||
            !orbitEnabled || deltaTime <= 0f)
        {
            return;
        }

        float yawError = Mathf.DeltaAngle(desiredYaw, movementHeadingYaw);
        float remainingError = Mathf.Abs(yawError) - movementFollowDeadZone;
        if (remainingError <= 0f)
        {
            return;
        }

        float normalizedStrength = Mathf.InverseLerp(movementFollowMinimumStrength, 1f, movementStrength);
        float speedScale = Mathf.Lerp(0.55f, 1f, normalizedStrength);
        float maximumStep = Mathf.Min(remainingError, movementFollowSpeed * speedScale * deltaTime);
        desiredYaw = Mathf.MoveTowardsAngle(desiredYaw, movementHeadingYaw, maximumStep);
    }

    private float FindSafeDistance(Vector3 origin, Vector3 direction, float requestedDistance)
    {
        float radius = Mathf.Max(collisionRadius, CalculateNearPlaneRadius());
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction, collisionHits,
            requestedDistance, collisionMask, QueryTriggerInteraction.Ignore);

        float nearestDistance = requestedDistance;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = collisionHits[i].collider;
            if (hitCollider == null || ShouldIgnoreCollider(hitCollider))
            {
                continue;
            }

            nearestDistance = Mathf.Min(nearestDistance, collisionHits[i].distance - collisionPadding);
        }

        // A wall can be closer than the preferred minimum framing distance (for
        // example when the target is pressed flat against it). Collision safety
        // wins in that case, so allow the camera to pull almost all the way into
        // the target rather than placing it on the far side of the obstruction.
        float absoluteMinimum = Mathf.Min(minimumDistance, 0.05f);
        return Mathf.Clamp(nearestDistance, absoluteMinimum, requestedDistance);
    }

    private bool ShouldIgnoreCollider(Collider candidate)
    {
        Transform candidateTransform = candidate.transform;
        return candidateTransform == followTarget || candidateTransform.IsChildOf(followTarget);
    }

    private float CalculateNearPlaneRadius()
    {
        float halfHeight = Mathf.Tan(drivenCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * drivenCamera.nearClipPlane;
        float halfWidth = halfHeight * drivenCamera.aspect;
        return Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) + 0.025f;
    }

    private float GetModeDistance()
    {
        float requestedDistance = attachedMode
            ? attachedDistance
            : focusedMode
                ? focusedDistance
                : orbitDistance;
        return Mathf.Max(minimumDistance, requestedDistance);
    }

    private Vector3 CalculateRawAnchor(float yaw)
    {
        float activeShoulderOffset = focusedMode
            ? focusedShoulderOffset
            : attachedMode
                ? attachedShoulderOffset
                : shoulderOffset;
        Vector3 shoulder = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * activeShoulderOffset;
        return followTarget.position + targetOffset + shoulder;
    }

    private float GetModeFieldOfView()
    {
        return attachedMode
            ? attachedFieldOfView
            : focusedMode
                ? focusedFieldOfView
                : normalFieldOfView;
    }

    private float ClampPitch(float pitch)
    {
        return Mathf.Clamp(NormalizeSignedAngle(pitch), minimumPitch, maximumPitch);
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }

    private static bool IsPointerOverUi()
    {
        return ChameleonInput.IsPointerOverUi(ChameleonInput.MousePosition);
    }

    private void ResetPointerTracking()
    {
        mouseDragging = false;
        mouseDragBlocked = false;
        orbitTouchId = -1;
        uiBlockedTouches.Clear();
    }

    private void OnDisable()
    {
        ResetPointerTracking();
    }

    private void OnValidate()
    {
        minimumPitch = Mathf.Clamp(minimumPitch, -89f, 88f);
        maximumPitch = Mathf.Clamp(maximumPitch, minimumPitch + 1f, 89f);
        orbitDistance = Mathf.Max(0.5f, orbitDistance);
        focusedDistance = Mathf.Max(0.5f, focusedDistance);
        attachedDistance = Mathf.Max(0.5f, attachedDistance);
        minimumDistance = Mathf.Max(0.05f, minimumDistance);
        collisionRadius = Mathf.Max(0.01f, collisionRadius);
        collisionPadding = Mathf.Max(0f, collisionPadding);
        shoulderOffset = Mathf.Clamp(shoulderOffset, -0.8f, 0.8f);
        focusedShoulderOffset = Mathf.Clamp(focusedShoulderOffset, -0.8f, 0.8f);
        attachedShoulderOffset = Mathf.Clamp(attachedShoulderOffset, -0.8f, 0.8f);
        orbitSmoothTime = Mathf.Max(0.001f, orbitSmoothTime);
        targetSmoothTime = Mathf.Max(0.001f, targetSmoothTime);
        movementFollowDelay = Mathf.Max(0f, movementFollowDelay);
        movementFollowSpeed = Mathf.Max(1f, movementFollowSpeed);
        movementFollowDeadZone = Mathf.Clamp(movementFollowDeadZone, 0f, 45f);
        movementFollowMinimumStrength = Mathf.Clamp01(movementFollowMinimumStrength);
        collisionReleaseSmoothTime = Mathf.Max(0.001f, collisionReleaseSmoothTime);
        normalFieldOfView = Mathf.Clamp(normalFieldOfView, 15f, 100f);
        focusedFieldOfView = Mathf.Clamp(focusedFieldOfView, 15f, 100f);
        attachedFieldOfView = Mathf.Clamp(attachedFieldOfView, 15f, 100f);
    }
}
