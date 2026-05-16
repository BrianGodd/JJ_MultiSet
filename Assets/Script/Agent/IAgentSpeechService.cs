using System;
using System.Collections;
using UnityEngine;

public interface IAgentSpeechService
{
    IEnumerator Speak(string text, AudioSource audioSource, Action onSuccess, Action<string> onError);
}
