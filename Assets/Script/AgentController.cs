using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

public class AgentController : MonoBehaviour
{
    [Header("References")]
    public EditorManager editorManager;

    [Header("API Settings")]
    [Tooltip("OpenAI API key (Bearer). Used for text generation and optionally TTS fallback.")]
    public string openAIApiKey = "";

    [Header("TTS Settings")]
    public string languageCode = "en-US";
    public string ttsVoice = "alloy";
    public int ttsSampleRate = 24000;

    [Header("Timing")]
    public float stableSeconds = 3f; // seconds message must be stable
    public float cooldownSeconds = 10f; // cooldown after handling

    [Header("Debug")]
    [Tooltip("When true, pressing Space will trigger a test generation/speak using TestMessage")]
    public bool enableSpaceTest = false;
    public string testMessage = "This is a test introduction for the nearest mark.";

    private string lastObserved = null;
    private float stableTimer = 0f;
    private float cooldownTimer = 0f;
    private string lastHandled = null;
    private string previousLabel = null;
    private EditorManager.Situation previousSituation = EditorManager.Situation.None;

    private AudioSource audioSource;

    private void Start()
    {
        if (editorManager == null) editorManager = FindObjectOfType<EditorManager>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void Update()
    {

        // Debug: trigger test with Space
        if (enableSpaceTest && Input.GetKeyDown(KeyCode.Space))
        {
            string prompt = BuildPrompt(testMessage, null, null, languageCode);
            StartCoroutine(GenerateAndSpeak(prompt));
        }

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        if (editorManager == null) return;
        if (editorManager.currentMode != EditorManager.EditorMode.Simulation)
        {
            lastObserved = null;
            stableTimer = 0f;
            return;
        }

        if (editorManager.simpleMSGText == null) return;

        // Trigger when nearest label changes or simulation situation changes
        string currentLabel = editorManager.nearestOutputText != null ? editorManager.nearestOutputText.text : null;
        var currentSituation = editorManager.currentSituation;

        if (currentSituation == EditorManager.Situation.None) return;

        bool labelChanged = !string.Equals(currentLabel, previousLabel, StringComparison.Ordinal);
        bool situationChanged = (currentSituation != previousSituation);
        // If label or situation changed, start/reset the stability timer and remember the observed state.
        if (labelChanged || situationChanged)
        {
            stableTimer = 0f;
            lastObserved = currentLabel;
            previousLabel = currentLabel;
            previousSituation = currentSituation;
            // Wait for the stable period before triggering.
            return;
        }

        // No change: increment stability timer.
        stableTimer += Time.deltaTime;

        // Only trigger after the label/situation has been stable for the configured time
        // and after cooldown. Also prevent repeating for the same state via lastHandled.
        if (stableTimer >= stableSeconds && cooldownTimer <= 0f)
        {
            string msg = editorManager.simpleMSGText.text ?? string.Empty;
            MarkStorage.MarkData md = null;
            if (!string.IsNullOrEmpty(currentLabel)) MarkStorage.TryGet(currentLabel, out md);

            // Use a combined key to track last handled label+situation
            string key = (currentLabel ?? "") + "|" + currentSituation.ToString();
            if (key != lastHandled)
            {
                lastHandled = key;
                cooldownTimer = cooldownSeconds;
                string prompt = BuildPrompt(msg, currentLabel, md, languageCode);
                StartCoroutine(GenerateAndSpeak(prompt));
            }
        }
    }

    private string BuildPrompt(string userMsg, string label, MarkStorage.MarkData md, string lang)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a helpful guide. The user message: '{Sanitize(userMsg)}'.");
        if (!string.IsNullOrEmpty(label))
        {
            sb.AppendLine($"Target Place: {Sanitize(label)}.");
            if (md != null)
            {
                if (!string.IsNullOrEmpty(md.keyword)) sb.AppendLine($"Keywords: {Sanitize(md.keyword)}.");
                if (!string.IsNullOrEmpty(md.details)) sb.AppendLine($"Details: {Sanitize(md.details)}.");
            }
        }
        sb.AppendLine($"Provide a guidance for user to know how to get to this place , and short introduce about the place. Keep it under 50 words. Be concise and friendly.");
        sb.AppendLine("Return only the text of the introduction (no extra metadata or quotes).");
        return sb.ToString();
    }

