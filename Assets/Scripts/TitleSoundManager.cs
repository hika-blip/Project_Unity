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

        // BGM3のAudioSource設定（ループはコルーチンで間隔を空けて行う）
        audioSource3 = gameObject.AddComponent<AudioSource>();
        audioSource3.clip = TitleBGM3;
        audioSource3.volume = BGM3Volume;
        audioSource3.loop = false; // 自動ループはOFF（間隔を空けるため）
        audioSource3.playOnAwake = false; // Awake時には再生しない
        if (TitleBGM3 != null)
        {
            bgm3Routine = StartCoroutine(PlayBGM3WithInterval(BGM3StartDelay));
        }
    }

    /// <summary>
    /// BGM3を「再生 → 終わったら BGM3Interval 秒待つ → また再生」と繰り返す
    /// （BGM3Loop がOFFなら1回だけ再生して終わる）
    /// </summary>
    IEnumerator PlayBGM3WithInterval(float startDelay)
    {
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        while (true)
        {
            audioSource3.Play();

            // 再生が終わるまで待つ
            yield return new WaitWhile(() => audioSource3.isPlaying);

            // ループOFFならここで終わり
            if (!BGM3Loop) break;

            // 次の再生まで待つ
            if (BGM3Interval > 0f)
            {
                yield return new WaitForSeconds(BGM3Interval);
            }

            // 待っている間にループOFFにされたら、次は再生しない
            if (!BGM3Loop) break;
        }
        bgm3Routine = null;
    }

    // Update is called once per frame
    void Update()
    {
        // インスペクターでのボリューム・ループ設定の変更をリアルタイムに反映
        UpdateLoopingSource(audioSource1, BGM1Volume, BGM1Loop);
        UpdateLoopingSource(audioSource2, BGM2Volume, BGM2Loop);

        if (audioSource3 != null)
        {
            audioSource3.volume = BGM3Volume;

            // 止まっている状態でループONにされたら、間隔を空けて再開する
            if (BGM3Loop && bgm3Routine == null && TitleBGM3 != null && !audioSource3.isPlaying)
            {
                bgm3Routine = StartCoroutine(PlayBGM3WithInterval(BGM3Interval));
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