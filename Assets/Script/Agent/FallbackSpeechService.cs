using System;
using System.Collections;
using UnityEngine;

public class FallbackSpeechService : IAgentSpeechService
{
    private readonly IAgentSpeechService primary;
    private readonly IAgentSpeechService fallback;

    public FallbackSpeechService(IAgentSpeechService primary, IAgentSpeechService fallback)
    {
        this.primary = primary;
        this.fallback = fallback;
    }

    public IEnumerator Speak(string text, AudioSource audioSource, Action onSuccess, Action<string> onError)
    {
        bool primarySucceeded = false;
        string primaryError = null;

        if (primary != null)
        {
            yield return primary.Speak(
                text,
                audioSource,
                () =>
                {
                    primarySucceeded = true;
                    onSuccess?.Invoke();
                },
                error => primaryError = error);
        }

        if (primarySucceeded) yield break;

        if (fallback == null)
        {
            onError?.Invoke(primaryError ?? "Primary speech service was unavailable.");
            yield break;
        }

        yield return fallback.Speak(text, audioSource, onSuccess, error => onError?.Invoke(primaryError ?? error));
    }
}
