using System.Collections.Generic;
using UnityEngine;

public class GripperContactSensor : MonoBehaviour
{
    [Header("Optional Filter")]
    public string requiredTag = "";
    public bool ignoreTriggers = true;

    private readonly HashSet<Collider> contacts = new HashSet<Collider>();

    public bool HasContact => contacts.Count > 0;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsValid(other))
            return;

        contacts.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (contacts.Contains(other))
            contacts.Remove(other);
    }

    public void ClearContacts()
    {
        contacts.Clear();
    }

    private bool IsValid(Collider other)
    {
        if (other == null)
            return false;

        if (ignoreTriggers && other.isTrigger)
            return false;

        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            return false;

        return true;
    }
}