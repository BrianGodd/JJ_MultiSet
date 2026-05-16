using System;
using System.Collections;
using System.Text;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    [Header("References")]
    public EditorManager editorManager;

    [Header("Agent Settings")]
    public AgentApiSettings apiSettings = new AgentApiSettings();

    [Header("Timing")]
    public float stableSeconds = 3f; // seconds message must be stable
    public float cooldownSeconds = 10f; // cooldown after handling

    [Header("Debug")]
    [Tooltip("When true, pressing Space will trigger a test generation/speak using TestMessage")]
    public bool enableSpaceTest = false;
    public string testMessage = "This is a test introduction for the nearest mark.";

    private float stableTimer = 0f;
    private float cooldownTimer = 0f;
    private string lastHandled = null;
    private string previousLabel = null;
    private EditorManager.Situation previousSituation = EditorManager.Situation.None;

    private AudioSource audioSource;
    private IAgentTextService textService;
    private IAgentSpeechService speechService;

    private void Start()
    {
        if (editorManager == null) editorManager = FindObjectOfType<EditorManager>();
        if (apiSettings == null) apiSettings = new AgentApiSettings();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        InitializeServices();
    }

    private void Update()
    {
        if (enableSpaceTest && Input.GetKeyDown(KeyCode.Space))
        {
            string prompt = BuildPrompt(testMessage, null, null, apiSettings.languageCode);
            StartCoroutine(GenerateAndSpeak(prompt));
        }

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        if (editorManager == null) return;
        if (editorManager.currentMode != EditorManager.EditorMode.Simulation)
        {
            stableTimer = 0f;
            return;
        }

        if (editorManager.simpleMSGText == null) return;

        string currentLabel = editorManager.nearestOutputText != null ? editorManager.nearestOutputText.text : null;
        var currentSituation = editorManager.currentSituation;

        if (currentSituation == EditorManager.Situation.None) return;

        bool labelChanged = !string.Equals(currentLabel, previousLabel, StringComparison.Ordinal);
        bool situationChanged = currentSituation != previousSituation;
        if (labelChanged || situationChanged)
        {
            stableTimer = 0f;
            previousLabel = currentLabel;
            previousSituation = currentSituation;
            return;
        }

        stableTimer += Time.deltaTime;

        if (stableTimer >= stableSeconds && cooldownTimer <= 0f)
        {
            string msg = editorManager.simpleMSGText.text ?? string.Empty;
            MarkStorage.MarkData md = null;
            if (!string.IsNullOrEmpty(currentLabel)) MarkStorage.TryGet(currentLabel, out md);

            string key = (currentLabel ?? "") + "|" + currentSituation;
            if (key != lastHandled)
            {
                lastHandled = key;
                cooldownTimer = cooldownSeconds;
                string prompt = BuildPrompt(msg, currentLabel, md, apiSettings.languageCode);
                StartCoroutine(GenerateAndSpeak(prompt));
            }
        }
    }

    private void InitializeServices()
    {
        textService = new OpenAITextService(apiSettings);
        speechService = new FallbackSpeechService(
            new LocalSpeechService(),
            new OpenAITtsSpeechService(apiSettings));
    }

    private string BuildPrompt(string userMsg, string label, MarkStorage.MarkData md, string lang)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a helpful guide. The user message: '{Sanitize(userMsg)}'.");
        if (!string.IsNullOrEmpty(lang))
        {
            sb.AppendLine($"Preferred language code: {Sanitize(lang)}.");
        }

        if (!string.IsNullOrEmpty(label))
        {
            sb.AppendLine($"Target Place: {Sanitize(label)}.");
            if (md != null)
            {
                if (!string.IsNullOrEmpty(md.keyword)) sb.AppendLine($"Keywords: {Sanitize(md.keyword)}.");
                if (!string.IsNullOrEmpty(md.details)) sb.AppendLine($"Details: {Sanitize(md.details)}.");
            }
        }

        sb.AppendLine("Provide a guidance for user to know how to get to this place, and short introduce about the place. Keep it under 50 words. Be concise and friendly.");
        sb.AppendLine("Return only the text of the introduction (no extra metadata or quotes).");
        return sb.ToString();
    }

    private string Sanitize(string s)
    {
        return (s ?? string.Empty).Replace("\n", " ").Replace("\r", " ").Replace("\"", "'");
    }

    private IEnumerator GenerateAndSpeak(string prompt)
    {
        if (textService == null || speechService == null)
        {
            InitializeServices();
        }

        string generatedText = null;
        string generationError = null;

        yield return textService.GenerateText(
            prompt,
            text => generatedText = text,
            error => generationError = error);

        if (!string.IsNullOrEmpty(generationError))
        {
            Debug.LogWarning("AgentController: text generation failed: " + generationError);
            yield break;
        }

        if (string.IsNullOrEmpty(generatedText))
        {
            Debug.LogWarning("AgentController: generated text was empty.");
            yield break;
        }

        Debug.Log("AgentController: generated text: " + generatedText);

        string speechError = null;
        yield return speechService.Speak(
            generatedText,
            audioSource,
            null,
            error => speechError = error);

        if (!string.IsNullOrEmpty(speechError))
        {
            Debug.LogWarning("AgentController: speech failed: " + speechError);
        }
    }
}
