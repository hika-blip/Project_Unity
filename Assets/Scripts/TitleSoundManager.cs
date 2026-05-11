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
    }
}