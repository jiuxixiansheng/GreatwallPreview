using System.Collections;
using UnityEngine;

public class SandStormTransition : MonoBehaviour
{
    [Header("Material")]
    [SerializeField] private Material sandStormMaterial;

    [Header("Transition Timing")]
    [SerializeField] public float sandFadeInDuration = 1.0f;
    [SerializeField] public float sandHoldDuration = 2.0f;
    [SerializeField] public float sandFadeOutDuration = 1.0f;

    [Header("Fade Feel")]
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Audio")]
    [SerializeField] private AudioSource sandStormAudioSource;
    [SerializeField] private AudioClip sandStormAudioClip;
    [SerializeField] private float sandStormMaxVolume = 1.0f;
    [SerializeField] private bool loopSandStormAudio = true;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private Coroutine sandStormRoutine;

    private void OnEnable()
    {
        EventDispatcher.Instance.AddListener("zhuanchang", PlaySandStormTransition);
    }

    private void OnDisable()
    {
        EventDispatcher.Instance.RemoveListener("zhuanchang", PlaySandStormTransition);

        if (sandStormRoutine != null)
        {
            StopCoroutine(sandStormRoutine);
            sandStormRoutine = null;
        }

        SetSandFade(0f);
        SetSandAudioVolume(0f);
        StopSandAudio();
    }

    private void Awake()
    {
        SetSandFade(0f);
        SetSandAudioVolume(0f);
    }

    public void PlaySandStormTransition()
    {
        if (sandStormRoutine != null)
            StopCoroutine(sandStormRoutine);

        sandStormRoutine = StartCoroutine(PlaySandStormTransitionRoutine());
    }

    private IEnumerator PlaySandStormTransitionRoutine()
    {
        PlaySandAudio();

        yield return FadeSandStorm(0f, 1f, sandFadeInDuration);

        yield return Wait(sandHoldDuration);

        yield return FadeSandStorm(1f, 0f, sandFadeOutDuration);

        SetSandFade(0f);
        SetSandAudioVolume(0f);
        StopSandAudio();

        sandStormRoutine = null;
    }

    private IEnumerator FadeSandStorm(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetSandFade(to);
            SetSandAudioVolume(to);
            yield break;
        }

        float timer = 0f;

        while (timer < duration)
        {
            timer += GetDeltaTime();

            float progress = Mathf.Clamp01(timer / duration);
            float curvedProgress = fadeCurve.Evaluate(progress);
            float fade = Mathf.Lerp(from, to, curvedProgress);

            SetSandFade(fade);
            SetSandAudioVolume(fade);

            yield return null;
        }

        SetSandFade(to);
        SetSandAudioVolume(to);
    }

    private IEnumerator Wait(float duration)
    {
        if (duration <= 0f)
            yield break;

        SetSandAudioVolume(1f);

        float timer = 0f;

        while (timer < duration)
        {
            timer += GetDeltaTime();
            yield return null;
        }
    }

    private void PlaySandAudio()
    {
        if (sandStormAudioSource == null)
            return;

        if (sandStormAudioClip != null)
            sandStormAudioSource.clip = sandStormAudioClip;

        sandStormAudioSource.loop = loopSandStormAudio;
        sandStormAudioSource.volume = 0f;

        if (!sandStormAudioSource.isPlaying)
            sandStormAudioSource.Play();
    }

    private void StopSandAudio()
    {
        if (sandStormAudioSource == null)
            return;

        sandStormAudioSource.Stop();
    }

    private void SetSandAudioVolume(float fade)
    {
        if (sandStormAudioSource == null)
            return;

        sandStormAudioSource.volume = Mathf.Clamp01(fade) * sandStormMaxVolume;
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private void SetSandFade(float fade)
    {
        if (sandStormMaterial == null)
            return;

        sandStormMaterial.SetFloat(FadeId, Mathf.Clamp01(fade));
    }
}