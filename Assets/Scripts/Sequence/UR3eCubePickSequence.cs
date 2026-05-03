using System.Collections;
using UnityEngine;

public class UR3eCubePickSequence : MonoBehaviour
{
    public enum UnityCoordinatePlane
    {
        XZ,
        XY
    }

    [Header("References")]
    public UR3ePointInputUI pointInputUI;
    public GridPathRosClient rosClient;
    public UR3eTrajectoryPlayer player;
    public HingedGripper gripper;

    [Header("Target / Goal")]
    public Transform targetCube;
    public Transform goalObject;

    [Header("Unity Coordinate Plane")]
    public UnityCoordinatePlane coordinatePlane = UnityCoordinatePlane.XZ;

    [Header("ROS Grid Z")]
    public int approachZ = 200;
    public int pickZ = 50;
    public int liftZ = 200;
    public int goalZ = 300;

    [Header("Joint Pose After Pick + Lift")]
    public float jointMoveDuration = 1.5f;

    public float targetShoulderPanDeg = 0f;
    public float targetShoulderLiftDeg = -95f;
    public float targetElbowDeg = 135f;
    public float targetWrist1Deg = -40f;
    public float targetWrist2Deg = 0f;
    public float targetWrist3Deg = 90f;

    [Header("Lower Until Contact")]
    public bool lowerUntilContact = true;
    public int minLowerZ = 0;
    public float contactThreshold = 0.005f;
    public bool releaseWhenContact = false;

    [Header("Start Point")]
    public bool useRosClientFirstPointAsStart = true;
    public GridPathRosClient.Int3 fallbackStartGridPoint =
        new GridPathRosClient.Int3(0, 0, 200);

    [Header("Timing")]
    public float waitTimeout = 10f;
    public float segmentPause = 0.2f;
    public float gripperActionPause = 0.25f;

    [Header("Segment Validation")]
    [Range(0f, 1f)]
    public float minAcceptableFraction = 0.95f;

    [Header("Request Option")]
    public bool requireFullFraction = false;

    private Coroutine sequenceRoutine;

    private string[] chainedJointNames;
    private float[] chainedJointPositionsRad;
    private bool lastSegmentSucceeded;

    private GridPathRosClient.Int3 lastGripPoint;
    private bool hasGripPoint = false;

    private bool placeContactDetected;
    private bool placeFailed;

    // =========================================================
    // 버튼 함수 1
    // 기존 기능:
    // Pick -> Lift -> Goal 이동 -> 접촉할 때까지 하강
    // =========================================================
    [ContextMenu("Run Pick Lift Move Goal And Lower Sequence")]
    public void RunPickLiftAndMoveToGoalSequence()
    {
        if (targetCube == null)
        {
            Debug.LogError("targetCube is not assigned.");
            return;
        }

        if (goalObject == null)
        {
            Debug.LogError("goalObject is not assigned.");
            return;
        }

        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);

