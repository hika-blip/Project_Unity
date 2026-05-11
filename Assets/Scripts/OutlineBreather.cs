using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Outline))]
public class OutlineBreather : MonoBehaviour
{
    public enum BreathPhase
    {
        Expand,     // 細 → 太
        Contract,   // 太 → 細
        HoldThin    // 細のまま維持
    }

    [Header("基本設定")]
    public bool enableBreath = true;

    [Tooltip("最小アウトライン幅")]
    public float minWidth = 0.5f;

    [Tooltip("最大アウトライン幅")]
    public float maxWidth = 2.5f;

    [Header("時間設定")]
    [Tooltip("細 → 太 にかかる時間")]
    public float expandTime = 0.6f;

    [Tooltip("太 → 細 にかかる時間")]
    public float contractTime = 0.6f;

    [Tooltip("細いまま維持する時間")]
    public float holdThinTime = 1.0f;

    private Outline outline;
    private BreathPhase phase;
    private float phaseTimer;

    void Awake()
    {
        outline = GetComponent<Outline>();
    }

    void OnEnable()
    {
        phase = BreathPhase.Expand;
        phaseTimer = 0f;
        outline.OutlineWidth = minWidth;
    }

    void Update()
    {
        if (!enableBreath || outline == null)
            return;

        phaseTimer += Time.deltaTime;

        switch (phase)
        {
            case BreathPhase.Expand:
                {
                    float t = Mathf.Clamp01(phaseTimer / expandTime);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    outline.OutlineWidth = Mathf.Lerp(minWidth, maxWidth, t);

                    if (t >= 1f)
                        NextPhase(BreathPhase.Contract);
                    break;
                }

            case BreathPhase.Contract:
                {
                    float t = Mathf.Clamp01(phaseTimer / contractTime);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    outline.OutlineWidth = Mathf.Lerp(maxWidth, minWidth, t);

                    if (t >= 1f)
                        NextPhase(BreathPhase.HoldThin);
                    break;
                }

            case BreathPhase.HoldThin:
                {
                    outline.OutlineWidth = minWidth;

                    if (phaseTimer >= holdThinTime)
                        NextPhase(BreathPhase.Expand);
                    break;
                }
        }
    }

    void NextPhase(BreathPhase next)
    {
        phase = next;
        phaseTimer = 0f;
    }

    public void StopBreath()
    {
        enableBreath = false;
        outline.OutlineWidth = minWidth;
    }

    public void StartBreath()
    {
        enableBreath = true;
        phase = BreathPhase.Expand;
        phaseTimer = 0f;
    }
}
