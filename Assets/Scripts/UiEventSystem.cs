using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Guarantees the scene contains EXACTLY ONE EventSystem.
///
/// WHY THE FIRST VERSION MADE THINGS WORSE:
///   Object.FindObjectsByType() SKIPS objects with HideFlags.DontSave.
///   The first version created the EventSystem with HideFlags.DontSave, so the
///   next script to call EnsureExactlyOne() could not see it, concluded there
///   were zero EventSystems, and created another one. Three scripts -> three
///   EventSystems, none of them able to find each other.
///
///   Two fixes live here:
///     1. Resources.FindObjectsOfTypeAll<EventSystem>() sees EVERY EventSystem,
///        including DontSave ones, so nothing is ever invisible to the search.
///     2. Extras are deactivated IMMEDIATELY (SetActive(false)) before being
///        destroyed, so they stop running Update() and stop logging the
///        "2 event systems" warning on the very same frame. Object.Destroy is
///        deferred to end of frame, so deactivating first is what actually
///        silences the spam immediately.
///
///   Extras are destroyed by destroying the EventSystem COMPONENT, not the
///   GameObject, so nothing else parented to that GameObject is lost.
///
/// Call EnsureExactlyOne() from every script that touches the UI.
/// </summary>
public static class UiEventSystem
{
    public static void EnsureExactlyOne()
    {
        // FindObjectsByType skips DontSave objects - this one does not.
        EventSystem[] all = Resources.FindObjectsOfTypeAll<EventSystem>();

        EventSystem keep = null;

        // Prefer a real scene object over a runtime-generated one, so the
        // EventSystem the player placed in the scene is the survivor.
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (!all[i].gameObject.scene.IsValid()) continue; // skip prefab assets
            if (all[i].hideFlags.HasFlag(HideFlags.DontSave)) continue;
            keep = all[i];
            break;
        }

        // Otherwise fall back to any EventSystem we can find.
        if (keep == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!all[i].gameObject.scene.IsValid()) continue;
                keep = all[i];
                break;
            }
        }

        // Otherwise make one.
        if (keep == null)
        {
            GameObject go = new GameObject("EventSystem");
            keep = go.AddComponent<EventSystem>();
        }

        // Every EventSystem needs an input module to drive it.
        if (keep.GetComponent<BaseInputModule>() == null)
            keep.gameObject.AddComponent<StandaloneInputModule>();

        // Deactivate every other EventSystem immediately, then destroy the
        // component. Destroying the component (not the GameObject) leaves
        // anything else on that GameObject intact.
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (all[i] == keep) continue;
            if (!all[i].gameObject.scene.IsValid()) continue;

            all[i].gameObject.SetActive(false);
            Object.Destroy(all[i]);
        }
    }
}