        sequenceRoutine = StartCoroutine(
            PickLiftMoveGoalAndLowerCoroutine(targetCube, goalObject)
        );
    }

    // =========================================================
    // 버튼 함수 2
    // 새 기능:
    // Pick -> Lift -> 지정한 Joint Axis 각도로 이동
    // 버튼 OnClick에는 이 함수를 넣으면 됨
    // =========================================================
    [ContextMenu("Run Pick Lift And Move To Joint Pose Sequence")]
    public void RunPickLiftAndMoveToJointPoseSequence()
    {
        if (targetCube == null)
        {
            Debug.LogError("targetCube is not assigned.");
            return;
        }

        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);

        sequenceRoutine = StartCoroutine(
            PickLiftAndMoveToJointPoseCoroutine(targetCube)
        );
    }

    public void StopSequence()
    {
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        if (player != null)
            player.StopPlayback();
    }

    private IEnumerator PickLiftAndMoveToJointPoseCoroutine(Transform cube)
    {
        if (!ValidateReferences())
        {
            sequenceRoutine = null;
            yield break;
        }

        chainedJointNames = null;
        chainedJointPositionsRad = null;

        hasGripPoint = false;
        lastGripPoint = null;

        Vector2 targetUnity2D = GetUnity2DPosition(cube);

        GridPathRosClient.Int3 approachPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, approachZ);

        GridPathRosClient.Int3 pickPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, pickZ);

        GridPathRosClient.Int3 liftPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, liftZ);

        if (approachPoint == null ||
            pickPoint == null ||
            liftPoint == null)
        {
            Debug.LogError("Failed to convert target position to ROS grid point.");
            sequenceRoutine = null;
            yield break;
        }

        GridPathRosClient.Int3 startPoint = GetStartGridPoint();

        Debug.Log(
            $"Pick Lift JointPose Sequence Start\n" +
            $"Target Unity2D: {targetUnity2D}\n" +
            $"Start: [{startPoint.x}, {startPoint.y}, {startPoint.z}]\n" +
            $"Approach: [{approachPoint.x}, {approachPoint.y}, {approachPoint.z}]\n" +
            $"Pick: [{pickPoint.x}, {pickPoint.y}, {pickPoint.z}]\n" +
            $"Lift: [{liftPoint.x}, {liftPoint.y}, {liftPoint.z}]"
        );

        // =========================================================
        // PICK START
        // targetCube의 Unity 월드 좌표를 ROS Grid 좌표로 변환
        // z=approachZ 위치로 접근
        // 같은 x, y에서 z=pickZ 위치로 하강
        // 그리퍼를 닫아서 targetCube를 잡음
        // =========================================================

        if (!IsSamePoint(startPoint, approachPoint))
        {
            yield return RequestAndPlaySegmentCoroutine(startPoint, approachPoint, 0);

            if (!lastSegmentSucceeded)
            {
                Debug.LogError("Pick failed at approach movement.");
                sequenceRoutine = null;
                yield break;
            }
        }

        yield return RequestAndPlaySegmentCoroutine(approachPoint, pickPoint, 1);

        if (!lastSegmentSucceeded)
        {
            Debug.LogError("Pick failed at downward movement.");
            sequenceRoutine = null;
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            pickPoint.x,
            pickPoint.y,
            pickPoint.z
        );

        hasGripPoint = true;

        gripper.Grip();
        yield return new WaitForSeconds(gripperActionPause);

        Debug.Log("Pick completed.");

        // =========================================================
        // PICK END
        // =========================================================


        // =========================================================
        // LIFT START
        // grip한 위치에서 x, y는 유지하고 z만 liftZ까지 올림
        // =========================================================

        yield return RequestAndPlaySegmentCoroutine(lastGripPoint, liftPoint, 2);

        if (!lastSegmentSucceeded)
        {
            Debug.LogError("Lift failed.");
            sequenceRoutine = null;
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            liftPoint.x,
            liftPoint.y,
            liftPoint.z
        );

        Debug.Log("Lift completed.");

        // =========================================================
        // LIFT END
        // =========================================================


        // =========================================================
        // JOINT POSE MOVE START
        // Lift 이후 ROS Grid 좌표 이동이 아니라
        // UR3eTrajectoryPlayer에 연결된 각 ArticulationBody의 xDrive.target을
        // 아래 목표 각도로 직접 보간 이동함
        //
        // Shoulder Pan Deg  = 0
        // Shoulder Lift Deg = -95
        // Elbow Deg         = 135
        // Wrist 1 Deg       = -40
        // Wrist 2 Deg       = 0
        // Wrist 3 Deg       = 90
        // =========================================================

        if (!ValidateJointReferences())
        {
            sequenceRoutine = null;
            yield break;
        }

        yield return MoveToJointPoseDirectCoroutine(
            targetShoulderPanDeg,
            targetShoulderLiftDeg,
            targetElbowDeg,
            targetWrist1Deg,
            targetWrist2Deg,
            targetWrist3Deg,
            jointMoveDuration
        );

        Debug.Log("Joint pose move completed.");

        // =========================================================
        // JOINT POSE MOVE END
        // =========================================================

        Debug.Log("Pick Lift And Move To Joint Pose Sequence Completed.");
        sequenceRoutine = null;
    }

    private IEnumerator PickLiftMoveGoalAndLowerCoroutine(Transform cube, Transform goal)
    {
        if (!ValidateReferences())
        {
            sequenceRoutine = null;
            yield break;
        }

        chainedJointNames = null;
        chainedJointPositionsRad = null;

        hasGripPoint = false;
        lastGripPoint = null;

        placeContactDetected = false;
        placeFailed = false;

        Vector2 targetUnity2D = GetUnity2DPosition(cube);

        GridPathRosClient.Int3 approachPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, approachZ);

        GridPathRosClient.Int3 pickPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, pickZ);

        GridPathRosClient.Int3 liftPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, liftZ);

        if (approachPoint == null ||
            pickPoint == null ||
            liftPoint == null)
        {
            Debug.LogError("Failed to convert target position to ROS grid point.");
            sequenceRoutine = null;
            yield break;
        }

        GridPathRosClient.Int3 startPoint = GetStartGridPoint();

        Debug.Log(
            $"Sequence Start\n" +
            $"Target Unity2D: {targetUnity2D}\n" +
            $"Start: [{startPoint.x}, {startPoint.y}, {startPoint.z}]\n" +
            $"Approach: [{approachPoint.x}, {approachPoint.y}, {approachPoint.z}]\n" +
            $"Pick: [{pickPoint.x}, {pickPoint.y}, {pickPoint.z}]\n" +
            $"Lift: [{liftPoint.x}, {liftPoint.y}, {liftPoint.z}]"
        );

        // =========================================================
        // PICK START
        // =========================================================

        if (!IsSamePoint(startPoint, approachPoint))
        {
            yield return RequestAndPlaySegmentCoroutine(startPoint, approachPoint, 0);

            if (!lastSegmentSucceeded)
            {
                Debug.LogError("Pick failed at approach movement.");
                sequenceRoutine = null;
                yield break;
            }
        }

        yield return RequestAndPlaySegmentCoroutine(approachPoint, pickPoint, 1);

        if (!lastSegmentSucceeded)
        {
            Debug.LogError("Pick failed at downward movement.");
            sequenceRoutine = null;
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            pickPoint.x,
            pickPoint.y,
            pickPoint.z
        );

        hasGripPoint = true;

        gripper.Grip();
        yield return new WaitForSeconds(gripperActionPause);

        Debug.Log("Pick completed.");

        // =========================================================
        // PICK END
        // =========================================================


        // =========================================================
        // LIFT START
        // =========================================================

        yield return RequestAndPlaySegmentCoroutine(lastGripPoint, liftPoint, 2);

        if (!lastSegmentSucceeded)
        {
            Debug.LogError("Lift failed.");
            sequenceRoutine = null;
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            liftPoint.x,
            liftPoint.y,
            liftPoint.z
        );

        Debug.Log("Lift completed.");

        // =========================================================
        // LIFT END
        // =========================================================


        // =========================================================
        // MOVE TO GOAL START
        // goalObject가 부모 객체를 따라 움직일 수 있으므로,
        // lift가 끝난 직후의 goalObject.position 월드 좌표를 다시 읽음
        // =========================================================

        Vector2 currentGoalUnity2D = GetUnity2DPosition(goal);

        GridPathRosClient.Int3 goalPoint =
            pointInputUI.ConvertUnity2Ros(currentGoalUnity2D, goalZ);

        if (goalPoint == null)
        {
            Debug.LogError("Failed to convert goal position to ROS grid point.");
            sequenceRoutine = null;
            yield break;
        }

        Debug.Log(
            $"Current Goal Unity2D: {currentGoalUnity2D}\n" +
            $"Goal Grid: [{goalPoint.x}, {goalPoint.y}, {goalPoint.z}]"
        );

        yield return RequestAndPlaySegmentCoroutine(lastGripPoint, goalPoint, 3);

        if (!lastSegmentSucceeded)
        {
            Debug.LogError("Move to goal failed.");
            sequenceRoutine = null;
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            goalPoint.x,
            goalPoint.y,
            goalPoint.z
        );

        Debug.Log("Move to goal completed.");

        // =========================================================
        // MOVE TO GOAL END
        // =========================================================


        // =========================================================
        // LOWER UNTIL CONTACT START
        // goal 위치에서 z만 minLowerZ까지 한 번에 내려가는 trajectory를 요청
        // 이동 중 매 프레임 targetCube와 goalObject Collider 접촉 확인
        // 접촉하면 즉시 player.StopPlayback()
        // =========================================================

        if (lowerUntilContact)
        {
            yield return LowerUntilTargetTouchesGoalCoroutine(4);

            if (placeFailed)
            {
                Debug.LogError("Lower until contact failed.");
                sequenceRoutine = null;
                yield break;
            }
        }

        // =========================================================
        // LOWER UNTIL CONTACT END
        // =========================================================

        Debug.Log("Pick Lift Move Goal And Lower Sequence Completed.");
        sequenceRoutine = null;
    }

    private IEnumerator LowerUntilTargetTouchesGoalCoroutine(int segmentIndex)
    {
        placeContactDetected = false;
        placeFailed = false;

        if (targetCube == null || goalObject == null)
        {
            Debug.LogError("targetCube or goalObject is missing.");
            placeFailed = true;
            yield break;
        }

        if (!HasCollider(targetCube))
        {
            Debug.LogError("targetCube has no Collider. Add Collider to targetCube or its child.");
            placeFailed = true;
            yield break;
        }

        if (!HasCollider(goalObject))
        {
            Debug.LogError("goalObject has no Collider. Add Collider to goalObject or its child.");
            placeFailed = true;
            yield break;
        }

        if (AreObjectsTouching(targetCube, goalObject))
        {
            Debug.Log("Target and goal are already touching. Lower skipped.");
            placeContactDetected = true;

            if (releaseWhenContact)
            {
                gripper.Release();
                yield return new WaitForSeconds(gripperActionPause);
            }

            yield break;
        }

        GridPathRosClient.Int3 startPoint = new GridPathRosClient.Int3(
            lastGripPoint.x,
            lastGripPoint.y,
            lastGripPoint.z
        );

        GridPathRosClient.Int3 endPoint = new GridPathRosClient.Int3(
            lastGripPoint.x,
            lastGripPoint.y,
            minLowerZ
        );

        Debug.Log(
            $"Continuous lowering until contact: " +
            $"[{startPoint.x}, {startPoint.y}, {startPoint.z}] -> " +
            $"[{endPoint.x}, {endPoint.y}, {endPoint.z}]"
        );

        lastSegmentSucceeded = false;

        GridPathRosClient.Int3[] segment = new GridPathRosClient.Int3[]
        {
            new GridPathRosClient.Int3(startPoint.x, startPoint.y, startPoint.z),
            new GridPathRosClient.Int3(endPoint.x, endPoint.y, endPoint.z)
        };

        int startVersion = rosClient.ResultVersion;

        rosClient.SendRequestWithGridPoints(
            segment,
            requireFullFraction,
            chainedJointNames,
            chainedJointPositionsRad
        );

        float elapsedWait = 0f;
        bool received = false;

        while (elapsedWait < waitTimeout)
        {
            if (rosClient.ResultVersion > startVersion)
            {
                received = true;
                break;
            }

            elapsedWait += Time.deltaTime;
            yield return null;
        }

        if (!received)
        {
            Debug.LogError("Timed out waiting for ROS result on continuous lower segment.");
            placeFailed = true;
            yield break;
        }

        GridPathResult result = rosClient.LastResult;

        if (result == null || !result.ok)
        {
            Debug.LogError("ROS returned invalid result on continuous lower segment.");
            placeFailed = true;
            yield break;
        }

        if (result.points == null || result.points.Length < 2)
        {
            Debug.LogError(
                $"Continuous lower segment has no usable trajectory. " +
                $"fraction={result.fraction}, " +
                $"pointCount={(result.points == null ? 0 : result.points.Length)}"
            );

            placeFailed = true;
            yield break;
        }

        if (result.fraction < minAcceptableFraction)
        {
            Debug.LogError(
                $"Continuous lower segment planned only partially. " +
                $"fraction={result.fraction}, required>={minAcceptableFraction}"
            );

            placeFailed = true;
            yield break;
        }

        float segmentDuration =
            result.points[result.points.Length - 1].time_from_start /
            Mathf.Max(0.0001f, player.playbackSpeed);

        player.PlayLastTrajectory();

        float elapsedMove = 0f;

        while (elapsedMove < segmentDuration)
        {
            if (AreObjectsTouching(targetCube, goalObject))
            {
                player.StopPlayback();

                float t = Mathf.Clamp01(
                    elapsedMove / Mathf.Max(0.0001f, segmentDuration)
                );

                int estimatedZ = Mathf.RoundToInt(
                    Mathf.Lerp(startPoint.z, endPoint.z, t)
                );

                lastGripPoint = new GridPathRosClient.Int3(
                    startPoint.x,
                    startPoint.y,
                    estimatedZ
                );

                placeContactDetected = true;

                Debug.Log(
                    $"Contact detected during continuous lower. " +
                    $"Playback stopped. Estimated Grid Z = {estimatedZ}"
                );

                if (releaseWhenContact)
                {
                    gripper.Release();
                    yield return new WaitForSeconds(gripperActionPause);
                }

                yield break;
            }

            elapsedMove += Time.deltaTime;
            yield return null;
        }

        if (AreObjectsTouching(targetCube, goalObject))
        {
            placeContactDetected = true;

            lastGripPoint = new GridPathRosClient.Int3(
                endPoint.x,
                endPoint.y,
                endPoint.z
            );

            Debug.Log("Contact detected at the end of continuous lower.");

            if (releaseWhenContact)
            {
                gripper.Release();
                yield return new WaitForSeconds(gripperActionPause);
            }

            yield break;
        }

        Debug.LogWarning(
            $"Reached minLowerZ = {minLowerZ}, but target and goal did not touch."
        );

        placeFailed = true;
    }

    private IEnumerator RequestAndPlaySegmentCoroutine(
        GridPathRosClient.Int3 from,
        GridPathRosClient.Int3 to,
        int segmentIndex)
    {
        lastSegmentSucceeded = false;

        GridPathRosClient.Int3[] segment = new GridPathRosClient.Int3[]
        {
            new GridPathRosClient.Int3(from.x, from.y, from.z),
            new GridPathRosClient.Int3(to.x, to.y, to.z)
        };

        Debug.Log(
            $"Requesting segment {segmentIndex}: " +
            $"[{from.x}, {from.y}, {from.z}] -> " +
            $"[{to.x}, {to.y}, {to.z}]"
        );

        int startVersion = rosClient.ResultVersion;

        rosClient.SendRequestWithGridPoints(
            segment,
            requireFullFraction,
            chainedJointNames,
            chainedJointPositionsRad
        );

        float elapsed = 0f;
        bool received = false;

        while (elapsed < waitTimeout)
        {
            if (rosClient.ResultVersion > startVersion)
            {
                received = true;
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!received)
        {
            Debug.LogError($"Timed out waiting for ROS result on segment {segmentIndex}.");
            yield break;
        }

        GridPathResult result = rosClient.LastResult;

        if (result == null || !result.ok)
        {
            Debug.LogError($"ROS returned invalid result on segment {segmentIndex}.");
            yield break;
        }

        if (result.points == null || result.points.Length < 2)
        {
            Debug.LogError(
                $"Segment {segmentIndex} has no usable trajectory. " +
                $"fraction={result.fraction}, " +
                $"pointCount={(result.points == null ? 0 : result.points.Length)}"
            );
            yield break;
        }

        if (result.fraction < minAcceptableFraction)
        {
            Debug.LogError(
                $"Segment {segmentIndex} planned only partially. " +
                $"fraction={result.fraction}, required>={minAcceptableFraction}"
            );
            yield break;
        }

        float segmentDuration =
            result.points[result.points.Length - 1].time_from_start /
            Mathf.Max(0.0001f, player.playbackSpeed);

        player.PlayLastTrajectory();

        if (segmentDuration > 0f)
            yield return new WaitForSeconds(segmentDuration + segmentPause);
        else
            yield return new WaitForSeconds(segmentPause);

        if (result.joint_names == null ||
            result.points[result.points.Length - 1].positions_rad == null ||
            result.joint_names.Length == 0 ||
            result.points[result.points.Length - 1].positions_rad.Length == 0)
        {
            Debug.LogError($"Segment {segmentIndex} has no valid chaining joint state.");
            yield break;
        }

        chainedJointNames = (string[])result.joint_names.Clone();
        chainedJointPositionsRad =
            (float[])result.points[result.points.Length - 1].positions_rad.Clone();

        lastSegmentSucceeded = true;
    }

    private IEnumerator MoveToJointPoseDirectCoroutine(
        float shoulderPanDeg,
        float shoulderLiftDeg,
        float elbowDeg,
        float wrist1Deg,
        float wrist2Deg,
        float wrist3Deg,
        float duration)
    {
        player.StopPlayback();

        float fromShoulderPan = GetJointTargetDeg(player.shoulderPan);
        float fromShoulderLift = GetJointTargetDeg(player.shoulderLift);
        float fromElbow = GetJointTargetDeg(player.elbow);
        float fromWrist1 = GetJointTargetDeg(player.wrist1);
        float fromWrist2 = GetJointTargetDeg(player.wrist2);
        float fromWrist3 = GetJointTargetDeg(player.wrist3);

        float safeDuration = Mathf.Max(0.0001f, duration);
        float elapsed = 0f;

        Debug.Log(
            $"MoveToJointPoseDirect Start\n" +
            $"Shoulder Pan: {fromShoulderPan} -> {shoulderPanDeg}\n" +
            $"Shoulder Lift: {fromShoulderLift} -> {shoulderLiftDeg}\n" +
            $"Elbow: {fromElbow} -> {elbowDeg}\n" +
            $"Wrist1: {fromWrist1} -> {wrist1Deg}\n" +
            $"Wrist2: {fromWrist2} -> {wrist2Deg}\n" +
            $"Wrist3: {fromWrist3} -> {wrist3Deg}"
        );

        while (elapsed < safeDuration)
        {
            float t = Mathf.Clamp01(elapsed / safeDuration);

            ApplyJointTargetDeg(player.shoulderPan, Mathf.Lerp(fromShoulderPan, shoulderPanDeg, t));
            ApplyJointTargetDeg(player.shoulderLift, Mathf.Lerp(fromShoulderLift, shoulderLiftDeg, t));
            ApplyJointTargetDeg(player.elbow, Mathf.Lerp(fromElbow, elbowDeg, t));
            ApplyJointTargetDeg(player.wrist1, Mathf.Lerp(fromWrist1, wrist1Deg, t));
            ApplyJointTargetDeg(player.wrist2, Mathf.Lerp(fromWrist2, wrist2Deg, t));
            ApplyJointTargetDeg(player.wrist3, Mathf.Lerp(fromWrist3, wrist3Deg, t));

            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyJointTargetDeg(player.shoulderPan, shoulderPanDeg);
        ApplyJointTargetDeg(player.shoulderLift, shoulderLiftDeg);
        ApplyJointTargetDeg(player.elbow, elbowDeg);
        ApplyJointTargetDeg(player.wrist1, wrist1Deg);
        ApplyJointTargetDeg(player.wrist2, wrist2Deg);
        ApplyJointTargetDeg(player.wrist3, wrist3Deg);
    }

    private float GetJointTargetDeg(ArticulationBody joint)
    {
        if (joint == null)
            return 0f;

        return joint.xDrive.target;
    }

    private void ApplyJointTargetDeg(ArticulationBody joint, float targetDeg)
    {
        if (joint == null)
            return;

        ArticulationDrive drive = joint.xDrive;
        drive.stiffness = player.stiffness;
        drive.damping = player.damping;
        drive.forceLimit = player.forceLimit;
        drive.target = targetDeg;
        joint.xDrive = drive;
    }

    private bool ValidateJointReferences()
    {
        if (player.shoulderPan == null)
        {
            Debug.LogError("player.shoulderPan is not assigned.");
            return false;
        }

        if (player.shoulderLift == null)
        {
            Debug.LogError("player.shoulderLift is not assigned.");
            return false;
        }

        if (player.elbow == null)
        {
            Debug.LogError("player.elbow is not assigned.");
            return false;
        }

        if (player.wrist1 == null)
        {
            Debug.LogError("player.wrist1 is not assigned.");
            return false;
        }

        if (player.wrist2 == null)
        {
            Debug.LogError("player.wrist2 is not assigned.");
            return false;
        }

        if (player.wrist3 == null)
        {
            Debug.LogError("player.wrist3 is not assigned.");
            return false;
        }

        return true;
    }

    private bool AreObjectsTouching(Transform a, Transform b)
    {
        Collider[] aColliders = a.GetComponentsInChildren<Collider>();
        Collider[] bColliders = b.GetComponentsInChildren<Collider>();

        foreach (Collider colA in aColliders)
        {
            if (!IsValidCollider(colA))
                continue;

            foreach (Collider colB in bColliders)
            {
                if (!IsValidCollider(colB))
                    continue;

                Vector3 direction;
                float distance;

                bool penetrating = Physics.ComputePenetration(
                    colA,
                    colA.transform.position,
                    colA.transform.rotation,
                    colB,
                    colB.transform.position,
                    colB.transform.rotation,
                    out direction,
                    out distance
                );

                if (penetrating)
                    return true;

                Vector3 pointA = colA.ClosestPoint(colB.bounds.center);
                Vector3 pointB = colB.ClosestPoint(pointA);

                if (Vector3.Distance(pointA, pointB) <= contactThreshold)
                    return true;

                Vector3 pointB2 = colB.ClosestPoint(colA.bounds.center);
                Vector3 pointA2 = colA.ClosestPoint(pointB2);

                if (Vector3.Distance(pointA2, pointB2) <= contactThreshold)
                    return true;
            }
        }

        return false;
    }

    private bool HasCollider(Transform obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();

        foreach (Collider col in colliders)
        {
            if (IsValidCollider(col))
                return true;
        }

        return false;
    }

    private bool IsValidCollider(Collider col)
    {
        return col != null &&
               col.enabled &&
               col.gameObject.activeInHierarchy;
    }

    private Vector2 GetUnity2DPosition(Transform obj)
    {
        Vector3 worldPos = obj.position;
        Vector3 localPos = obj.localPosition;

        Debug.Log(
            $"{obj.name}\n" +
            $"World Position: {worldPos}\n" +
            $"Local Position: {localPos}\n" +
            $"Parent: {(obj.parent != null ? obj.parent.name : "None")}"
        );

        if (coordinatePlane == UnityCoordinatePlane.XZ)
            return new Vector2(worldPos.x, worldPos.z);

        return new Vector2(worldPos.x, worldPos.y);
    }

    private GridPathRosClient.Int3 GetStartGridPoint()
    {
        if (useRosClientFirstPointAsStart && rosClient != null)
        {
            GridPathRosClient.Int3[] points = rosClient.GetGridPoints();

            if (points != null && points.Length > 0 && points[0] != null)
            {
                return new GridPathRosClient.Int3(
                    points[0].x,
                    points[0].y,
                    points[0].z
                );
            }
        }

        return new GridPathRosClient.Int3(
            fallbackStartGridPoint.x,
            fallbackStartGridPoint.y,
            fallbackStartGridPoint.z
        );
    }

    private bool IsSamePoint(GridPathRosClient.Int3 a, GridPathRosClient.Int3 b)
    {
        return a != null &&
               b != null &&
               a.x == b.x &&
               a.y == b.y &&
               a.z == b.z;
    }

    private bool ValidateReferences()
    {
        if (pointInputUI == null)
        {
            Debug.LogError("pointInputUI is not assigned.");
            return false;
        }

        if (rosClient == null)
        {
            Debug.LogError("rosClient is not assigned.");
            return false;
        }

        if (player == null)
        {
            Debug.LogError("player is not assigned.");
            return false;
        }

        if (gripper == null)
        {
            Debug.LogError("gripper is not assigned.");
            return false;
        }

        return true;
    }
}