using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class GridPathPreviewRenderer : MonoBehaviour
{
    public GridPathRosClient rosClient;

    [Header("Preview Options")]
    public bool autoRefresh = true;
    public bool warnIfFrameIsNotBase = true;

    [Header("Visibility")]
    public float previewScale = 1f;
    public float verticalLift = 0.002f;

    private LineRenderer lineRenderer;
    private string lastSignature = "";
    private bool frameWarningShown = false;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.useWorldSpace = false;
    }

    private void Update()
    {
        if (autoRefresh)
            RefreshPreview();
    }

    [ContextMenu("Refresh Preview")]
    public void RefreshPreview()
    {
        //Debug.Log("RefreshPreview called");

        if (rosClient == null)
        {
            Debug.LogError("GridPathPreviewRenderer: rosClient is null");
            lineRenderer.positionCount = 0;
            return;
        }

        if (warnIfFrameIsNotBase && rosClient.GetFrameId() != "base" && !frameWarningShown)
        {
            Debug.LogWarning(
                $"GridPathPreviewRenderer assumes base-frame preview, " +
                $"but current frameId is '{rosClient.GetFrameId()}'. Preview may be offset."
            );
            frameWarningShown = true;
        }

        var points = rosClient.GetGridPoints();
        //Debug.Log("Preview points count = " + (points == null ? 0 : points.Length));

        Vector3 origin = rosClient.GetGridOrigin();
        Vector3 step = rosClient.GetGridStep();

        string signature = BuildSignature(points, origin, step);
        if (signature == lastSignature)
            return;

        lastSignature = signature;

        if (points == null || points.Length == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        Vector3[] positions = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];

            // ROS base-frame point
            Vector3 rosPoint = new Vector3(
                origin.x + step.x * p.x,
                origin.y + step.y * p.y,
                origin.z + step.z * p.z
            );

            // ROS (x forward, y left, z up) -> Unity local
            Vector3 unityPoint = RosToUnity(rosPoint) * previewScale;

            // 살짝 띄워서 바닥/로봇과 겹치지 않게
            unityPoint += Vector3.up * verticalLift;

            positions[i] = unityPoint;
        }

        lineRenderer.positionCount = positions.Length;
        lineRenderer.SetPositions(positions);
    }

    private Vector3 RosToUnity(Vector3 ros)
    {
        // ROS FLU -> Unity
        // x forward, y left, z up  ->  Unity local
        return new Vector3(-ros.y, ros.z, ros.x);
    }

    private string BuildSignature(GridPathRosClient.Int3[] points, Vector3 origin, Vector3 step)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.Append(origin.x).Append(",").Append(origin.y).Append(",").Append(origin.z).Append("|");
        sb.Append(step.x).Append(",").Append(step.y).Append(",").Append(step.z).Append("|");

        if (points != null)
        {
            for (int i = 0; i < points.Length; i++)
            {
                sb.Append(points[i].x).Append(",");
                sb.Append(points[i].y).Append(",");
                sb.Append(points[i].z).Append(";");
            }
        }

        return sb.ToString();
    }
}