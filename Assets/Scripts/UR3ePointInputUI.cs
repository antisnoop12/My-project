using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UR3ePointInputUI : MonoBehaviour
{
    public GridPathRosClient rosClient;
    public PathActionStore actionStore;

    [Header("Input Fields")]
    public TMP_InputField inputX;
    public TMP_InputField inputY;
    public TMP_InputField inputZ;

    [Header("Display")]
    public TextMeshProUGUI pointsText;

    private readonly List<GridPathRosClient.Int3> currentPoints = new();

    private void Start()
    {
        SyncFromRosClient();
    }

    public void OnClickAddPoint()
    {
        if (!int.TryParse(inputX.text, out int x))
        {
            Debug.LogError("Invalid X value.");
            return;
        }

        if (!int.TryParse(inputY.text, out int y))
        {
            Debug.LogError("Invalid Y value.");
            return;
        }

        if (!int.TryParse(inputZ.text, out int z))
        {
            Debug.LogError("Invalid Z value.");
            return;
        }

        currentPoints.Add(new GridPathRosClient.Int3(x, y, z));
        PushToRosClient();
        RefreshText();

        inputX.text = "";
        inputY.text = "";
        inputZ.text = "";
    }

    public void OnClickRemoveLast()
    {
        if (currentPoints.Count == 0)
        {
            Debug.LogWarning("No points to remove.");
            return;
        }

        currentPoints.RemoveAt(currentPoints.Count - 1);
        PushToRosClient();
        RefreshText();
    }

    public void OnClickClear()
    {
        GridPathRosClient.Int3 convertedPoint = ConvertUnity2Ros(new Vector2(0.105f, -0.185f), 200);
        
        Debug.Log(convertedPoint.x);
        Debug.Log(convertedPoint.y);
        Debug.Log(convertedPoint.z);
        // currentPoints.Clear();
        // PushToRosClient();
        // RefreshText();
    }

    public void OnclickAddUnityPoint()
    {
        if (!float.TryParse(inputX.text, out float x))
        {
            Debug.LogError("Invalid X value.");
            return;
        }

        if (!float.TryParse(inputY.text, out float y))
        {
            Debug.LogError("Invalid Y value.");
            return;
        }

        if (!int.TryParse(inputZ.text, out int z))
        {
            Debug.LogError("Invalid Z value.");
            return;
        }
        GridPathRosClient.Int3 p =  ConvertUnity2Ros(new Vector2(x,y), z);
        currentPoints.Add(p);
        PushToRosClient();
        RefreshText();

        inputX.text = "";
        inputY.text = "";
        inputZ.text = "";
    }

    public void SyncFromRosClient()
    {
        currentPoints.Clear();

        if (rosClient != null)
        {
            var existing = rosClient.GetGridPoints();
            if (existing != null)
                currentPoints.AddRange(existing);
        }

        if (actionStore != null)
            actionStore.SyncCount(currentPoints.Count);

        RefreshText();
    }

    private void PushToRosClient()
    {
        if (rosClient == null)
        {
            Debug.LogError("rosClient is not assigned.");
            return;
        }

        rosClient.SetGridPoints(currentPoints.ToArray());

        if (actionStore != null)
            actionStore.SyncCount(currentPoints.Count);
    }

    private void RefreshText()
    {
        if (pointsText == null)
            return;

        if (currentPoints.Count == 0)
        {
            pointsText.text = "Points: (empty)";
            return;
        }

        string text = "Points:\n";
        for (int i = 0; i < currentPoints.Count; i++)
        {
            var p = currentPoints[i];
            text += $"{i}: [{p.x}, {p.y}, {p.z}]\n";
        }

        pointsText.text = text;
    }
    private static readonly Vector2 A1 = new Vector2(380f, 155f);
    private static readonly Vector2 B1 = new Vector2(-0.111f, -0.245f);

    // 기준점 2
    private static readonly Vector2 A2 = new Vector2(455f, 266f);
    private static readonly Vector2 B2 = new Vector2(0f, -0.325f);

    public GridPathRosClient.Int3 ConvertUnity2Ros(Vector2 unityCoordinate, int zCoordinate)
    {
        Vector2 unityPoint = new Vector2(unityCoordinate.x, unityCoordinate.y);

        Vector2 dB = B2 - B1;
        Vector2 dA = A2 - A1;

        float denom = dB.x * dB.x + dB.y * dB.y;

        if (Mathf.Approximately(denom, 0f))
        {
            Debug.LogError("Debug Error: Check Standard Points");
            return null;
        }

        float real = (dA.x * dB.x + dA.y * dB.y) / denom;
        float imag = (dA.y * dB.x - dA.x * dB.y) / denom;

        Vector2 relativeB = unityPoint - B1;

        float ax = A1.x + real * relativeB.x - imag * relativeB.y;
        float ay = A1.y + imag * relativeB.x + real * relativeB.y;

        return new GridPathRosClient.Int3(Mathf.RoundToInt(ax), Mathf.RoundToInt(ay), zCoordinate);
    }
}