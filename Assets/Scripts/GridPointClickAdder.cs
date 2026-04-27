using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class GridPointClickAdder : MonoBehaviour
{
    public Camera targetCamera;
    public GridPathRosClient rosClient;
    public UR3ePointInputUI pointUi;
    public PathActionStore actionStore;
    public Transform baseFrameTransform;

    [Header("Click Mode")]
    public bool clickToAddEnabled = false;
    public TextMeshProUGUI modeButtonLabel;

    [Header("Click Options")]
    public bool autoAddOriginPoint = true;
    public bool ignoreClicksOverUI = true;
    public bool requireLeftShift = false;

    [Header("Working Plane")]
    public float previewLift = 0.0f;

    private void Start()
    {
        RefreshModeLabel();
    }

    private void Update()
    {
        if (!clickToAddEnabled)
            return;

        if (targetCamera == null || rosClient == null || baseFrameTransform == null)
            return;

        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (requireLeftShift && !Input.GetKey(KeyCode.LeftShift))
            return;

        if (!TryGetWorldPointOnWorkingPlane(out Vector3 hitWorld))
            return;

        Vector3 localUnity = baseFrameTransform.InverseTransformPoint(hitWorld);
        Vector3 rosPoint = UnityToRos(localUnity);

        Vector3 origin = rosClient.GetGridOrigin();
        Vector3 step = rosClient.GetGridStep();

        if (Mathf.Approximately(step.x, 0f) ||
            Mathf.Approximately(step.y, 0f) ||
            Mathf.Approximately(step.z, 0f))
        {
            Debug.LogError("Grid step contains zero. Cannot convert click to grid index.");
            return;
        }

        int gx = Mathf.RoundToInt((rosPoint.x - origin.x) / step.x);
        int gy = Mathf.RoundToInt((rosPoint.y - origin.y) / step.y);
        int gz = Mathf.RoundToInt((rosPoint.z - origin.z) / step.z);

        var newPoint = new GridPathRosClient.Int3(gx, gy, gz);

        List<GridPathRosClient.Int3> points = new List<GridPathRosClient.Int3>();
        var existing = rosClient.GetGridPoints();
        if (existing != null)
            points.AddRange(existing);

        if (autoAddOriginPoint && points.Count == 0)
            points.Add(new GridPathRosClient.Int3(0, 0, 0));

        if (points.Count == 0 || !IsSame(points[points.Count - 1], newPoint))
            points.Add(newPoint);

        rosClient.SetGridPoints(points.ToArray());

        if (actionStore != null)
            actionStore.SyncCount(points.Count);

        if (pointUi != null)
            pointUi.SyncFromRosClient();

        Debug.Log($"Clicked world={hitWorld} -> ros={rosPoint} -> grid=[{gx},{gy},{gz}]");
    }

    public void OnClickToggleClickMode()
    {
        clickToAddEnabled = !clickToAddEnabled;
        RefreshModeLabel();
        Debug.Log("Click-to-add mode: " + (clickToAddEnabled ? "ON" : "OFF"));
    }

    private void RefreshModeLabel()
    {
        if (modeButtonLabel == null)
            return;

        modeButtonLabel.text = clickToAddEnabled ? "Click Add: ON" : "Click Add: OFF";
    }

    private bool TryGetWorldPointOnWorkingPlane(out Vector3 hitWorld)
    {
        Vector3 originRos = rosClient.GetGridOrigin();

        Vector3 planePointLocal = RosToUnity(originRos);
        Vector3 planePointWorld = baseFrameTransform.TransformPoint(planePointLocal);
        Vector3 planeNormalWorld = baseFrameTransform.TransformDirection(Vector3.up);

        Plane plane = new Plane(planeNormalWorld, planePointWorld + planeNormalWorld * previewLift);
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);

        if (plane.Raycast(ray, out float enter))
        {
            hitWorld = ray.GetPoint(enter);
            return true;
        }

        hitWorld = default;
        return false;
    }

    private Vector3 RosToUnity(Vector3 ros)
    {
        return new Vector3(-ros.y, ros.z, ros.x);
    }

    private Vector3 UnityToRos(Vector3 unity)
    {
        return new Vector3(unity.z, -unity.x, unity.y);
    }

    private bool IsSame(GridPathRosClient.Int3 a, GridPathRosClient.Int3 b)
    {
        return a.x == b.x && a.y == b.y && a.z == b.z;
    }
}