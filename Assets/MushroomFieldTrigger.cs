using UnityEngine;

public class MushroomFieldTrigger : MonoBehaviour
{
    public MartianSequencePuzzle puzzle;

    private bool triggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;

        if (other.CompareTag("Player"))
        {
            var player = other.GetComponent<PlayerController>();
            if (player != null && player.HasFollower)
            {
                triggered = true;

                if (puzzle != null)
                {
                    puzzle.StartPuzzle();
                    Debug.Log("Entered mushroom field WITH a Martian → starting puzzle.");
                }
            }
        }
    }
}
