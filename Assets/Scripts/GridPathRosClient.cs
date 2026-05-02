using System;
using System.Globalization;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class GridPathRosClient : MonoBehaviour
{
    [Header("ROS Topics")]
    [SerializeField] private string requestTopic = "/grid_path_request";
    [SerializeField] private string resultTopic = "/grid_path_result";

    [Header("Request Settings")]
    [SerializeField] private string frameId = "base";
    [SerializeField] private string groupName = "ur_manipulator";
    [SerializeField] private string linkName = "tool0";

    [SerializeField] private OrientationData orientation = new OrientationData
    {
        qx = -0.00039187f,
        qy = 0.99984348f,
        qz = 0.01247830f,
        qw = 0.01253627f
    };

    [SerializeField] private Vector3 gridOrigin = new Vector3(-0.13686154f, -0.26718082f, 0.14697126f);
    [SerializeField] private Vector3 gridStep = new Vector3(0.02f, 0.02f, 0.02f);

    [Header("Grid Point Input")]
    [SerializeField] private Int3[] gridPoints = new Int3[]
    {
        new Int3(0, 0, 0),
        new Int3(0, 0, 1)
    };

    [Header("Unity Coordinate Input (Optional)")]
    [SerializeField] private bool useUnityPointInput = false;
    [SerializeField] private bool unityPointsAreWorldSpace = true;
    [SerializeField] private Transform baseFrameTransform;
    [SerializeField] private Vector3[] unityPoints = new Vector3[]
    {
        new Vector3(-0.111f, 0.05549323f, -0.245f),
        new Vector3(-0.05900016f, 0.05549321f, -0.3599993f)
    };

    [Header("Planner Settings")]
    [SerializeField] private int subdividePerSegment = 10;
    [SerializeField] private float maxStep = 0.005f;
    [SerializeField] private float jumpThreshold = 0.0f;
    [SerializeField] private bool avoidCollisions = true;
    [SerializeField] private bool executeOnRequest = false;
    [SerializeField] private bool requireFullFraction = true;

    private ROSConnection ros;
    private GridPathResult lastResult;

    public GridPathResult LastResult => lastResult;
    public int ResultVersion { get; private set; }

    [Serializable]
    public class OrientationData
    {
        public float qx;
        public float qy;
        public float qz;
        public float qw;
    }

    [Serializable]
    public class Int3
    {
        public int x;
        public int y;
        public int z;

        public Int3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    [Serializable]
    public class GridPathRequest
    {
        public string frame_id;
        public string group_name;
        public string link_name;
        public OrientationData orientation;
        public float[] grid_origin;
        public float[] grid_step;
        public Int3[] grid_points;
        public int subdivide_per_segment;
        public float max_step;
        public float jump_threshold;
        public bool avoid_collisions;
        public bool execute;
        public bool require_full_fraction;
    }

    private void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(requestTopic);
        ros.Subscribe<StringMsg>(resultTopic, OnResultReceived);

        Debug.Log("GridPathRosClient ready");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SendRequest();
        }
    }

    public Vector3 GetGridOrigin()
    {
        return gridOrigin;
    }

    public Vector3 GetGridStep()
    {
        return gridStep;
    }

    public string GetFrameId()
    {
        return frameId;
    }

    public Int3[] GetGridPoints()
    {
        return gridPoints;
    }

    public void SetGridPoints(Int3[] points)
    {
        if (points == null)
        {
            gridPoints = Array.Empty<Int3>();
            Debug.LogWarning("SetGridPoints called with null. Cleared points.");
            return;
        }

        gridPoints = points;
        Debug.Log("Grid points updated. Count = " + gridPoints.Length);
    }

    public void SetUnityPoints(Vector3[] points)
    {
        unityPoints = points ?? Array.Empty<Vector3>();
        Debug.Log("Unity points updated. Count = " + unityPoints.Length);
    }

    [ContextMenu("Send Request")]
    public void SendRequest()
    {
        Int3[] pointsToSend = GetPointsToSend();

        GridPathRequest req = new GridPathRequest
        {
            frame_id = frameId,
            group_name = groupName,
            link_name = linkName,
            orientation = orientation,
            grid_origin = new float[] { gridOrigin.x, gridOrigin.y, gridOrigin.z },
            grid_step = new float[] { gridStep.x, gridStep.y, gridStep.z },
            grid_points = pointsToSend,
            subdivide_per_segment = subdividePerSegment,
            max_step = maxStep,
            jump_threshold = jumpThreshold,
            avoid_collisions = avoidCollisions,
            execute = executeOnRequest,
            require_full_fraction = requireFullFraction
        };

        string json = ToBridgeJson(req);
        ros.Publish(requestTopic, new StringMsg(json));
        Debug.Log("Sent request: " + json);
    }

    public void SendRequestWithGridPoints(
        Int3[] points,
        bool requireFullFractionOverride = false,
        string[] startJointNames = null,
        float[] startJointPositionsRad = null)
    {
        GridPathRequest req = new GridPathRequest
        {
            frame_id = frameId,
            group_name = groupName,
            link_name = linkName,
            orientation = orientation,
            grid_origin = new float[] { gridOrigin.x, gridOrigin.y, gridOrigin.z },
            grid_step = new float[] { gridStep.x, gridStep.y, gridStep.z },
            grid_points = points,
            subdivide_per_segment = subdividePerSegment,
            max_step = maxStep,
            jump_threshold = jumpThreshold,
            avoid_collisions = avoidCollisions,
            execute = executeOnRequest,
            require_full_fraction = requireFullFractionOverride
        };

        string json = ToBridgeJson(req);

        if (startJointNames != null &&
            startJointPositionsRad != null &&
            startJointNames.Length == startJointPositionsRad.Length &&
            startJointNames.Length > 0)
        {
            string namesJson = "\"start_joint_names\":[";
            for (int i = 0; i < startJointNames.Length; i++)
            {
                namesJson += "\"" + startJointNames[i] + "\"";
                if (i < startJointNames.Length - 1)
                    namesJson += ",";
            }
            namesJson += "]";

            string posJson = "\"start_joint_positions_rad\":[";
            for (int i = 0; i < startJointPositionsRad.Length; i++)
            {
                posJson += startJointPositionsRad[i].ToString("R", CultureInfo.InvariantCulture);
                if (i < startJointPositionsRad.Length - 1)
                    posJson += ",";
            }
            posJson += "]";

            json = json.Substring(0, json.Length - 1) + "," + namesJson + "," + posJson + "}";
        }

        ros.Publish(requestTopic, new StringMsg(json));
        Debug.Log("Sent segment request: " + json);
    }

    public Int3 ConvertUnityPointToGridPoint(Vector3 unityPoint)
    {
        Vector3 localUnity = unityPoint;

        if (unityPointsAreWorldSpace)
        {
            if (baseFrameTransform == null)
            {
                Debug.LogError("baseFrameTransform is null. Cannot convert world Unity point to base-local point.");
                return new Int3(0, 0, 0);
            }

            localUnity = baseFrameTransform.InverseTransformPoint(unityPoint);
        }

        Vector3 rosPoint = UnityToRos(localUnity);

        int gx = Mathf.RoundToInt((rosPoint.x - gridOrigin.x) / gridStep.x);
        int gy = Mathf.RoundToInt((rosPoint.y - gridOrigin.y) / gridStep.y);
        int gz = Mathf.RoundToInt((rosPoint.z - gridOrigin.z) / gridStep.z);

        Debug.Log(
            "Unity point " + unityPoint +
            " -> local " + localUnity +
            " -> ros " + rosPoint +
            " -> grid [" + gx + "," + gy + "," + gz + "]"
        );

        return new Int3(gx, gy, gz);
    }

    public Int3[] ConvertUnityPointsToGridPoints()
    {
        if (unityPoints == null || unityPoints.Length == 0)
            return Array.Empty<Int3>();

        Int3[] converted = new Int3[unityPoints.Length];
        for (int i = 0; i < unityPoints.Length; i++)
        {
            converted[i] = ConvertUnityPointToGridPoint(unityPoints[i]);
        }

        return converted;
    }

    private Int3[] GetPointsToSend()
    {
        if (useUnityPointInput && unityPoints != null && unityPoints.Length > 0)
            return ConvertUnityPointsToGridPoints();

        return gridPoints;
    }

    private void OnResultReceived(StringMsg msg)
    {
        lastResult = JsonUtility.FromJson<GridPathResult>(msg.data);

        if (lastResult == null)
        {
            Debug.LogError("Result parse failed");
            return;
        }

        if (!lastResult.ok)
        {
            Debug.LogError("ROS result error: " + lastResult.error);
            ResultVersion++;
            return;
        }

        Debug.Log(
            "Result OK | fraction=" + lastResult.fraction +
            " | trajectory_point_count=" + lastResult.trajectory_point_count
        );

        if (lastResult.points != null && lastResult.points.Length > 0)
        {
            GridPathPointResult first = lastResult.points[0];
            GridPathPointResult last = lastResult.points[lastResult.points.Length - 1];

            Debug.Log("First point time: " + first.time_from_start);
            Debug.Log("Last point time: " + last.time_from_start);

            if (last.positions_deg != null)
            {
                Debug.Log("Last joint deg: " + string.Join(", ", last.positions_deg));
            }
        }

        ResultVersion++;
    }

    private Vector3 UnityToRos(Vector3 unityLocal)
    {
        // inverse of RosToUnity = (-y, z, x)
        return new Vector3(
            unityLocal.z,
            -unityLocal.x,
            unityLocal.y
        );
    }

    private string ToBridgeJson(GridPathRequest req)
    {
        string orientationJson =
            "\"orientation\":{\"qx\":" + req.orientation.qx.ToString(CultureInfo.InvariantCulture) +
            ",\"qy\":" + req.orientation.qy.ToString(CultureInfo.InvariantCulture) +
            ",\"qz\":" + req.orientation.qz.ToString(CultureInfo.InvariantCulture) +
            ",\"qw\":" + req.orientation.qw.ToString(CultureInfo.InvariantCulture) +
            "}";

        string originJson =
            "\"grid_origin\":[" +
            req.grid_origin[0].ToString(CultureInfo.InvariantCulture) + "," +
            req.grid_origin[1].ToString(CultureInfo.InvariantCulture) + "," +
            req.grid_origin[2].ToString(CultureInfo.InvariantCulture) +
            "]";

        string stepJson =
            "\"grid_step\":[" +
            req.grid_step[0].ToString(CultureInfo.InvariantCulture) + "," +
            req.grid_step[1].ToString(CultureInfo.InvariantCulture) + "," +
            req.grid_step[2].ToString(CultureInfo.InvariantCulture) +
            "]";

        string pointsJson = "\"grid_points\":[";
        for (int i = 0; i < req.grid_points.Length; i++)
        {
            Int3 p = req.grid_points[i];
            pointsJson += "[" + p.x + "," + p.y + "," + p.z + "]";
            if (i < req.grid_points.Length - 1)
                pointsJson += ",";
        }
        pointsJson += "]";

        string json =
            "{"
            + "\"frame_id\":\"" + req.frame_id + "\","
            + "\"group_name\":\"" + req.group_name + "\","
            + "\"link_name\":\"" + req.link_name + "\","
            + orientationJson + ","
            + originJson + ","
            + stepJson + ","
            + pointsJson + ","
            + "\"subdivide_per_segment\":" + req.subdivide_per_segment + ","
            + "\"max_step\":" + req.max_step.ToString(CultureInfo.InvariantCulture) + ","
            + "\"jump_threshold\":" + req.jump_threshold.ToString(CultureInfo.InvariantCulture) + ","
            + "\"avoid_collisions\":" + req.avoid_collisions.ToString().ToLower() + ","
            + "\"execute\":" + req.execute.ToString().ToLower() + ","
            + "\"require_full_fraction\":" + req.require_full_fraction.ToString().ToLower()
            + "}";

        return json;
    }
}