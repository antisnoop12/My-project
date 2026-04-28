using System.Collections;
using UnityEngine;

public class HingedGripper : MonoBehaviour
{
    public Transform leftFingerRoot;
    public Transform rightFingerRoot;

    [Header("Contact Sensors")]
    public GripperContactSensor leftSensor;
    public GripperContactSensor rightSensor;

    [Header("Stop On Contact")]
    public bool stopClosingOnContact = true;
    public bool requireBothContacts = true;

    [Header("Extra Close After Contact")]
    public bool extraCloseAfterContact = true;
    public Vector3 leftExtraCloseEuler = new Vector3(0f, 0f, 0.5f);
    public Vector3 rightExtraCloseEuler = new Vector3(0f, 0f, -0.5f);
    public float extraCloseDuration = 0.08f;

    [Header("Local Euler Angles")]
    public Vector3 leftOpenEuler = new Vector3(0f, 0f, 0f);
    public Vector3 leftClosedEuler = new Vector3(0f, 0f, -10f);

    public Vector3 rightOpenEuler = new Vector3(0f, 0f, 0f);
    public Vector3 rightClosedEuler = new Vector3(0f, 0f, 10f);

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

        if (!open)
        {
            leftSensor?.ClearContacts();
            rightSensor?.ClearContacts();
        }

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
        bool extraCloseExecuted = false;

        while (elapsed < motionDuration)
        {
            if (!open && stopClosingOnContact && ShouldStopClosing())
            {
                if (extraCloseAfterContact && !extraCloseExecuted)
                {
                    yield return StartCoroutine(ExtraCloseCoroutine());
                    extraCloseExecuted = true;
                }

                Debug.Log("HingedGripper: contact detected, stopping close motion.");
                motionRoutine = null;
                yield break;
            }

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

    private IEnumerator ExtraCloseCoroutine()
    {
        Quaternion leftFrom = leftFingerRoot.localRotation;
        Quaternion rightFrom = rightFingerRoot.localRotation;

        Vector3 leftCurrentEuler = GetSignedLocalEuler(leftFingerRoot);
        Vector3 rightCurrentEuler = GetSignedLocalEuler(rightFingerRoot);

        Vector3 leftTargetEuler = leftCurrentEuler + leftExtraCloseEuler;
        Vector3 rightTargetEuler = rightCurrentEuler + rightExtraCloseEuler;

        Quaternion leftTo = Quaternion.Euler(leftTargetEuler);
        Quaternion rightTo = Quaternion.Euler(rightTargetEuler);

        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, extraCloseDuration);

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);

            leftFingerRoot.localRotation = Quaternion.Slerp(leftFrom, leftTo, t);
            rightFingerRoot.localRotation = Quaternion.Slerp(rightFrom, rightTo, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        leftFingerRoot.localRotation = leftTo;
        rightFingerRoot.localRotation = rightTo;
    }

    private bool ShouldStopClosing()
    {
        bool left = leftSensor != null && leftSensor.HasContact;
        bool right = rightSensor != null && rightSensor.HasContact;

        if (requireBothContacts)
            return left && right;

        return left || right;
    }

    private void ApplyImmediate(bool open)
    {
        isOpen = open;

        if (leftFingerRoot != null)
            leftFingerRoot.localRotation = Quaternion.Euler(open ? leftOpenEuler : leftClosedEuler);

        if (rightFingerRoot != null)
            rightFingerRoot.localRotation = Quaternion.Euler(open ? rightOpenEuler : rightClosedEuler);
    }

    private Vector3 GetSignedLocalEuler(Transform t)
    {
        Vector3 e = t.localEulerAngles;
        e.x = NormalizeAngle(e.x);
        e.y = NormalizeAngle(e.y);
        e.z = NormalizeAngle(e.z);
        return e;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}