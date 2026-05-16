using System;
using UnityEngine;

[Serializable]
public class AgentApiSettings
{
    [Header("Authentication")]
    [Tooltip("Bearer token or provider API key.")]
    public string apiKey = "";

    [Header("Text Generation")]
    public string textModel = "gpt-3.5-turbo";
    public string textBaseUrl = "https://api.openai.com/v1/chat/completions";
    [TextArea(2, 4)]
    public string systemPrompt = "You are a concise guide. Provide a guide for user to know how to get to this place, and short intro <=50 words.";
    public int maxTokens = 200;
    [Range(0f, 2f)]
    public float temperature = 0.6f;

    [Header("Speech")]
    public string languageCode = "en-US";
    public string speechModel = "gpt-4o-mini-tts";
    public string speechBaseUrl = "https://api.openai.com/v1/audio/speech";
    public string voice = "alloy";
    public string audioFormat = "mp3";
    public int sampleRate = 24000;
}
