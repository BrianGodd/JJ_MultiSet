using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAITtsSpeechService : IAgentSpeechService
{
    private readonly AgentApiSettings settings;

    public OpenAITtsSpeechService(AgentApiSettings settings)
    {
        this.settings = settings ?? new AgentApiSettings();
    }

    public IEnumerator Speak(string text, AudioSource audioSource, Action onSuccess, Action<string> onError)
    {
        if (audioSource == null)
        {
            onError?.Invoke("AudioSource is missing.");
            yield break;
        }

        if (string.IsNullOrEmpty(settings.apiKey))
        {
            onError?.Invoke("API key is empty.");
            yield break;
        }

        string json = "{" +
            "\"model\":\"" + AgentJsonUtility.EscapeJson(settings.speechModel) + "\"," +
            "\"voice\":\"" + AgentJsonUtility.EscapeJson(settings.voice) + "\"," +
            "\"input\":\"" + AgentJsonUtility.EscapeJson(text) + "\"," +
            "\"format\":\"" + AgentJsonUtility.EscapeJson(settings.audioFormat) + "\"" +
        "}";

        using (var uw = new UnityWebRequest(settings.speechBaseUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
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

            byte[] audioBytes = ResolveAudioBytes(uw);
            if (audioBytes == null || audioBytes.Length == 0)
            {
                onError?.Invoke("No audio bytes available after parsing TTS response.");
                yield break;
            }

            string extension = string.Equals(settings.audioFormat, "wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3";
            string tmpPath = Path.Combine(Application.temporaryCachePath, "agent_tts_" + Guid.NewGuid().ToString("N") + extension);

            try
            {
                File.WriteAllBytes(tmpPath, audioBytes);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to write temp audio file: " + ex.Message);
                yield break;
            }

            AudioType audioType = string.Equals(settings.audioFormat, "wav", StringComparison.OrdinalIgnoreCase)
                ? AudioType.WAV
                : AudioType.MPEG;

            using (var uw2 = UnityWebRequestMultimedia.GetAudioClip("file://" + tmpPath, audioType))
            {
                yield return uw2.SendWebRequest();

                try { File.Delete(tmpPath); } catch { }

                if (uw2.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Failed to load audio clip: " + uw2.error);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(uw2);
                if (clip == null)
                {
                    onError?.Invoke("Loaded audio clip was null.");
                    yield break;
                }

                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.Play();
                onSuccess?.Invoke();
            }
        }
    }

    private byte[] ResolveAudioBytes(UnityWebRequest request)
    {
        string contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;
        if (contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return request.downloadHandler.data;
        }

        string bodyText = request.downloadHandler.text;
        string b64 = AgentJsonUtility.ExtractJsonValue(bodyText, "audio")
            ?? AgentJsonUtility.ExtractJsonValue(bodyText, "audioContent")
            ?? AgentJsonUtility.ExtractJsonValue(bodyText, "audio_data")
            ?? AgentJsonUtility.ExtractJsonValue(bodyText, "data");

        if (string.IsNullOrEmpty(b64)) return null;

        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("OpenAITtsSpeechService: failed to decode base64 audio: " + ex.Message);
            return null;
        }
    }
}
