using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class TrajectoryCsvLogger : MonoBehaviour
{
    public UR3eTrajectoryPlayer player;

    [Header("Save Settings")]
    public string baseFolderPath = "/home/anywoo/바탕화면/TrajectoryLog";
    public float sampleRateHz = 10f;

    private Coroutine logRoutine;
    private StreamWriter writer;
    private bool isLogging = false;
    private float startTime;
    private int sampleIndex;
    private string currentFolderPath;
    private string currentCsvPath;

    public string CurrentFolderPath => currentFolderPath;
    public string CurrentCsvPath => currentCsvPath;
    public bool IsLogging => isLogging;

    public void StartLogging()
    {
        if (player == null)
        {
            Debug.LogError("TrajectoryCsvLogger: player is not assigned.");
            return;
        }

        StopLogging();

        Directory.CreateDirectory(baseFolderPath);

        int nextIndex = GetNextTrajectoryIndex();
        string folderName = $"trajectory_{nextIndex:000}";
        string fileName = $"trajectory_{nextIndex:000}.csv";

        currentFolderPath = Path.Combine(baseFolderPath, folderName);
        Directory.CreateDirectory(currentFolderPath);

        currentCsvPath = Path.Combine(currentFolderPath, fileName);

        writer = new StreamWriter(currentCsvPath, false, new UTF8Encoding(true));
        writer.AutoFlush = true;

        writer.WriteLine("sample_index,elapsed_sec,shoulder_pan_deg,shoulder_lift_deg,elbow_deg,wrist_1_deg,wrist_2_deg,wrist_3_deg");

        startTime = Time.time;
        sampleIndex = 0;
        isLogging = true;

        logRoutine = StartCoroutine(LogCoroutine());

        Debug.Log($"Trajectory logging started: {currentCsvPath}");
    }

    public void StopLogging()
    {
        if (isLogging)
        {
            WriteSample();
        }

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

    private IEnumerator LogCoroutine()
    {
        float interval = 1f / Mathf.Max(0.0001f, sampleRateHz);

        // 시작 시점 1회 저장
        WriteSample();

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

        float elapsed = Time.time - startTime;
        float[] deg = player.GetCurrentJointPositionsDeg();

        if (deg == null || deg.Length < 6)
            return;

        writer.WriteLine(
            $"{sampleIndex}," +
            $"{elapsed:F4}," +
            $"{deg[0]:F6}," +
            $"{deg[1]:F6}," +
            $"{deg[2]:F6}," +
            $"{deg[3]:F6}," +
            $"{deg[4]:F6}," +
            $"{deg[5]:F6}"
        );

        sampleIndex++;
    }

    private int GetNextTrajectoryIndex()
    {
        if (!Directory.Exists(baseFolderPath))
            return 1;

        int maxIndex = 0;
        string[] dirs = Directory.GetDirectories(baseFolderPath, "trajectory_*");

        foreach (string dir in dirs)
        {
            string name = Path.GetFileName(dir);
            if (!name.StartsWith("trajectory_"))
                continue;

            string suffix = name.Substring("trajectory_".Length);
            if (int.TryParse(suffix, out int idx))
            {
                if (idx > maxIndex)
                    maxIndex = idx;
            }
        }

        return maxIndex + 1;
    }

    private void OnDestroy()
    {
        StopLogging();
    }
}