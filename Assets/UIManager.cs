using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("Assign the FILL Images (Image Type = Filled)")]
    [SerializeField] private Image daffodilEnergyFill;
    [SerializeField] private Image windmillHealthFill;

    [Header("Drag & drop refs here")]
    [SerializeField] private FollowPlayer daffodil; // your daffodil script
    [SerializeField] private TaskSite windMill;      // your windmill TaskSite

    void Start()
    {
        // Initialize once
        UpdateDaffodilUI();
        UpdateWindmillUI();
    }

    void Update()
    {
        // Keep UI synced
        UpdateDaffodilUI();
        UpdateWindmillUI();
    }

    private void UpdateDaffodilUI()
    {
        if (daffodilEnergyFill == null || daffodil == null) return;

        float energy = daffodil.energy;
        float max = daffodil.maxEnergy;

        daffodilEnergyFill.fillAmount = Safe01(energy, max);
    }

    private void UpdateWindmillUI()
    {
        if (windmillHealthFill == null || windMill == null) return;

        float health = windMill.health;
        float max = windMill.maxHealth;

        windmillHealthFill.fillAmount = Safe01(health, max);
    }

    private float Safe01(float value, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(value / max);
    }
}
