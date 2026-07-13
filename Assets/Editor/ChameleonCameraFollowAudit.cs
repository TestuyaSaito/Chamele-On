using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ChameleonCameraFollowAudit
{
    [MenuItem("ChameleON URP/Run Camera Follow Audit")]
    public static void Run()
    {
        GameObject targetObject = null;
        GameObject cameraObject = null;

        try
        {
            targetObject = new GameObject("Camera Follow Audit Target");
            cameraObject = new GameObject("Camera Follow Audit Camera", typeof(Camera), typeof(ThirdPersonCameraRig));
            Camera camera = cameraObject.GetComponent<Camera>();
            ThirdPersonCameraRig rig = cameraObject.GetComponent<ThirdPersonCameraRig>();

            cameraObject.transform.position = new Vector3(0f, 1.4f, -3.5f);
            rig.Initialize(camera, targetObject.transform);
            rig.SetAutoUpdate(false);
            rig.SetLegacyPointerInputEnabled(false);
            rig.SetOrbitAngles(0f, 10f, true);

            // The shipping mobile prototype is manual-camera by default:
            // walking must not rotate the orbit behind the player's back.
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 12, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f &&
                    !rig.IsMovementFollowPending && !rig.IsFollowingMovement,
                "The default mobile camera followed movement instead of staying manual.");

            // Keep the threshold implementation covered as an opt-in helper;
            // no shipping code enables it at startup.
            rig.SetMovementFollowEnabled(true);
            rig.SetOrbitAngles(0f, 10f, true);

            // Small steering corrections must never pull the camera around.
            rig.SetMovementHeading(24f, 1f);
            Step(rig, 15, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f,
                "A sub-threshold steering correction moved the camera.");
            Require(!rig.IsMovementFollowPending && !rig.IsFollowingMovement,
                "A sub-threshold steering correction armed movement follow.");

            // A deliberate large turn must be held for the commit delay. A
            // direction reversal during that delay starts the decision again.
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 2, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f && rig.IsMovementFollowPending,
                "Movement follow did not respect its commit delay.");

            rig.SetMovementHeading(-90f, 1f);
            Step(rig, 2, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f,
                "A reversed pending turn committed before being held long enough.");

            rig.Tick(0.1f);
            Require(rig.Yaw < -1f,
                "A deliberate turn did not begin after its activation angle and commit delay.");

            Step(rig, 20, 0.1f);
            float releaseError = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, -90f));
            Require(releaseError >= 7.8f && releaseError <= 8.2f && !rig.IsFollowingMovement,
                $"Movement follow should settle at its 8 degree release dead zone, but stopped at {releaseError:F1} degrees.");

            // A committed turn must not keep chasing a stale snapshot when the
            // player deliberately selects another heading. The new heading must
            // pass the full commit delay again before yaw resumes.
            rig.SetOrbitAngles(0f, 10f, true);
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 3, 0.1f);
            Require(rig.IsFollowingMovement, "The retarget test never entered committed follow.");
            float yawBeforeLargeRetarget = rig.Yaw;
            rig.SetMovementHeading(150f, 1f);
            Step(rig, 2, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, yawBeforeLargeRetarget)) < 0.1f &&
                    rig.IsMovementFollowPending && !rig.IsFollowingMovement,
                "A 35+ degree committed retarget did not restart the hold-time decision.");
            rig.Tick(0.1f);
            Require(rig.Yaw > yawBeforeLargeRetarget && rig.IsFollowingMovement,
                "The replacement heading did not commit after being held for 0.30 seconds.");

            // Also cancel a stale commit when the latest heading crosses behind
            // the camera, even if it differs from the snapshot by less than 35°.
            rig.SetOrbitAngles(0f, 10f, true);
            rig.SetMovementHeading(45f, 1f);
            Step(rig, 6, 0.1f);
            Require(rig.IsFollowingMovement, "The sign-reversal test never entered committed follow.");
            float yawBeforeSignReversal = rig.Yaw;
            rig.SetMovementHeading(30f, 1f);
            Step(rig, 5, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, yawBeforeSignReversal)) < 0.1f &&
                    !rig.IsMovementFollowPending && !rig.IsFollowingMovement,
                "A committed turn continued after the latest heading reversed its turn direction.");

            // Manual camera input cancels both pending and committed follow.
            rig.SetOrbitAngles(0f, 10f, true);
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 2, 0.1f);
            SetOrbitTouchId(rig, 42);
            rig.AddOrbitInput(new Vector2(-40f, 0f));
            rig.Tick(0.1f);
            float manuallyOrbitedYaw = rig.Yaw;
            Step(rig, 20, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, manuallyOrbitedYaw)) < 0.1f,
                "Automatic follow moved the camera while a look finger was still held.");

            SetOrbitTouchId(rig, -1);
            Step(rig, 5, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, manuallyOrbitedYaw)) < 0.1f,
                "Automatic follow overrode the manual camera grace period.");

            // The 0.6 second manual release grace still wins. The deliberate
            // movement heading must then pass the 0.3 second commit delay again.
            Step(rig, 4, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 90f)) <
                    Mathf.Abs(Mathf.DeltaAngle(manuallyOrbitedYaw, 90f)),
                "Camera did not resume movement follow after release grace plus commit delay.");

            rig.SetOrbitAngles(0f, 10f, true);
            rig.SetFocusedMode(true);
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 12, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f,
                "Paint/focused mode must not auto-rotate the camera.");

            rig.SetFocusedMode(false);
            rig.SetAttachedMode(true);
            rig.SetMovementHeading(90f, 1f);
            Step(rig, 12, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 0f)) < 0.1f,
                "Wall-attached mode must not auto-rotate the camera.");

            Debug.Log("CHAMELEON_CAMERA_FOLLOW_AUDIT_PASS: manual default, optional activation threshold, " +
                "commit delay, committed retarget/reversal, release dead zone, manual override, paint, and attachment checks passed.");
        }
        finally
        {
            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            if (targetObject != null)
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }
    }

    private static void Step(ThirdPersonCameraRig rig, int count, float deltaTime)
    {
        for (int i = 0; i < count; i++)
        {
            rig.Tick(deltaTime);
        }
    }

    private static void SetOrbitTouchId(ThirdPersonCameraRig rig, int touchId)
    {
        FieldInfo field = typeof(ThirdPersonCameraRig).GetField("orbitTouchId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "Camera rig orbit touch state could not be inspected.");
        field.SetValue(rig, touchId);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("CHAMELEON_CAMERA_FOLLOW_AUDIT_FAIL: " + message);
        }
    }
}
