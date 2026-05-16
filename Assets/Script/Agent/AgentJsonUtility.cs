using System;

public static class AgentJsonUtility
{
    public static string EscapeJson(string s)
    {
        if (s == null) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    public static string ParseOpenAIChatResponseForText(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            int idx = json.IndexOf("\"choices\"", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int msg = json.IndexOf("\"message\"", idx, StringComparison.OrdinalIgnoreCase);
                if (msg >= 0)
                {
                    int cont = json.IndexOf("\"content\"", msg, StringComparison.OrdinalIgnoreCase);
                    if (cont >= 0)
                    {
                        int firstQuote = json.IndexOf('"', cont + 9);
                        if (firstQuote >= 0)
                        {
                            int secondQuote = json.IndexOf('"', firstQuote + 1);
                            if (secondQuote > firstQuote) return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        }
                    }
                }
            }

            int idxText = json.IndexOf("\"text\"", StringComparison.OrdinalIgnoreCase);
            if (idxText >= 0)
            {
                int colon = json.IndexOf(':', idxText);
                int firstQuote = json.IndexOf('"', colon + 1);
                if (firstQuote >= 0)
                {
                    int secondQuote = json.IndexOf('"', firstQuote + 1);
                    if (secondQuote > firstQuote) return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("AgentJsonUtility ParseOpenAIChatResponseForText exception: " + ex.Message);
        }

        return null;
    }

    public static string ExtractJsonValue(string json, string key)
    {
        try
        {
            string keyToken = $"\"{key}\"";
            int idx = json.IndexOf(keyToken, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + keyToken.Length);
            if (colon < 0) return null;
            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0) return null;
            int end = json.IndexOf('"', firstQuote + 1);
            if (end < firstQuote) return null;
            string val = json.Substring(firstQuote + 1, end - firstQuote - 1);
            return val.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("AgentJsonUtility ExtractJsonValue: " + ex.Message);
            return null;
        }
    }
}
