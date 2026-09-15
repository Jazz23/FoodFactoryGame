// Sends card-level pointer scrolling to the active scroll view inside that card.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class UiScrollRelay : MonoBehaviour, IScrollHandler
{
    private Func<ScrollRect?> targetResolver = null!;

    public void Initialize(Func<ScrollRect?> resolver)
    {
        targetResolver = resolver;
    }

    public void OnScroll(PointerEventData eventData)
    {
        var target = targetResolver();
        if (target is not null && target.isActiveAndEnabled)
        {
            target.OnScroll(eventData);
        }
    }
}
