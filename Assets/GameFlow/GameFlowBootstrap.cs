using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the story flow run even in a scene where nobody has added a GameFlowManager yet:
/// when a scene loads that looks like the game scene (it has a player and the melody puzzle)
/// and has no flow manager, one is created. The manager auto-resolves everything else
/// (HUD, dialogue, video, life support, checkpoints, a dummy obelisk).
///
/// Run Tools ▸ Eschropita ▸ Set Up Game Flow In Scene to persist a configurable copy in the scene;
/// when one exists this bootstrap does nothing.
/// </summary>
public static class GameFlowBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureFlow();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureFlow();

    static void EnsureFlow()
    {
        if (Object.FindAnyObjectByType<GameFlowManager>() != null) return;
        if (Object.FindAnyObjectByType<PlayerController>() == null) return;
        if (Object.FindAnyObjectByType<MartianSequencePuzzle>() == null) return;

        var go = new GameObject("GameFlow (auto)");
        go.AddComponent<GameFlowManager>();
        Debug.Log("[GameFlow] No GameFlowManager in the scene: created one at runtime. " +
                  "Use Tools ▸ Eschropita ▸ Set Up Game Flow In Scene to add a configurable one.");
    }
}
