using UnityEngine;

[ExecuteAlways]
public class UR3eFixedPose : MonoBehaviour
{
    [Header("Robot Root")]
    public Transform robotRoot;

    [Header("Auto-found joints")]
    public ArticulationBody shoulderPan;   // shoulder_link
    public ArticulationBody shoulderLift;  // upper_arm_link
    public ArticulationBody elbow;         // forearm_link
    public ArticulationBody wrist1;        // wrist_1_link
    public ArticulationBody wrist2;        // wrist_2_link
    public ArticulationBody wrist3;        // wrist_3_link

    [Header("Pose (deg)")]
    public float shoulderPanDeg = 90f;
    public float shoulderLiftDeg = -95f;
    public float elbowDeg = 105f;
    public float wrist1Deg = -100f;
    public float wrist2Deg = -90f;
    public float wrist3Deg = 0f;

    [Header("Drive settings")]
    public float stiffness = 30000f;
    public float damping = 3000f;
    public float forceLimit = 30000f;

    [Header("Behavior")]
    public bool applyInEditMode = true;
    public bool keepPoseEveryFrame = true;

    private void Start()
    {
        EnsureJoints();
        ApplyPose();
    }

    private void Update()
    {
        if (!Application.isPlaying && !applyInEditMode)
            return;

        if (keepPoseEveryFrame)
        {
            EnsureJoints();
            ApplyPose();
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && !applyInEditMode)
            return;

        EnsureJoints();
        ApplyPose();
    }

    [ContextMenu("Auto Assign Joints")]
    public void AutoAssignJoints()
    {
        EnsureJoints(forceRefresh: true);
    }

    [ContextMenu("Apply Pose Now")]
    public void ApplyPoseNow()
    {
        EnsureJoints();
        ApplyPose();
    }

    [ContextMenu("Read Current Pose From Robot")]
    public void ReadCurrentPoseFromRobot()
    {
        EnsureJoints();

        shoulderPanDeg = GetCurrentTargetDeg(shoulderPan);
        shoulderLiftDeg = GetCurrentTargetDeg(shoulderLift);
        elbowDeg = GetCurrentTargetDeg(elbow);
        wrist1Deg = GetCurrentTargetDeg(wrist1);
        wrist2Deg = GetCurrentTargetDeg(wrist2);
        wrist3Deg = GetCurrentTargetDeg(wrist3);
    }

    private void EnsureJoints(bool forceRefresh = false)
    {
        if (robotRoot == null)
            return;

        if (forceRefresh || shoulderPan == null)
            shoulderPan = FindArticulationBodyByName(robotRoot, "shoulder_link");

        if (forceRefresh || shoulderLift == null)
            shoulderLift = FindArticulationBodyByName(robotRoot, "upper_arm_link");

        if (forceRefresh || elbow == null)
            elbow = FindArticulationBodyByName(robotRoot, "forearm_link");

        if (forceRefresh || wrist1 == null)
            wrist1 = FindArticulationBodyByName(robotRoot, "wrist_1_link");

        if (forceRefresh || wrist2 == null)
            wrist2 = FindArticulationBodyByName(robotRoot, "wrist_2_link");

        if (forceRefresh || wrist3 == null)
            wrist3 = FindArticulationBodyByName(robotRoot, "wrist_3_link");
    }

    private void ApplyPose()
    {
        ApplyJoint(shoulderPan, shoulderPanDeg);
        ApplyJoint(shoulderLift, shoulderLiftDeg);
        ApplyJoint(elbow, elbowDeg);
        ApplyJoint(wrist1, wrist1Deg);
        ApplyJoint(wrist2, wrist2Deg);
        ApplyJoint(wrist3, wrist3Deg);
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
        if (joint == null)
            return 0f;

        return joint.xDrive.target;
    }

    private ArticulationBody FindArticulationBodyByName(Transform root, string targetName)
    {
        Transform target = FindDeepChild(root, targetName);
        if (target == null)
            return null;

        return target.GetComponent<ArticulationBody>();
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