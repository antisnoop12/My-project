using System.Collections;
using UnityEngine;

public class UR3eControlUI : MonoBehaviour
{
    public GridPathRosClient rosClient;
    public UR3eTrajectoryPlayer player;

    public float waitTimeout = 10f;
    public float segmentPause = 0.2f;

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
            moveRoutine = null;
            yield break;
        }

        // 첫 segment는 ROS 최신 joint_state를 시작 상태로 쓰기 위해 null 유지
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
                moveRoutine = null;
                yield break;
            }

            var result = rosClient.LastResult;

            if (result == null || !result.ok)
            {
                Debug.LogError($"ROS returned invalid result on segment {i}.");
                moveRoutine = null;
                yield break;
            }

            if (result.points == null || result.points.Length < 2)
            {
                Debug.LogError(
                    $"Segment {i} has no usable trajectory. " +
                    $"fraction={result.fraction}, pointCount={(result.points == null ? 0 : result.points.Length)}"
                );
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

                moveRoutine = null;
                yield break;
            }

            if (result.joint_names == null ||
                result.points[result.points.Length - 1].positions_rad == null ||
                result.joint_names.Length == 0 ||
                result.points[result.points.Length - 1].positions_rad.Length == 0)
            {
                Debug.LogError($"Segment {i} has no valid chaining joint state.");
                moveRoutine = null;
                yield break;
            }

            chainedJointNames = (string[])result.joint_names.Clone();
            chainedJointPositionsRad = (float[])result.points[result.points.Length - 1].positions_rad.Clone();

            if (segmentDuration > 0f)
                yield return new WaitForSeconds(segmentDuration + segmentPause);
            else
                yield return new WaitForSeconds(segmentPause);
        }

        Debug.Log("All segments completed.");
        moveRoutine = null;
    }
}