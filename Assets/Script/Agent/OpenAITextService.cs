using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAITextService : IAgentTextService
{
    private readonly AgentApiSettings settings;

    public OpenAITextService(AgentApiSettings settings)
    {
        this.settings = settings ?? new AgentApiSettings();
    }

    public IEnumerator GenerateText(string prompt, Action<string> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(settings.apiKey))
        {
            onError?.Invoke("API key is empty.");
            yield break;
        }

        string chatJson = "{" +
            "\"model\":\"" + AgentJsonUtility.EscapeJson(settings.textModel) + "\"," +
            "\"messages\":[{" +
                "\"role\":\"system\",\"content\":\"" + AgentJsonUtility.EscapeJson(settings.systemPrompt) + "\"" +
            "},{" +
                "\"role\":\"user\",\"content\":\"" + AgentJsonUtility.EscapeJson(prompt) + "\"" +
            "}]," +
            "\"max_tokens\":" + settings.maxTokens + "," +
            "\"temperature\":" + settings.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "}";

        using (var uw = new UnityWebRequest(settings.textBaseUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(chatJson);
            uw.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uw.downloadHandler = new DownloadHandlerBuffer();
            uw.SetRequestHeader("Content-Type", "application/json");
            uw.SetRequestHeader("Authorization", $"Bearer {settings.apiKey}");

            yield return uw.SendWebRequest();

            if (uw.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{uw.error} - {uw.downloadHandler.text}");
                yield break;
            }

            string genText = AgentJsonUtility.ParseOpenAIChatResponseForText(uw.downloadHandler.text);
            if (string.IsNullOrEmpty(genText))
            {
                onError?.Invoke("Could not parse response text.");
                yield break;
            }

            onSuccess?.Invoke(genText);
        }
    }
}
