using System.Collections;
using UnityEngine;

public class HingedGripper : MonoBehaviour
{
    public Transform leftFingerRoot;
    public Transform rightFingerRoot;

    [Header("Local Euler Angles")]
    public Vector3 leftOpenEuler = new Vector3(0f, 0f, 0f);
    public Vector3 leftClosedEuler = new Vector3(0f, 0f, -18f);

    public Vector3 rightOpenEuler = new Vector3(0f, 0f, 0f);
    public Vector3 rightClosedEuler = new Vector3(0f, 0f, 18f);

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
        if (leftFingerRoot == null || rightFingerRoot == null)
        {
            Debug.LogError("HingedGripper: finger roots are missing.");
            return;
        }

        if (motionRoutine != null)
            StopCoroutine(motionRoutine);

        motionRoutine = StartCoroutine(SetOpenCoroutine(open));
    }

    private IEnumerator SetOpenCoroutine(bool open)
    {
        isOpen = open;

        Quaternion leftFrom = leftFingerRoot.localRotation;
        Quaternion rightFrom = rightFingerRoot.localRotation;

        Quaternion leftTo = Quaternion.Euler(open ? leftOpenEuler : leftClosedEuler);
        Quaternion rightTo = Quaternion.Euler(open ? rightOpenEuler : rightClosedEuler);

        float elapsed = 0f;

        while (elapsed < motionDuration)
        {
            float t = Mathf.Clamp01(elapsed / motionDuration);

            leftFingerRoot.localRotation = Quaternion.Slerp(leftFrom, leftTo, t);
            rightFingerRoot.localRotation = Quaternion.Slerp(rightFrom, rightTo, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        leftFingerRoot.localRotation = leftTo;
        rightFingerRoot.localRotation = rightTo;
        motionRoutine = null;
    }

    private void ApplyImmediate(bool open)
    {
        isOpen = open;

        if (leftFingerRoot != null)
            leftFingerRoot.localRotation = Quaternion.Euler(open ? leftOpenEuler : leftClosedEuler);

        if (rightFingerRoot != null)
            rightFingerRoot.localRotation = Quaternion.Euler(open ? rightOpenEuler : rightClosedEuler);
    }
}