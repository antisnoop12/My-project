using System.Collections;
using UnityEngine;

public class UR3eTrajectoryPlayer : MonoBehaviour
{
    [Header("ROS client")]
    public GridPathRosClient rosClient;

    [Header("Imported UR3e root")]
    public Transform robotRoot;

    [Header("Auto-found joints")]
    public ArticulationBody shoulderPan;   // shoulder_link
    public ArticulationBody shoulderLift;  // upper_arm_link
    public ArticulationBody elbow;         // forearm_link
    public ArticulationBody wrist1;        // wrist_1_link
    public ArticulationBody wrist2;        // wrist_2_link
    public ArticulationBody wrist3;        // wrist_3_link

    [Header("Optional invert flags")]
    public bool invertJ1;
    public bool invertJ2;
    public bool invertJ3;
    public bool invertJ4;
    public bool invertJ5;
    public bool invertJ6;

    [Header("Drive settings")]
    public float stiffness = 10000f;
    public float damping = 1000f;
    public float forceLimit = 10000f;

    [Header("Playback")]
    public float playbackSpeed = 0.4f;
    public float homeMoveDuration = 1.0f;

    private Coroutine playRoutine;
    private float[] homePoseDeg = new float[6];
    private bool homeCaptured = false;

    private void Start()
    {
        if (robotRoot != null && shoulderPan == null)
            AutoAssignJoints();

        CaptureCurrentPoseAsHome();
    }

    [ContextMenu("Auto Assign Joints")]
    public void AutoAssignJoints()
    {
        if (robotRoot == null)
        {
            Debug.LogError("robotRoot is not assigned.");
            return;
        }

        shoulderPan = FindArticulationBodyByName(robotRoot, "shoulder_link");
        shoulderLift = FindArticulationBodyByName(robotRoot, "upper_arm_link");
        elbow = FindArticulationBodyByName(robotRoot, "forearm_link");
        wrist1 = FindArticulationBodyByName(robotRoot, "wrist_1_link");
        wrist2 = FindArticulationBodyByName(robotRoot, "wrist_2_link");
        wrist3 = FindArticulationBodyByName(robotRoot, "wrist_3_link");

        Debug.Log(
            "TrajectoryPlayer AutoAssignJoints finished.\n" +
            $"shoulderPan: {(shoulderPan ? shoulderPan.name : "null")}\n" +
            $"shoulderLift: {(shoulderLift ? shoulderLift.name : "null")}\n" +
            $"elbow: {(elbow ? elbow.name : "null")}\n" +
            $"wrist1: {(wrist1 ? wrist1.name : "null")}\n" +
            $"wrist2: {(wrist2 ? wrist2.name : "null")}\n" +
            $"wrist3: {(wrist3 ? wrist3.name : "null")}"
        );
    }

    [ContextMenu("Capture Current Pose As Home")]
    public void CaptureCurrentPoseAsHome()
    {
        homePoseDeg[0] = GetCurrentTargetDeg(shoulderPan);
        homePoseDeg[1] = GetCurrentTargetDeg(shoulderLift);
        homePoseDeg[2] = GetCurrentTargetDeg(elbow);
        homePoseDeg[3] = GetCurrentTargetDeg(wrist1);
        homePoseDeg[4] = GetCurrentTargetDeg(wrist2);
        homePoseDeg[5] = GetCurrentTargetDeg(wrist3);

        homeCaptured = true;

        Debug.Log(
            $"Home captured: [{homePoseDeg[0]}, {homePoseDeg[1]}, {homePoseDeg[2]}, {homePoseDeg[3]}, {homePoseDeg[4]}, {homePoseDeg[5]}]"
        );
    }

    [ContextMenu("Move To Home")]
    public void MoveToHome()
    {
        Debug.Log("MoveToHome called");

        if (!homeCaptured)
        {
            Debug.LogError("Home pose not captured yet.");
            return;
        }

        if (playRoutine != null)
            StopCoroutine(playRoutine);

        playRoutine = StartCoroutine(MoveToPoseCoroutine(
            homePoseDeg[0], homePoseDeg[1], homePoseDeg[2],
            homePoseDeg[3], homePoseDeg[4], homePoseDeg[5],
            homeMoveDuration
        ));
    }

