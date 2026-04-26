using UnityEngine;

public class UR3eJointTester : MonoBehaviour
{
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

    [Header("Target angles in degrees")]
    public float j1;
    public float j2;
    public float j3;
    public float j4;
    public float j5;
    public float j6;

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
            "AutoAssignJoints finished.\n" +
            $"shoulderPan: {(shoulderPan ? shoulderPan.name : "null")}\n" +
            $"shoulderLift: {(shoulderLift ? shoulderLift.name : "null")}\n" +
            $"elbow: {(elbow ? elbow.name : "null")}\n" +
            $"wrist1: {(wrist1 ? wrist1.name : "null")}\n" +
            $"wrist2: {(wrist2 ? wrist2.name : "null")}\n" +
            $"wrist3: {(wrist3 ? wrist3.name : "null")}"
        );
    }

    [ContextMenu("Apply Joint Targets")]
    public void ApplyJointTargets()
    {
        ApplyJoint(shoulderPan, invertJ1 ? -j1 : j1, "J1");
        ApplyJoint(shoulderLift, invertJ2 ? -j2 : j2, "J2");
        ApplyJoint(elbow, invertJ3 ? -j3 : j3, "J3");
        ApplyJoint(wrist1, invertJ4 ? -j4 : j4, "J4");
        ApplyJoint(wrist2, invertJ5 ? -j5 : j5, "J5");
        ApplyJoint(wrist3, invertJ6 ? -j6 : j6, "J6");
    }

    [ContextMenu("Print Joint Info")]
    public void PrintJointInfo()
    {
        PrintJoint("shoulderPan", shoulderPan);
        PrintJoint("shoulderLift", shoulderLift);
        PrintJoint("elbow", elbow);
        PrintJoint("wrist1", wrist1);
        PrintJoint("wrist2", wrist2);
        PrintJoint("wrist3", wrist3);
    }

    private void ApplyJoint(ArticulationBody joint, float targetDeg, string label)
    {
        if (joint == null)
        {
            Debug.LogWarning($"{label}: joint is null");
            return;
        }

        var drive = joint.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetDeg;
        joint.xDrive = drive;

        Debug.Log($"{label}: applied target {targetDeg} deg to {joint.name}");
    }

    private void PrintJoint(string label, ArticulationBody joint)
    {
        if (joint == null)
        {
            Debug.LogWarning($"{label}: null");
            return;
        }

        var drive = joint.xDrive;
        Debug.Log(
            $"{label} => name={joint.name}, " +
            $"jointType={joint.jointType}, " +
            $"stiffness={drive.stiffness}, damping={drive.damping}, forceLimit={drive.forceLimit}, " +
            $"target={drive.target}, lowerLimit={drive.lowerLimit}, upperLimit={drive.upperLimit}"
        );
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