    private string Sanitize(string s)
    {
        return (s ?? string.Empty).Replace("\n", " ").Replace("\r", " ").Replace("\"", "'");
    }

    private IEnumerator GenerateAndSpeak(string prompt)
    {
        if (string.IsNullOrEmpty(openAIApiKey))
        {
            Debug.LogWarning("AgentController: openAIApiKey is empty; cannot call OpenAI APIs.");
            yield break;
        }

        // Build chat completion request (cost-conscious model)
        // build JSON manually to avoid JsonUtility limitations
        string chatJson = "{" +
            "\"model\":\"gpt-3.5-turbo\"," +
            "\"messages\":[{" +
                "\"role\":\"system\",\"content\":\"You are a concise guide. Provide a guide for user to know how to get to this place, and short intro <=50 words.\"" +
            "},{" +
                "\"role\":\"user\",\"content\":\"" + EscapeJson(prompt) + "\"" +
            "}] ," +
            "\"max_tokens\":200,\"temperature\":0.6" +
        "}";
        string chatUrl = "https://api.openai.com/v1/chat/completions";

        using (var uw = new UnityWebRequest(chatUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(chatJson);
            uw.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uw.downloadHandler = new DownloadHandlerBuffer();
            uw.SetRequestHeader("Content-Type", "application/json");
            uw.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

            yield return uw.SendWebRequest();

            if (uw.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"AgentController: OpenAI chat request failed: {uw.error} - {uw.downloadHandler.text}");
                yield break;
            }

            string resp = uw.downloadHandler.text;
            string genText = ParseOpenAIChatResponseForText(resp);
            if (string.IsNullOrEmpty(genText))
            {
                Debug.LogWarning("AgentController: could not parse OpenAI chat response for text.");
                yield break;
            }

            Debug.Log("AgentController: generated text: " + genText);

            // Prefer local (free) TTS when available to reduce cost. If available, speak and return.
            if (TrySpeakLocally(genText))
            {
                yield break;
            }

            // Fallback: use OpenAI TTS (request WAV) — cheaper than other cloud TTS in some cases depending on plan
            yield return StartCoroutine(SynthesizeAndPlay_OpenAI_TTS(genText));
        }
    }

