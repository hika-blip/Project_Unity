using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TitleSoundManager : MonoBehaviour
{
    [Header("BGM1設定")]
    public AudioClip TitleBGM1;
    [Range(0f, 1f)]
    public float BGM1Volume = 1f;
    private AudioSource audioSource1;

    [Header("BGM2設定")]
    public AudioClip TitleBGM2;
    [Range(0f, 1f)]
    public float BGM2Volume = 1f;
    private AudioSource audioSource2;

    [Header("BGM3設定（一定時間を空けてループ）")]
    public AudioClip TitleBGM3;
    [Range(0f, 1f)]
    public float BGM3Volume = 1f;
    [Tooltip("シーン開始から最初に再生するまでの秒数")]
    public float BGM3StartDelay = 0f;
    [Tooltip("再生が終わってから、次に再生するまでの秒数")]
    public float BGM3Interval = 5f;
    private AudioSource audioSource3;

    // Start is called before the first frame update
    void Start()
    {
        // BGM1のAudioSource設定
        audioSource1 = gameObject.AddComponent<AudioSource>();
        audioSource1.clip = TitleBGM1;
        audioSource1.volume = BGM1Volume;
        audioSource1.loop = true; // ループ再生をON
        audioSource1.playOnAwake = false; // Awake時には再生しない
        if (TitleBGM1 != null)
        {
            audioSource1.Play(); // 手動で再生
        }

        // BGM2のAudioSource設定
        audioSource2 = gameObject.AddComponent<AudioSource>();
        audioSource2.clip = TitleBGM2;
        audioSource2.volume = BGM2Volume;
        audioSource2.loop = true; // ループ再生をON
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
            StartCoroutine(PlayBGM3WithInterval());
        }
    }

    /// <summary>
    /// BGM3を「再生 → 終わったら BGM3Interval 秒待つ → また再生」と繰り返す
    /// </summary>
    IEnumerator PlayBGM3WithInterval()
    {
        if (BGM3StartDelay > 0f)
        {
            yield return new WaitForSeconds(BGM3StartDelay);
        }

        while (true)
        {
            audioSource3.Play();

            // 再生が終わるまで待つ
            yield return new WaitWhile(() => audioSource3.isPlaying);

            // 次の再生まで待つ
            if (BGM3Interval > 0f)
            {
                yield return new WaitForSeconds(BGM3Interval);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        // インスペクターでのボリューム変更をリアルタイムに反映
        if (audioSource1 != null)
        {
            audioSource1.volume = BGM1Volume;
        }
        if (audioSource2 != null)
        {
            audioSource2.volume = BGM2Volume;
        }
        if (audioSource3 != null)
        {
            audioSource3.volume = BGM3Volume;
        }
    }
}