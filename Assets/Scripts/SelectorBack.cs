using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TitleSoundManager : MonoBehaviour
{
    [Header("BGM1設定")]
    public AudioClip TitleBGM1;
    [Range(0f, 1f)]
    public float BGM1Volume = 1f;
    [Tooltip("ONでループ再生、OFFで1回だけ再生")]
    public bool BGM1Loop = true;
    private AudioSource audioSource1;

    [Header("BGM2設定")]
    public AudioClip TitleBGM2;
    [Range(0f, 1f)]
    public float BGM2Volume = 1f;
    [Tooltip("ONでループ再生、OFFで1回だけ再生")]
    public bool BGM2Loop = true;
    private AudioSource audioSource2;

    [Header("BGM3設定（一定時間を空けてループ）")]
    public AudioClip TitleBGM3;
    [Range(0f, 1f)]
    public float BGM3Volume = 1f;
    [Tooltip("ONで一定時間を空けてループ再生、OFFで1回だけ再生")]
    public bool BGM3Loop = true;
    [Tooltip("シーン開始から最初に再生するまでの秒数")]
    public float BGM3StartDelay = 0f;
    [Tooltip("再生が終わってから、次に再生するまでの秒数")]
    public float BGM3Interval = 5f;
    private AudioSource audioSource3;
    private Coroutine bgm3Routine;

    [Header("BGM4設定（一定時間を空けてループ）")]
    public AudioClip TitleBGM4;
    [Range(0f, 1f)]
    public float BGM4Volume = 1f;
    [Tooltip("ONで一定時間を空けてループ再生、OFFで1回だけ再生")]
    public bool BGM4Loop = true;
    [Tooltip("シーン開始から最初に再生するまでの秒数")]
    public float BGM4StartDelay = 0f;
    [Tooltip("再生が終わってから、次に再生するまでの秒数")]
    public float BGM4Interval = 5f;
    private AudioSource audioSource4;
    private Coroutine bgm4Routine;

    // Start is called before the first frame update
    void Start()
    {
        // BGM1のAudioSource設定
        audioSource1 = gameObject.AddComponent<AudioSource>();
        audioSource1.clip = TitleBGM1;
        audioSource1.volume = BGM1Volume;
        audioSource1.loop = BGM1Loop; // ループ再生の有無
        audioSource1.playOnAwake = false; // Awake時には再生しない
        if (TitleBGM1 != null)
        {
            audioSource1.Play(); // 手動で再生
        }

        // BGM2のAudioSource設定
        audioSource2 = gameObject.AddComponent<AudioSource>();
        audioSource2.clip = TitleBGM2;
        audioSource2.volume = BGM2Volume;
        audioSource2.loop = BGM2Loop; // ループ再生の有無
        audioSource2.playOnAwake = false; // Awake時には再生しない
        if (TitleBGM2 != null)
        {
            audioSource2.Play(); // 手動で再生
        }

        // BGM3・BGM4のAudioSource設定（ループはコルーチンで間隔を空けて行う）
        audioSource3 = CreateIntervalSource(TitleBGM3, BGM3Volume);
        audioSource4 = CreateIntervalSource(TitleBGM4, BGM4Volume);

        if (TitleBGM3 != null)
        {
            bgm3Routine = StartCoroutine(PlayWithInterval(
                audioSource3, BGM3StartDelay, () => BGM3Loop, () => BGM3Interval, () => bgm3Routine = null));
        }
        if (TitleBGM4 != null)
        {
            bgm4Routine = StartCoroutine(PlayWithInterval(
                audioSource4, BGM4StartDelay, () => BGM4Loop, () => BGM4Interval, () => bgm4Routine = null));
        }
    }

    /// <summary>間隔を空けてループするBGM用のAudioSourceを作る</summary>
    AudioSource CreateIntervalSource(AudioClip clip, float volume)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.loop = false; // 自動ループはOFF（間隔を空けるため）
        source.playOnAwake = false; // Awake時には再生しない
        return source;
    }

    /// <summary>
    /// 「再生 → 終わったら interval 秒待つ → また再生」と繰り返す
    /// （loop がOFFなら1回だけ再生して終わる）
    /// loop・interval は毎回Inspectorの値を読むので、再生中の変更も反映される
    /// </summary>
    IEnumerator PlayWithInterval(AudioSource source, float startDelay,
                                 Func<bool> loop, Func<float> interval, Action onFinished)
    {
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        while (true)
        {
            source.Play();

            // 再生が終わるまで待つ
            yield return new WaitWhile(() => source.isPlaying);

            // ループOFFならここで終わり
            if (!loop()) break;

            // 次の再生まで待つ
            float wait = interval();
            if (wait > 0f)
            {
                yield return new WaitForSeconds(wait);
            }

            // 待っている間にループOFFにされたら、次は再生しない
            if (!loop()) break;
        }
        onFinished();
    }

    // Update is called once per frame
    void Update()
    {
        // インスペクターでのボリューム・ループ設定の変更をリアルタイムに反映
        UpdateLoopingSource(audioSource1, BGM1Volume, BGM1Loop);
        UpdateLoopingSource(audioSource2, BGM2Volume, BGM2Loop);

        // BGM3：止まっている状態でループONにされたら、間隔を空けて再開する
        if (audioSource3 != null)
        {
            audioSource3.volume = BGM3Volume;
            if (BGM3Loop && bgm3Routine == null && TitleBGM3 != null && !audioSource3.isPlaying)
            {
                bgm3Routine = StartCoroutine(PlayWithInterval(
                    audioSource3, BGM3Interval, () => BGM3Loop, () => BGM3Interval, () => bgm3Routine = null));
            }
        }

        // BGM4：BGM3と同じ
        if (audioSource4 != null)
        {
            audioSource4.volume = BGM4Volume;
            if (BGM4Loop && bgm4Routine == null && TitleBGM4 != null && !audioSource4.isPlaying)
            {
                bgm4Routine = StartCoroutine(PlayWithInterval(
                    audioSource4, BGM4Interval, () => BGM4Loop, () => BGM4Interval, () => bgm4Routine = null));
            }
        }
    }

    /// <summary>
    /// BGM1・2の音量とループ設定を反映する
    /// （ループOFFにすると今の再生が終わったところで止まる。止まった後にONに戻すと再生し直す）
    /// </summary>
    void UpdateLoopingSource(AudioSource source, float volume, bool loop)
    {
        if (source == null) return;

        source.volume = volume;
        source.loop = loop;

        if (loop && source.clip != null && !source.isPlaying)
        {
            source.Play();
        }
    }
}