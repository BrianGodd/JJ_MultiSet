using System;
using System.Collections;

public class LocalSpeechService : IAgentSpeechService
{
    public IEnumerator Speak(string text, UnityEngine.AudioSource audioSource, Action onSuccess, Action<string> onError)
    {
        try
        {
            var synthType = Type.GetType("System.Speech.Synthesis.SpeechSynthesizer, System.Speech");
            if (synthType == null)
            {
                onError?.Invoke("Local Windows speech synthesizer is not available.");
                yield break;
            }

            var synth = Activator.CreateInstance(synthType);
            var speakMethod = synthType.GetMethod("SpeakAsync", new[] { typeof(string) });
            if (speakMethod == null)
            {
                onError?.Invoke("SpeakAsync method is not available.");
                yield break;
            }

            speakMethod.Invoke(synth, new object[] { text });
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
        }
    }
}
