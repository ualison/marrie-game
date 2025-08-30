using System;
using System.Collections.Generic;

/// <summary>
/// Helper to track event unsubscription actions for a node's lifetime.
/// Usage: after subscribing to an event, call AddUnsubscriber(() => SomeEvent -= Handler);
/// Then call UnsubscribeAll() in _ExitTree to ensure cleanup.
/// </summary>
public class EventSubscriptionHelper
{
    private readonly List<Action> _unsubscribers = new();

    public void AddUnsubscriber(Action unsubscribe)
    {
        if (unsubscribe == null) return;
        _unsubscribers.Add(unsubscribe);
    }

    public void UnsubscribeAll()
    {
        foreach (var unsub in _unsubscribers)
        {
            try
            {
                unsub();
            }
            catch (Exception)
            {
                // Swallow exceptions from unsubscribe to avoid breaking teardown
            }
        }
        _unsubscribers.Clear();
    }
}