    private string EscapeJson(string s)
    {
        if (s == null) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string ParseGeminiResponseForText(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            // Heuristics: try common fields in various response shapes.
            // 1) Look for "candidates" -> first candidate -> "content" or "text"
            int idx = json.IndexOf("\"candidates\"", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int cont = json.IndexOf("\"content\"", idx, StringComparison.OrdinalIgnoreCase);
                if (cont >= 0)
                {
                    int q = json.IndexOf('"', cont + 9);
                    if (q >= 0)
                    {
                        int q2 = json.IndexOf('"', q + 1);
                        if (q2 > q) return json.Substring(q + 1, q2 - q - 1);
                    }
                }
                int t = json.IndexOf("\"text\"", idx, StringComparison.OrdinalIgnoreCase);
                if (t >= 0)
                {
                    int colon = json.IndexOf(':', t);
                    int firstQuote = json.IndexOf('"', colon + 1);
                    if (firstQuote >= 0)
                    {
                        int secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (secondQuote > firstQuote) return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                    }
                }
            }

            // 2) Look for "output" blocks
            idx = json.IndexOf("\"output\"", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int t = json.IndexOf("\"content\"", idx, StringComparison.OrdinalIgnoreCase);
                if (t >= 0)
                {
                    int q = json.IndexOf('"', t + 9);
                    if (q >= 0)
                    {
                        int q2 = json.IndexOf('"', q + 1);
                        if (q2 > q) return json.Substring(q + 1, q2 - q - 1);
                    }
                }
            }

            // 3) Generic: first occurrence of "text": "..."
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
            Debug.LogWarning("AgentController ParseGeminiResponseForText exception: " + ex.Message);
        }

        return null;
    }
    private string ParseOpenAIChatResponseForText(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            // Look for choices[0].message.content
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

            // fallback: find first "text": "..."
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
            Debug.LogWarning("AgentController ParseOpenAIChatResponseForText exception: " + ex.Message);
        }
        return null;
    }

    // Try to speak locally using Windows SAPI via reflection (no extra cloud cost).
    // Returns true if spoken (or queued) successfully.
    private bool TrySpeakLocally(string text)
    {
        try
        {
            // Attempt to load System.Speech.Synthesis.SpeechSynthesizer
            var synthType = Type.GetType("System.Speech.Synthesis.SpeechSynthesizer, System.Speech");
            if (synthType == null) return false;

            var synth = Activator.CreateInstance(synthType);
            var speakMethod = synthType.GetMethod("SpeakAsync", new Type[] { typeof(string) });
            if (speakMethod != null)
            {
                speakMethod.Invoke(synth, new object[] { text });
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.Log("AgentController: local TTS not available: " + ex.Message);
        }
        return false;
    }

    // Fallback: call OpenAI TTS endpoint to request WAV audio and play it.
    private IEnumerator SynthesizeAndPlay_OpenAI_TTS(string text)
    {
        Debug.Log("AgentController: using OpenAI TTS fallback (may incur cost).");
        
        string url = "https://api.openai.com/v1/audio/speech"; // endpoint may vary by OpenAI account

        // Try a simple JSON body for text->speech (model and voice may vary)
        // Build TTS request JSON. Note: OpenAI TTS endpoint shape may differ; adjust if needed.
        string json = "{" +
            "\"model\":\"gpt-4o-mini-tts\"," +
            "\"voice\":\"" + EscapeJson(ttsVoice) + "\"," +
            "\"input\":\"" + EscapeJson(text) + "\"," +
            "\"format\":\"mp3\"" +
        "}";

        using (var uw = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            uw.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uw.downloadHandler = new DownloadHandlerBuffer();
            uw.SetRequestHeader("Content-Type", "application/json");
            uw.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

            yield return uw.SendWebRequest();
            Debug.Log("Status Code: " + uw.responseCode);
            Debug.Log("Content-Type: " + uw.GetResponseHeader("Content-Type"));

            if (uw.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"AgentController: OpenAI TTS failed: {uw.error} - {uw.downloadHandler.text}");
                yield break;
            }

            byte[] respBytes = uw.downloadHandler.data;
            if (respBytes == null || respBytes.Length == 0)
            {
                Debug.LogWarning("AgentController: OpenAI TTS returned empty audio.");
                yield break;
            }

            string contentType = uw.GetResponseHeader("Content-Type") ?? string.Empty;
            byte[] audioBytes = null;

            if (contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // JSON response likely contains base64-encoded audio in a field
                string bodyText = uw.downloadHandler.text;
                string b64 = ExtractJsonValue(bodyText, "audio") ?? ExtractJsonValue(bodyText, "audioContent") ?? ExtractJsonValue(bodyText, "audio_data") ?? ExtractJsonValue(bodyText, "data");
                if (string.IsNullOrEmpty(b64))
                {
                    Debug.LogWarning("AgentController: OpenAI TTS JSON response did not contain base64 audio field.");
                    yield break;
                }
                try { audioBytes = Convert.FromBase64String(b64); }
                catch (Exception ex) { Debug.LogWarning("AgentController: failed to decode base64 audio: " + ex.Message); yield break; }
            }
            else
            {
                // assume raw mp3 bytes
                audioBytes = respBytes;
            }

            if (audioBytes == null || audioBytes.Length == 0)
            {
                Debug.LogWarning("AgentController: no audio bytes available after parsing TTS response.");
                yield break;
            }

            // write to temp file and load via UnityWebRequestMultimedia
            string fileName = "agent_tts_" + Guid.NewGuid().ToString("N") + ".mp3";
            string tmpPath = Path.Combine(Application.temporaryCachePath, fileName);
            try
            {
                File.WriteAllBytes(tmpPath, audioBytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("AgentController: failed to write temp mp3 file: " + ex.Message);
                yield break;
            }

            using (var uw2 = UnityWebRequestMultimedia.GetAudioClip("file://" + tmpPath, AudioType.MPEG))
            {
                yield return uw2.SendWebRequest();
                if (uw2.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"AgentController: failed to load mp3 from temp file: {uw2.error}");
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(uw2);
                    if (clip != null)
                    {
                        audioSource.Stop();
                        audioSource.clip = clip;
                        audioSource.Play();
                        Debug.Log("AgentController: playing TTS audio from OpenAI (mp3).");
                    }
                }
            }

            try { File.Delete(tmpPath); } catch { }
        }
    }

    // Minimal WAV parser to AudioClip (supports PCM16)
    private AudioClip WAVToAudioClip(byte[] wavBytes, string name)
    {
        try
        {
            using (var ms = new MemoryStream(wavBytes))
            using (var br = new BinaryReader(ms))
            {
                // read 4-byte ASCII chunk IDs safely
                Func<int, string> ReadId = (n) =>
                {
                    var b = br.ReadBytes(n);
                    return System.Text.Encoding.ASCII.GetString(b);
                };

                string riff = ReadId(4);
                if (riff != "RIFF") return null;
                br.ReadInt32(); // file size
                string wave = ReadId(4);
                if (wave != "WAVE") return null;

                // Read chunks until we find 'fmt ' chunk
                string chunkId = ReadId(4);
                int chunkSize = br.ReadInt32();
                while (chunkId != "fmt " )
                {
                    // skip this chunk
                    br.ReadBytes(chunkSize);
                    if (ms.Position >= ms.Length) return null;
                    chunkId = ReadId(4);
                    chunkSize = br.ReadInt32();
                }

                int fmtLen = chunkSize;
                int audioFormat = br.ReadInt16();
                int channels = br.ReadInt16();
                int sampleRate = br.ReadInt32();
                br.ReadInt32(); // byte rate
                br.ReadInt16(); // block align
                int bitsPerSample = br.ReadInt16();
                if (fmtLen > 16) br.ReadBytes(fmtLen - 16);

                // Now find the data chunk
                string dataTag = ReadId(4);
                int dataChunkSize = br.ReadInt32();
                while (dataTag != "data")
                {
                    // skip unknown chunk
                    br.ReadBytes(dataChunkSize);
                    if (ms.Position >= ms.Length) return null;
                    dataTag = ReadId(4);
                    dataChunkSize = br.ReadInt32();
                }

                int dataSize = dataChunkSize;
                byte[] data = br.ReadBytes(dataSize);

                if (bitsPerSample != 16) { Debug.LogWarning("WAV bits per sample not 16-bit."); return null; }

                int samples = data.Length / 2 / channels;
                float[] floatData = new float[samples * channels];
                int idx = 0;
                for (int i = 0; i < data.Length; i += 2)
                {
                    short s = BitConverter.ToInt16(data, i);
                    floatData[idx++] = s / 32768f;
                }

                AudioClip clip = AudioClip.Create(name, samples, channels, sampleRate, false);
                clip.SetData(floatData, 0);
                return clip;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AgentController WAV parse failed: " + ex.Message);
            return null;
        }
    }

    private string ExtractJsonValue(string json, string key)
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
            Debug.LogWarning("AgentController ExtractJsonValue: " + ex.Message);
            return null;
        }
    }

    private AudioClip PCM16ToAudioClip(byte[] pcm16, int sampleRate, string name)
    {
        if (pcm16 == null || pcm16.Length < 2) return null;
        int samples = pcm16.Length / 2;
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short s = BitConverter.ToInt16(pcm16, i * 2);
            data[i] = s / 32768f;
        }
        AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
