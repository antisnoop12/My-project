using System.Collections;
using UnityEngine;

public class UR3eControlUI : MonoBehaviour
{
    public GridPathRosClient rosClient;
    public UR3eTrajectoryPlayer player;
    public PathActionStore actionStore;
    public HingedGripper gripper;
    public TrajectoryCsvLogger trajectoryLogger;

    public float waitTimeout = 10f;
    public float segmentPause = 0.2f;
    public float gripperActionPause = 0.25f;

    [Header("Segment validation")]
    [Range(0f, 1f)]
    public float minAcceptableFraction = 0.95f;

    private Coroutine moveRoutine;

    public void OnClickHome()
    {
        if (player == null)
        {
            Debug.LogError("player is not assigned.");
            return;
        }

        player.MoveToHome();
    }

    public void OnClickMove()
    {
        if (rosClient == null || player == null)
        {
            Debug.LogError("rosClient or player is not assigned.");
            return;
        }

        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        moveRoutine = StartCoroutine(RequestAndPlayAllSegmentsCoroutine());
    }

    private IEnumerator RequestAndPlayAllSegmentsCoroutine()
    {
        var points = rosClient.GetGridPoints();

        if (points == null || points.Length < 2)
        {
            Debug.LogError("At least 2 grid points are required.");
            StopLoggingIfNeeded();
            moveRoutine = null;
            yield break;
        }

        if (trajectoryLogger != null)
            trajectoryLogger.StartLogging();

        string[] chainedJointNames = null;
        float[] chainedJointPositionsRad = null;

        for (int i = 0; i < points.Length - 1; i++)
        {
            GridPathRosClient.Int3[] segment = new GridPathRosClient.Int3[]
            {
                new GridPathRosClient.Int3(points[i].x, points[i].y, points[i].z),
                new GridPathRosClient.Int3(points[i + 1].x, points[i + 1].y, points[i + 1].z)
            };

            Debug.Log(
                $"Requesting segment {i}: " +
                $"[{segment[0].x},{segment[0].y},{segment[0].z}] -> " +
                $"[{segment[1].x},{segment[1].y},{segment[1].z}]"
            );

            int startVersion = rosClient.ResultVersion;

            rosClient.SendRequestWithGridPoints(
                segment,
                false,
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
                Debug.LogError($"Timed out waiting for ROS result on segment {i}.");
                StopLoggingIfNeeded();
                moveRoutine = null;
                yield break;
            }

            var result = rosClient.LastResult;

            if (result == null || !result.ok)
            {
                Debug.LogError($"ROS returned invalid result on segment {i}.");
                StopLoggingIfNeeded();
                moveRoutine = null;
                yield break;
            }

            if (result.points == null || result.points.Length < 2)
            {
                Debug.LogError(
                    $"Segment {i} has no usable trajectory. " +
                    $"fraction={result.fraction}, pointCount={(result.points == null ? 0 : result.points.Length)}"
                );
                StopLoggingIfNeeded();
                moveRoutine = null;
                yield break;
            }

            float segmentDuration =
                result.points[result.points.Length - 1].time_from_start /
                Mathf.Max(0.0001f, player.playbackSpeed);

            player.PlayLastTrajectory();

            if (result.fraction < minAcceptableFraction)
            {
                Debug.LogError(
                    $"Segment {i} planned only partially. " +
                    $"fraction={result.fraction}, required>={minAcceptableFraction}. " +
                    $"Played partial trajectory and stopped chaining here."
                );

                if (segmentDuration > 0f)
                    yield return new WaitForSeconds(segmentDuration + segmentPause);
                else
                    yield return new WaitForSeconds(segmentPause);

                StopLoggingIfNeeded();
                moveRoutine = null;
                yield break;
            }

            if (result.joint_names == null ||
                result.points[result.points.Length - 1].positions_rad == null ||
                result.joint_names.Length == 0 ||
                result.points[result.points.Length - 1].positions_rad.Length == 0)
            {
                Debug.LogError($"Segment {i} has no valid chaining joint state.");
                StopLoggingIfNeeded();
                moveRoutine = null;
                yield break;
            }

            if (segmentDuration > 0f)
                yield return new WaitForSeconds(segmentDuration + segmentPause);
            else
                yield return new WaitForSeconds(segmentPause);

            yield return ExecuteWaypointAction(i + 1);

            chainedJointNames = (string[])result.joint_names.Clone();
            chainedJointPositionsRad = (float[])result.points[result.points.Length - 1].positions_rad.Clone();
        }

        StopLoggingIfNeeded();
        Debug.Log("All segments completed.");
        moveRoutine = null;
    }

    private IEnumerator ExecuteWaypointAction(int waypointIndex)
    {
        if (actionStore == null || gripper == null)
            yield break;

        PathActionType action = actionStore.GetAction(waypointIndex);

        if (action == PathActionType.Grip)
        {
            Debug.Log($"Waypoint {waypointIndex}: Grip");
            gripper.Grip();
            yield return new WaitForSeconds(gripperActionPause);
        }
        else if (action == PathActionType.Release)
        {
            Debug.Log($"Waypoint {waypointIndex}: Release");
            gripper.Release();
            yield return new WaitForSeconds(gripperActionPause);
        }
    }

    private void StopLoggingIfNeeded()
    {
        if (trajectoryLogger != null)
            trajectoryLogger.StopLogging();
    }
}