using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class TrajectoryCsvLogger : MonoBehaviour
{
    public UR3eTrajectoryPlayer player;

    [Header("Save Settings")]
    public string baseFolderPath = "/home/anywoo/바탕화면/TrajectoryLog";
    public float sampleRateHz = 10f;

    [Header("Folder Index Settings")]
    public int startIndex = 0;
    public int indexDigits = 3;

    private Coroutine logRoutine;
    private StreamWriter writer;

    private bool isLogging = false;
    private float startTime;

    private int sampleIndex;
    private int currentSequenceIndex = -1;
    private int currentSequenceSampleIndex = 0;

    private bool gripperClosed = false;

    private string currentFolderPath;
    private string currentCsvPath;

    public string CurrentFolderPath => currentFolderPath;
    public string CurrentCsvPath => currentCsvPath;
    public bool IsLogging => isLogging;

    public void StartLogging(bool initialGripperClosed = false)
    {
        if (player == null)
        {
            Debug.LogError("TrajectoryCsvLogger: player is not assigned.");
            return;
        }

        StopLogging();

        Directory.CreateDirectory(baseFolderPath);

        int nextIndex = GetNextTrajectoryIndex();
        string indexText = nextIndex.ToString().PadLeft(Mathf.Max(1, indexDigits), '0');

        string folderName = $"trajectory_{indexText}";
        string fileName = $"trajectory_{indexText}.csv";

        currentFolderPath = Path.Combine(baseFolderPath, folderName);
        Directory.CreateDirectory(currentFolderPath);

        currentCsvPath = Path.Combine(currentFolderPath, fileName);

        writer = new StreamWriter(currentCsvPath, false, new UTF8Encoding(true));
        writer.AutoFlush = true;

        writer.WriteLine(
            "sample_index," +
            "sequence_index," +
            "elapsed_sec," +
            "shoulder_pan_deg," +
            "shoulder_lift_deg," +
            "elbow_deg," +
            "wrist_1_deg," +
            "wrist_2_deg," +
            "wrist_3_deg," +
            "gripper_closed," +
            "gripper_state"
        );

        startTime = Time.time;
        sampleIndex = 0;

        currentSequenceIndex = -1;
        currentSequenceSampleIndex = 0;

        gripperClosed = initialGripperClosed;

        isLogging = true;
        logRoutine = StartCoroutine(LogCoroutine());

        Debug.Log($"Trajectory logging started: {currentCsvPath}");
    }

    public void StopLogging()
    {
        if (isLogging)
            WriteSample();

        isLogging = false;

        if (logRoutine != null)
        {
            StopCoroutine(logRoutine);
            logRoutine = null;
        }

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }

    public void BeginSequence(int sequenceIndex)
    {
        currentSequenceIndex = sequenceIndex;
        currentSequenceSampleIndex = 0;

        WriteSample();
    }

    public void SetGripperClosed(bool closed)
    {
        if (gripperClosed == closed)
            return;

        gripperClosed = closed;
        WriteSample();
    }

    private IEnumerator LogCoroutine()
    {
        float interval = 1f / Mathf.Max(0.0001f, sampleRateHz);

        while (isLogging)
        {
            yield return new WaitForSeconds(interval);
            WriteSample();
        }
    }

    private void WriteSample()
    {
        if (!isLogging || writer == null || player == null)
            return;

        if (currentSequenceIndex < 0)
            return;

        float[] deg = player.GetCurrentJointPositionsDeg();

        if (deg == null || deg.Length < 6)
            return;

        float elapsed = Time.time - startTime;

        string sequenceIndexText =
            $"{currentSequenceIndex}_{currentSequenceSampleIndex}";

        string gripperState = gripperClosed ? "closed" : "open";
        int gripperClosedInt = gripperClosed ? 1 : 0;

        writer.WriteLine(
            $"{sampleIndex}," +
            $"{sequenceIndexText}," +
            $"{elapsed.ToString("F4", CultureInfo.InvariantCulture)}," +
            $"{deg[0].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{deg[1].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{deg[2].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{deg[3].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{deg[4].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{deg[5].ToString("F6", CultureInfo.InvariantCulture)}," +
            $"{gripperClosedInt}," +
            $"{gripperState}"
        );

        sampleIndex++;
        currentSequenceSampleIndex++;
    }

    private int GetNextTrajectoryIndex()
    {
        if (!Directory.Exists(baseFolderPath))
            return startIndex;

        int maxIndex = startIndex - 1;
        string[] dirs = Directory.GetDirectories(baseFolderPath, "trajectory_*");

        foreach (string dir in dirs)
        {
            string name = Path.GetFileName(dir);

            if (!name.StartsWith("trajectory_"))
                continue;

            string suffix = name.Substring("trajectory_".Length);

            if (int.TryParse(suffix, out int idx) && idx > maxIndex)
                maxIndex = idx;
        }

        return maxIndex + 1;
    }

    private void OnDestroy()
    {
        StopLogging();
    }
}