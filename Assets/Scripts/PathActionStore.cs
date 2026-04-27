using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum PathActionType
{
    None,
    Grip,
    Release
}

public class PathActionStore : MonoBehaviour
{
    public TextMeshProUGUI actionsText;

    [SerializeField]
    private List<PathActionType> actions = new List<PathActionType>();

    public void SyncCount(int count)
    {
        if (count < 0)
            count = 0;

        while (actions.Count < count)
            actions.Add(PathActionType.None);

        while (actions.Count > count)
            actions.RemoveAt(actions.Count - 1);

        RefreshText();
    }

    public PathActionType GetAction(int index)
    {
        if (index < 0 || index >= actions.Count)
            return PathActionType.None;

        return actions[index];
    }

    public void SetAction(int index, PathActionType action)
    {
        if (index < 0 || index >= actions.Count)
            return;

        actions[index] = action;
        RefreshText();
    }

    public void SetLastPointGrip()
    {
        if (actions.Count == 0)
            return;

        actions[actions.Count - 1] = PathActionType.Grip;
        RefreshText();
    }

    public void SetLastPointRelease()
    {
        if (actions.Count == 0)
            return;

        actions[actions.Count - 1] = PathActionType.Release;
        RefreshText();
    }

    public void ClearLastPointAction()
    {
        if (actions.Count == 0)
            return;

        actions[actions.Count - 1] = PathActionType.None;
        RefreshText();
    }

    private void RefreshText()
    {
        if (actionsText == null)
            return;

        if (actions.Count == 0)
        {
            actionsText.text = "Actions: (empty)";
            return;
        }

        string text = "Actions:\n";
        for (int i = 0; i < actions.Count; i++)
        {
            text += $"{i}: {actions[i]}\n";
        }

        actionsText.text = text;
    }
}