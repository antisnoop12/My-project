using System.Collections;
using UnityEngine;

public class SimpleParallelGripper : MonoBehaviour
{
    public Transform leftFinger;
    public Transform rightFinger;

    [Header("Finger Local Positions")]
    public Vector3 leftOpenLocalPosition = new Vector3(-0.015f, 0f, 0.04f);
    public Vector3 leftClosedLocalPosition = new Vector3(-0.005f, 0f, 0.04f);

    public Vector3 rightOpenLocalPosition = new Vector3(0.015f, 0f, 0.04f);
    public Vector3 rightClosedLocalPosition = new Vector3(0.005f, 0f, 0.04f);

    [Header("Motion")]
    public float motionDuration = 0.2f;
    public bool startOpened = true;

    private Coroutine motionRoutine;
    private bool isOpen;

    private void Start()
    {
        ApplyImmediate(startOpened);
    }

    public void Grip()
    {
        SetOpen(false);
    }

    public void Release()
    {
        SetOpen(true);
    }

    public void Toggle()
    {
        SetOpen(!isOpen);
    }

    public void SetOpen(bool open)
    {
        if (leftFinger == null || rightFinger == null)
        {
            Debug.LogError("SimpleParallelGripper: finger references are missing.");
            return;
        }

        if (motionRoutine != null)
            StopCoroutine(motionRoutine);

        motionRoutine = StartCoroutine(SetOpenCoroutine(open));
    }

    private IEnumerator SetOpenCoroutine(bool open)
    {
        isOpen = open;

        Vector3 leftFrom = leftFinger.localPosition;
        Vector3 rightFrom = rightFinger.localPosition;

        Vector3 leftTo = open ? leftOpenLocalPosition : leftClosedLocalPosition;
        Vector3 rightTo = open ? rightOpenLocalPosition : rightClosedLocalPosition;

        float elapsed = 0f;

        while (elapsed < motionDuration)
        {
            float t = Mathf.Clamp01(elapsed / motionDuration);

            leftFinger.localPosition = Vector3.Lerp(leftFrom, leftTo, t);
            rightFinger.localPosition = Vector3.Lerp(rightFrom, rightTo, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        leftFinger.localPosition = leftTo;
        rightFinger.localPosition = rightTo;
        motionRoutine = null;
    }

    private void ApplyImmediate(bool open)
    {
        isOpen = open;

        if (leftFinger != null)
            leftFinger.localPosition = open ? leftOpenLocalPosition : leftClosedLocalPosition;

        if (rightFinger != null)
            rightFinger.localPosition = open ? rightOpenLocalPosition : rightClosedLocalPosition;
    }
}