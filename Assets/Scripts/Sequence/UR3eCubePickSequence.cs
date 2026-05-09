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
    public TrajectoryCsvLogger trajectoryLogger;

    [Header("Target")]
    public Transform targetCube;

    [Tooltip("Optional. 실제 그리퍼가 잡으러 갈 위치. 비워두면 targetCube 위치를 사용함.")]
    public Transform targetGripPoint;

    [Tooltip("Optional. 접촉 검사에 사용할 Collider Root. 비워두면 targetCube를 사용함.")]
    public Transform targetContactRoot;

    [Header("Goal")]
    public Transform goalObject;

    [Tooltip("Optional. goal 접촉 검사에 사용할 Collider Root. 비워두면 goalObject를 사용함.")]
    public Transform goalContactRoot;

    [Header("Unity Coordinate Plane")]
    public UnityCoordinatePlane coordinatePlane = UnityCoordinatePlane.XZ;

    [Header("ROS Grid Z")]
    public int approachZ = 200;
    public int pickZ = 50;
    public int liftZ = 200;
    public int goalZ = 300;

    [Header("Segment Playback Speed")]
    public bool useCustomSegmentSpeed = true;

    [Tooltip("-1이면 UR3eTrajectoryPlayer의 playbackSpeed 사용")]
    public float approachPlaybackSpeed = -1f;

    [Tooltip("-1이면 UR3eTrajectoryPlayer의 playbackSpeed 사용")]
    public float pickPlaybackSpeed = -1f;

    [Tooltip("-1이면 UR3eTrajectoryPlayer의 playbackSpeed 사용")]
    public float liftPlaybackSpeed = -1f;

    [Tooltip("goalZ까지 이동하는 속도. 작을수록 느림.")]
    public float goalMovePlaybackSpeed = 0.15f;

    [Tooltip("꽂으러 내려가는 속도. 작을수록 느림.")]
    public float lowerPlaybackSpeed = 0.10f;

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
    // Pick -> Lift -> Goal 이동 -> 접촉할 때까지 하강
    //
    // CSV sequence_index:
    // 0_0, 0_1, ... = approachZ까지 이동
    // 1_0, 1_1, ... = pickZ까지 하강
    // 2_0, 2_1, ... = liftZ까지 상승
    // 3_0, 3_1, ... = goalZ까지 이동
    // 4_0, 4_1, ... = 꽂으러 하강
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
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        if (player != null)
            player.StopPlayback();

        StopLoggingIfNeeded();

        sequenceRoutine = StartCoroutine(
            PickLiftMoveGoalAndLowerCoroutine(targetCube, goalObject)
        );
    }

    // =========================================================
    // 버튼 함수 2
    // Pick -> Lift -> 지정한 Joint Axis 각도로 이동
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
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        if (player != null)
            player.StopPlayback();

        StopLoggingIfNeeded();

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

        StopLoggingIfNeeded();
    }

    private IEnumerator PickLiftMoveGoalAndLowerCoroutine(Transform cube, Transform goal)
    {
        if (!ValidateReferences())
        {
            sequenceRoutine = null;
            yield break;
        }

        ResetSequenceState();

        StartLoggingIfNeeded();

        Transform pickReference = GetTargetPickReference(cube);
        Vector2 targetUnity2D = GetUnity2DPosition(pickReference);

        GridPathRosClient.Int3 approachPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, approachZ);

        GridPathRosClient.Int3 pickPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, pickZ);

        GridPathRosClient.Int3 liftPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, liftZ);

        if (approachPoint == null || pickPoint == null || liftPoint == null)
        {
            AbortSequence("Failed to convert target position to ROS grid point.");
            yield break;
        }

        GridPathRosClient.Int3 startPoint = GetStartGridPoint();

        Debug.Log(
            $"Pick Lift Goal Lower Sequence Start\n" +
            $"Pick Reference: {pickReference.name}\n" +
            $"Target Unity2D: {targetUnity2D}\n" +
            $"Start: [{startPoint.x}, {startPoint.y}, {startPoint.z}]\n" +
            $"Approach: [{approachPoint.x}, {approachPoint.y}, {approachPoint.z}]\n" +
            $"Pick: [{pickPoint.x}, {pickPoint.y}, {pickPoint.z}]\n" +
            $"Lift: [{liftPoint.x}, {liftPoint.y}, {liftPoint.z}]"
        );

        // =========================================================
        // PICK START
        // sequence 0: 현재 시작점 -> approachZ
        // sequence 1: approachZ -> pickZ
        // 이후 gripper close 기록
        // =========================================================

        if (!IsSamePoint(startPoint, approachPoint))
        {
            yield return RequestAndPlaySegmentCoroutine(startPoint, approachPoint, 0);

            if (!lastSegmentSucceeded)
            {
                AbortSequence("Pick failed at approach movement.");
                yield break;
            }
        }

        yield return RequestAndPlaySegmentCoroutine(approachPoint, pickPoint, 1);

        if (!lastSegmentSucceeded)
        {
            AbortSequence("Pick failed at downward movement.");
            yield break;
        }

        lastGripPoint = new GridPathRosClient.Int3(
            pickPoint.x,
            pickPoint.y,
            pickPoint.z
        );

        hasGripPoint = true;

        gripper.Grip();
        SetLoggerGripperClosed(true);

        yield return new WaitForSeconds(gripperActionPause);

        Debug.Log("Pick completed.");

        // =========================================================
        // PICK END
        // =========================================================


        // =========================================================
        // LIFT START
        // sequence 2: pickZ -> liftZ
        // =========================================================

        yield return RequestAndPlaySegmentCoroutine(lastGripPoint, liftPoint, 2);

        if (!lastSegmentSucceeded)
        {
            AbortSequence("Lift failed.");
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
        // sequence 3: lift 위치 -> goalZ
        // goalMovePlaybackSpeed가 적용되는 구간
        // =========================================================

        Vector2 currentGoalUnity2D = GetUnity2DPosition(goal);

        GridPathRosClient.Int3 goalPoint =
            pointInputUI.ConvertUnity2Ros(currentGoalUnity2D, goalZ);

        if (goalPoint == null)
        {
            AbortSequence("Failed to convert goal position to ROS grid point.");
            yield break;
        }

        Debug.Log(
            $"Current Goal Unity2D: {currentGoalUnity2D}\n" +
            $"Goal Grid: [{goalPoint.x}, {goalPoint.y}, {goalPoint.z}]"
        );

        yield return RequestAndPlaySegmentCoroutine(lastGripPoint, goalPoint, 3);

        if (!lastSegmentSucceeded)
        {
            AbortSequence("Move to goal failed.");
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
        // sequence 4: goalZ -> minLowerZ 방향으로 하강
        // lowerPlaybackSpeed가 적용되는 구간
        // =========================================================

        if (lowerUntilContact)
        {
            yield return LowerUntilTargetTouchesGoalCoroutine(4);

            if (placeFailed)
            {
                AbortSequence("Lower until contact failed.");
                yield break;
            }
        }

        // =========================================================
        // LOWER UNTIL CONTACT END
        // =========================================================

        Debug.Log("Pick Lift Move Goal And Lower Sequence Completed.");

        StopLoggingIfNeeded();
        sequenceRoutine = null;
    }

    private IEnumerator PickLiftAndMoveToJointPoseCoroutine(Transform cube)
    {
        if (!ValidateReferences())
        {
            sequenceRoutine = null;
            yield break;
        }

        ResetSequenceState();

        Transform pickReference = GetTargetPickReference(cube);
        Vector2 targetUnity2D = GetUnity2DPosition(pickReference);

        GridPathRosClient.Int3 approachPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, approachZ);

        GridPathRosClient.Int3 pickPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, pickZ);

        GridPathRosClient.Int3 liftPoint =
            pointInputUI.ConvertUnity2Ros(targetUnity2D, liftZ);

        if (approachPoint == null || pickPoint == null || liftPoint == null)
        {
            AbortSequence("Failed to convert target position to ROS grid point.");
            yield break;
        }

        GridPathRosClient.Int3 startPoint = GetStartGridPoint();

        Debug.Log(
            $"Pick Lift JointPose Sequence Start\n" +
            $"Pick Reference: {pickReference.name}\n" +
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
                AbortSequence("Pick failed at approach movement.");
                yield break;
            }
        }

        yield return RequestAndPlaySegmentCoroutine(approachPoint, pickPoint, 1);

        if (!lastSegmentSucceeded)
        {
            AbortSequence("Pick failed at downward movement.");
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
            AbortSequence("Lift failed.");
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

    private IEnumerator LowerUntilTargetTouchesGoalCoroutine(int segmentIndex)
    {
        placeContactDetected = false;
        placeFailed = false;

        Transform targetContact = GetTargetContactReference();
        Transform goalContact = GetGoalContactReference();

        if (targetContact == null)
        {
            Debug.LogError("target contact reference is null.");
            placeFailed = true;
            yield break;
        }

        if (goalContact == null)
        {
            Debug.LogError("goal contact reference is null.");
            placeFailed = true;
            yield break;
        }

        if (!HasCollider(targetContact))
        {
            Debug.LogError(
                $"Target contact object '{targetContact.name}' has no valid Collider.\n" +
                $"Assign Target Contact Root to the actual object that has Collider. " +
                $"Do not assign GripPoint here."
            );

            DebugLogColliderSearch(targetContact);
            placeFailed = true;
            yield break;
        }

        if (!HasCollider(goalContact))
        {
            Debug.LogError(
                $"Goal contact object '{goalContact.name}' has no valid Collider.\n" +
                $"Assign Goal Contact Root to the object that has Collider."
            );

            DebugLogColliderSearch(goalContact);
            placeFailed = true;
            yield break;
        }

        if (AreObjectsTouching(targetContact, goalContact))
        {
            Debug.Log("Target and goal are already touching. Lower skipped.");
            placeContactDetected = true;

            if (releaseWhenContact)
            {
                gripper.Release();
                SetLoggerGripperClosed(false);
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
            $"Continuous lowering until contact\n" +
            $"Target Contact Root: {targetContact.name}\n" +
            $"Goal Contact Root: {goalContact.name}\n" +
            $"Grid: [{startPoint.x}, {startPoint.y}, {startPoint.z}] -> " +
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

        float originalPlaybackSpeed = player.playbackSpeed;
        float segmentPlaybackSpeed = GetPlaybackSpeedForSegment(segmentIndex);

        player.playbackSpeed = segmentPlaybackSpeed;

        try
        {
            float segmentDuration =
                result.points[result.points.Length - 1].time_from_start /
                Mathf.Max(0.0001f, segmentPlaybackSpeed);

            BeginLoggingSequence(segmentIndex);
            player.PlayLastTrajectory();

            float elapsedMove = 0f;

            while (elapsedMove < segmentDuration)
            {
                if (AreObjectsTouching(targetContact, goalContact))
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
                        SetLoggerGripperClosed(false);
                        yield return new WaitForSeconds(gripperActionPause);
                    }

                    yield break;
                }

                elapsedMove += Time.deltaTime;
                yield return null;
            }

            if (AreObjectsTouching(targetContact, goalContact))
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
                    SetLoggerGripperClosed(false);
                    yield return new WaitForSeconds(gripperActionPause);
                }

                yield break;
            }

            Debug.LogWarning(
                $"Reached minLowerZ = {minLowerZ}, but target and goal did not touch."
            );

            placeFailed = true;
        }
        finally
        {
            player.playbackSpeed = originalPlaybackSpeed;
        }
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

        float originalPlaybackSpeed = player.playbackSpeed;
        float segmentPlaybackSpeed = GetPlaybackSpeedForSegment(segmentIndex);

        player.playbackSpeed = segmentPlaybackSpeed;

        try
        {
            float segmentDuration =
                result.points[result.points.Length - 1].time_from_start /
                Mathf.Max(0.0001f, segmentPlaybackSpeed);

            BeginLoggingSequence(segmentIndex);
            player.PlayLastTrajectory();

            if (segmentDuration > 0f)
                yield return new WaitForSeconds(segmentDuration + segmentPause);
            else
                yield return new WaitForSeconds(segmentPause);
        }
        finally
        {
            player.playbackSpeed = originalPlaybackSpeed;
        }

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

    private float GetPlaybackSpeedForSegment(int segmentIndex)
    {
        if (!useCustomSegmentSpeed)
            return Mathf.Max(0.0001f, player.playbackSpeed);

        float customSpeed = -1f;

        if (segmentIndex == 0)
            customSpeed = approachPlaybackSpeed;
        else if (segmentIndex == 1)
            customSpeed = pickPlaybackSpeed;
        else if (segmentIndex == 2)
            customSpeed = liftPlaybackSpeed;
        else if (segmentIndex == 3)
            customSpeed = goalMovePlaybackSpeed;
        else if (segmentIndex == 4)
            customSpeed = lowerPlaybackSpeed;

        if (customSpeed > 0f)
            return Mathf.Max(0.0001f, customSpeed);

        return Mathf.Max(0.0001f, player.playbackSpeed);
    }

    private void StartLoggingIfNeeded()
    {
        if (trajectoryLogger == null)
        {
            Debug.LogWarning("trajectoryLogger is not assigned. CSV will not be saved.");
            return;
        }

        trajectoryLogger.StartLogging(false);
    }

    private void StopLoggingIfNeeded()
    {
        if (trajectoryLogger != null)
            trajectoryLogger.StopLogging();
    }

    private void BeginLoggingSequence(int sequenceIndex)
    {
        if (trajectoryLogger != null && trajectoryLogger.IsLogging)
            trajectoryLogger.BeginSequence(sequenceIndex);
    }

    private void SetLoggerGripperClosed(bool closed)
    {
        if (trajectoryLogger != null && trajectoryLogger.IsLogging)
            trajectoryLogger.SetGripperClosed(closed);
    }

    private void AbortSequence(string message)
    {
        Debug.LogError(message);
        StopLoggingIfNeeded();
        sequenceRoutine = null;
    }

    private Transform GetTargetPickReference(Transform cube)
    {
        if (targetGripPoint != null)
            return targetGripPoint;

        return cube;
    }

    private Transform GetTargetContactReference()
    {
        if (targetContactRoot != null)
            return targetContactRoot;

        return targetCube;
    }

    private Transform GetGoalContactReference()
    {
        if (goalContactRoot != null)
            return goalContactRoot;

        return goalObject;
    }

    private void ResetSequenceState()
    {
        chainedJointNames = null;
        chainedJointPositionsRad = null;

        hasGripPoint = false;
        lastGripPoint = null;

        placeContactDetected = false;
        placeFailed = false;
    }

    private bool AreObjectsTouching(Transform a, Transform b)
    {
        Collider[] aColliders = a.GetComponentsInChildren<Collider>(true);
        Collider[] bColliders = b.GetComponentsInChildren<Collider>(true);

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
        if (obj == null)
            return false;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);

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

    private void DebugLogColliderSearch(Transform root)
    {
        if (root == null)
        {
            Debug.LogWarning("Collider search root is null.");
            return;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

        if (colliders == null || colliders.Length == 0)
        {
            Debug.LogWarning($"No Collider components found under '{root.name}'.");
            return;
        }

        string log = $"Colliders found under '{root.name}':\n";

        foreach (Collider col in colliders)
        {
            log +=
                $"- {col.name}, " +
                $"enabled={col.enabled}, " +
                $"activeInHierarchy={col.gameObject.activeInHierarchy}, " +
                $"type={col.GetType().Name}\n";
        }

        Debug.LogWarning(log);
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

        if (targetCube == null)
        {
            Debug.LogError("targetCube is not assigned.");
            return false;
        }

        return true;
    }
}