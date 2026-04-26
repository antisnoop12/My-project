using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UR3ePointInputUI : MonoBehaviour
{
    public GridPathRosClient rosClient;

    [Header("Input Fields")]
    public TMP_InputField inputX;
    public TMP_InputField inputY;
    public TMP_InputField inputZ;

    [Header("Display")]
    public TextMeshProUGUI pointsText;

    private readonly List<GridPathRosClient.Int3> currentPoints = new List<GridPathRosClient.Int3>();

    private void Start()
    {
        if (rosClient != null)
        {
            var existing = rosClient.GetGridPoints();
            if (existing != null)
            {
                currentPoints.Clear();
                currentPoints.AddRange(existing);
                RefreshText();
            }
        }
    }

    public void OnClickAddPoint()
    {
        if (inputX == null || inputY == null || inputZ == null)
        {
            Debug.LogError("Input fields are not assigned.");
            return;
        }

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

        if (currentPoints.Count == 0)
        {
            currentPoints.Add(new GridPathRosClient.Int3(0, 0, 0));
        }

        PushToRosClient();
        RefreshText();
    }

    public void OnClickClear()
    {
        currentPoints.Clear();
        currentPoints.Add(new GridPathRosClient.Int3(0, 0, 0));
        currentPoints.Add(new GridPathRosClient.Int3(0, 1, 0));

        PushToRosClient();
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
    }

    private void RefreshText()
    {
        if (pointsText == null)
            return;

        if (currentPoints.Count == 0)
        {
            pointsText.text = "Points: (none)";
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
}