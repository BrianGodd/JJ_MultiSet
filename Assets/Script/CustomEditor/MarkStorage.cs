using UnityEngine;
using System.Collections.Generic;

public class MarkStorage : MonoBehaviour
{
    // Data structure to hold mark information
    public class MarkData
    {
        public string label;
        public Vector3 position;
        public Vector3 scale;
        public float margin;
        public float angle1;
        public float angle2;
        public string keyword;
        public string details;
    }

    // In-memory storage for marks, keyed by label
    public static Dictionary<string, MarkData> Marks { get; } = new Dictionary<string, MarkData>();

    public static void Save(string label, MarkData data)
    {
        if (string.IsNullOrEmpty(label) || data == null) return;
        Marks[label] = data;
    }

    public static void Remove(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        Marks.Remove(label);
    }

    public static bool TryGet(string label, out MarkData data)
    {
        return Marks.TryGetValue(label, out data);
    }

    public static void Clear()
    {
        Marks.Clear();
    }
}