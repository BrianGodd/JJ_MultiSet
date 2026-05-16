using System;
using System.Collections;

public interface IAgentTextService
{
    IEnumerator GenerateText(string prompt, Action<string> onSuccess, Action<string> onError);
}