    [ContextMenu("Play Last Trajectory")]
    public void PlayLastTrajectory()
    {
        if (rosClient == null)
        {
            Debug.LogError("rosClient is not assigned.");
            return;
        }

        if (rosClient.LastResult == null)
        {
            Debug.LogError("No ROS result received yet.");
            return;
        }

        if (!rosClient.LastResult.ok)
        {
            Debug.LogError("Last ROS result is not ok.");
            return;
        }

        if (rosClient.LastResult.points == null || rosClient.LastResult.points.Length < 2)
        {
            Debug.LogError("Trajectory must contain at least 2 points.");
            return;
        }

        if (playRoutine != null)
            StopCoroutine(playRoutine);

        playRoutine = StartCoroutine(PlayTrajectoryCoroutine(rosClient.LastResult));
    }

    [ContextMenu("Stop Playback")]
    public void StopPlayback()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
    }

    public string[] GetRosJointNames()
    {
        return new string[]
        {
            "shoulder_pan_joint",
            "shoulder_lift_joint",
            "elbow_joint",
            "wrist_1_joint",
            "wrist_2_joint",
            "wrist_3_joint"
        };
    }

    private float GetCurrentJointPositionRad(ArticulationBody joint)
    {
        if (joint == null || joint.dofCount < 1)
            return 0f;

        return joint.jointPosition[0];
    }

    public float[] GetCurrentJointPositionsRadForRos()
    {
        float j1 = invertJ1 ? -GetCurrentJointPositionRad(shoulderPan) : GetCurrentJointPositionRad(shoulderPan);
        float j2 = invertJ2 ? -GetCurrentJointPositionRad(shoulderLift) : GetCurrentJointPositionRad(shoulderLift);
        float j3 = invertJ3 ? -GetCurrentJointPositionRad(elbow) : GetCurrentJointPositionRad(elbow);
        float j4 = invertJ4 ? -GetCurrentJointPositionRad(wrist1) : GetCurrentJointPositionRad(wrist1);
        float j5 = invertJ5 ? -GetCurrentJointPositionRad(wrist2) : GetCurrentJointPositionRad(wrist2);
        float j6 = invertJ6 ? -GetCurrentJointPositionRad(wrist3) : GetCurrentJointPositionRad(wrist3);

        Debug.Log(
            $"ROS start joints rad = [{j1}, {j2}, {j3}, {j4}, {j5}, {j6}]"
        );

        return new float[]
        {
            j1, j2, j3, j4, j5, j6
        };
    }

    private IEnumerator MoveToPoseCoroutine(float j1, float j2, float j3, float j4, float j5, float j6, float duration)
    {
        float from1 = GetCurrentTargetDeg(shoulderPan);
        float from2 = GetCurrentTargetDeg(shoulderLift);
        float from3 = GetCurrentTargetDeg(elbow);
        float from4 = GetCurrentTargetDeg(wrist1);
        float from5 = GetCurrentTargetDeg(wrist2);
        float from6 = GetCurrentTargetDeg(wrist3);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);

            ApplyJointTargets(
                Mathf.Lerp(from1, j1, t),
                Mathf.Lerp(from2, j2, t),
                Mathf.Lerp(from3, j3, t),
                Mathf.Lerp(from4, j4, t),
                Mathf.Lerp(from5, j5, t),
                Mathf.Lerp(from6, j6, t)
            );

            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyJointTargets(j1, j2, j3, j4, j5, j6);
        playRoutine = null;
    }

    private IEnumerator PlayTrajectoryCoroutine(GridPathResult result)
    {
        var points = result.points;

        ApplyJointTargetsFromPoint(points[0]);

        for (int i = 0; i < points.Length - 1; i++)
        {
            GridPathPointResult from = points[i];
            GridPathPointResult to = points[i + 1];

            if (!IsValidPoint(from) || !IsValidPoint(to))
            {
                Debug.LogError($"Invalid trajectory point at index {i} or {i + 1}");
                yield break;
            }

            float segmentDuration = (to.time_from_start - from.time_from_start) / Mathf.Max(0.0001f, playbackSpeed);

            if (segmentDuration <= 0f)
            {
                ApplyJointTargetsFromPoint(to);
                yield return null;
                continue;
            }

            float elapsed = 0f;

            while (elapsed < segmentDuration)
            {
                float t = elapsed / segmentDuration;

                float j1 = Mathf.Lerp(from.positions_deg[0], to.positions_deg[0], t);
                float j2 = Mathf.Lerp(from.positions_deg[1], to.positions_deg[1], t);
                float j3 = Mathf.Lerp(from.positions_deg[2], to.positions_deg[2], t);
                float j4 = Mathf.Lerp(from.positions_deg[3], to.positions_deg[3], t);
                float j5 = Mathf.Lerp(from.positions_deg[4], to.positions_deg[4], t);
                float j6 = Mathf.Lerp(from.positions_deg[5], to.positions_deg[5], t);

                ApplyJointTargets(
                    invertJ1 ? -j1 : j1,
                    invertJ2 ? -j2 : j2,
                    invertJ3 ? -j3 : j3,
                    invertJ4 ? -j4 : j4,
                    invertJ5 ? -j5 : j5,
                    invertJ6 ? -j6 : j6
                );

                elapsed += Time.deltaTime;
                yield return null;
            }

            ApplyJointTargetsFromPoint(to);
            yield return null;
        }

        playRoutine = null;
    }

    private bool IsValidPoint(GridPathPointResult pt)
    {
        return pt != null && pt.positions_deg != null && pt.positions_deg.Length >= 6;
    }

    private void ApplyJointTargetsFromPoint(GridPathPointResult pt)
    {
        ApplyJointTargets(
            invertJ1 ? -pt.positions_deg[0] : pt.positions_deg[0],
            invertJ2 ? -pt.positions_deg[1] : pt.positions_deg[1],
            invertJ3 ? -pt.positions_deg[2] : pt.positions_deg[2],
            invertJ4 ? -pt.positions_deg[3] : pt.positions_deg[3],
            invertJ5 ? -pt.positions_deg[4] : pt.positions_deg[4],
            invertJ6 ? -pt.positions_deg[5] : pt.positions_deg[5]
        );
    }

    private void ApplyJointTargets(float j1, float j2, float j3, float j4, float j5, float j6)
    {
        ApplyJoint(shoulderPan, j1);
        ApplyJoint(shoulderLift, j2);
        ApplyJoint(elbow, j3);
        ApplyJoint(wrist1, j4);
        ApplyJoint(wrist2, j5);
        ApplyJoint(wrist3, j6);
    }

    private void ApplyJoint(ArticulationBody joint, float targetDeg)
    {
        if (joint == null)
            return;

        var drive = joint.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetDeg;
        joint.xDrive = drive;
    }

    private float GetCurrentTargetDeg(ArticulationBody joint)
    {
        if (joint == null) return 0f;
        return joint.xDrive.target;
    }

    private ArticulationBody FindArticulationBodyByName(Transform root, string targetName)
    {
        Transform target = FindDeepChild(root, targetName);
        if (target == null)
        {
            Debug.LogWarning($"Could not find transform: {targetName}");
            return null;
        }

        ArticulationBody ab = target.GetComponent<ArticulationBody>();
        if (ab == null)
        {
            Debug.LogWarning($"Found {targetName}, but it has no ArticulationBody.");
            return null;
        }

        return ab;
    }

    private Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent.name == targetName)
            return parent;

        foreach (Transform child in parent)
        {
            Transform result = FindDeepChild(child, targetName);
            if (result != null)
                return result;
        }

        return null;
    }
}