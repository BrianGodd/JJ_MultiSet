using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EventController : MonoBehaviour
{
    public UnityEvent UnityEvent;
    public UnityEvent UnityEventSec;
    public UnityEvent UnityEventWithTime;

    public void HindObj()
    {
        this.gameObject.SetActive(false);
    }

    public void DestroyObj()
    {
        Destroy(this.gameObject);
    }

    public void SwitchObj()
    {
        this.gameObject.SetActive(!this.gameObject.active);
    }

    public void ParentAnimActive(string wh)
    {
        this.transform.parent.GetComponent<Animator>().SetBool(wh, !this.transform.parent.GetComponent<Animator>().GetBool(wh));
    }

    public void ActiveCustomEvent()
    {
        UnityEvent?.Invoke();
    }

    public void ActiveCustomEvent2()
    {
        UnityEventSec?.Invoke();
    }

    public void WaitTimeEvent(float time)
    {
        StartCoroutine(TimeCustomEvent(time));
    }

    IEnumerator TimeCustomEvent(float time)
    {
        yield return new WaitForSeconds(time);

        UnityEventWithTime?.Invoke();
    }
}
