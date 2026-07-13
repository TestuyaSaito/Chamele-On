using System;
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

            rig.SetMovementHeading(90f, 1f);
            Step(rig, 10, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 90f)) <= 4f,
                $"Movement follow stopped at {rig.Yaw:F1} degrees instead of settling behind 90 degrees.");

            rig.AddOrbitInput(new Vector2(-40f, 0f));
            rig.Tick(0.1f);
            float manuallyOrbitedYaw = rig.Yaw;
            Step(rig, 5, 0.1f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, manuallyOrbitedYaw)) < 0.1f,
                "Automatic follow overrode the manual camera grace period.");

            rig.Tick(0.2f);
            Require(Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 90f)) <
                    Mathf.Abs(Mathf.DeltaAngle(manuallyOrbitedYaw, 90f)),
                "Camera did not resume movement follow after the manual grace period.");

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

            Debug.Log("CHAMELEON_CAMERA_FOLLOW_AUDIT_PASS: movement heading, manual override, paint, and attachment checks passed.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("CHAMELEON_CAMERA_FOLLOW_AUDIT_FAIL: " + message);
        }
    }
}
