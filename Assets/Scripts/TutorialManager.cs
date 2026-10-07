using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Video;

/// <summary>
/// 虫取りゲームのチュートリアル管理
/// GameStartManager2の後、PathFollower2の前に実行される
/// BugCatcher2の機能を利用してチュートリアルを実行
///
/// 【変更点】
/// ・チュートリアル用の虫を表示した時点（Phase3）から捕獲を受け付け、
///   どのフェーズで捕まえてもチュートリアルを完了してゲーム本編へ進む
/// ・各フェーズの説明を音声でも再生（AudioSource + AudioClip）
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI tutorialText; //  チュートリアルテキスト
    public Image targetIllustration; // 中央のイラスト

    [Header("Tutorial Target")]
    public GameObject tutorialBug; // チュートリアル用の虫オブジェクト
    public Transform targetPosition; // 虫を配置する位置

    [Header("Camera")]
    public Camera mainCamera;

    [Header("Target Detection Settings")]
    public float targetCenterThreshold = 0.15f; // 画面中心からの許容範囲（0-1の正規化座標）
    public float detectionTime = 1.0f; // ターゲットを捉え続ける必要がある時間

    [Header("Text Settings")]
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;
    public Color outlineColor = Color.black;

    [Header("Timing Settings")]
    public float phase1DisplayTime = 3.0f; // 「捕まえてみよう」表示時間
    public float phase3DisplayTime = 7.0f; // 「右や左に動いて」表示時間
    [Tooltip("完了メッセージを表示してからゲーム本編に移るまでの秒数")]
    public float completeDisplayTime = 2.0f;

    [Header("Next Script")]
    public PathFollower3 pathFollowerScript; // PathFollower2スクリプト（型を明示）
    public BugCatcher2 BugCatcherScript; // BugCather スクリプト

    [Header("Game Bugs Setup")]
    public List<GameObject> gameBugs; // ゲーム本編用の虫リスト

    [Header("Catch Detection")]
    [Tooltip("チュートリアルの虫が非表示（非アクティブ）になったら捕獲されたとみなす（BugCatcher2が捕獲時に虫を消す場合の保険）")]
    public bool detectCatchByDeactivation = true;

    [Header("Session Recording")]
    public bool recordTutorialData = true;

    [Header("Skip Settings")]
    public bool allowSkip = true; // スキップを許可するか
    public KeyCode skipKey = KeyCode.Escape; // スキップキー

    [Header("Video Settings")]
    public RawImage videoRawImage1; // 動画1表示用RawImage（左右に動いて＋真ん中に）
    public VideoPlayer videoPlayer1; // 動画1のVideoPlayer
    public RawImage videoRawImage2; // 動画2表示用RawImage（ジェスチャーで捕まえよう）
    public VideoPlayer videoPlayer2; // 動画2のVideoPlayer

    [Header("Voice Settings（音声ガイド）")]
    [Tooltip("音声を再生するAudioSource（空欄なら自動で追加）")]
    public AudioSource voiceSource;
    [Tooltip("「つかまえてみよう！」")]
    public AudioClip voicePhase1;
    [Tooltip("「みぎやひだりにうごいて！」")]
    public AudioClip voicePhase3;
    [Tooltip("「むしをまんなかに！」")]
    public AudioClip voicePhase4;
    [Tooltip("「ジェスチャーでつかまえよう！」")]
    public AudioClip voicePhase5;
    [Tooltip("「できた！ゲームスタート！」")]
    public AudioClip voiceComplete;
    [Range(0f, 1f)] public float voiceVolume = 1.0f;
    [Tooltip("音声が表示時間より長い場合、音声が終わるまで次のフェーズに進まない")]
    public bool waitForVoiceToFinish = true;
    [Tooltip("Phase4・5で待っている間、この秒数ごとに同じ音声をくり返す（0でくり返さない）")]
    public float voiceRepeatInterval = 8.0f;

    // チュートリアルの状態
    private enum TutorialPhase
    {
        Phase1_CatchPrompt,      //「捕まえてみよう」表示
        Phase2_HideText,         // テキスト非表示、イラスト再表示
        Phase3_AimingPrompt,     //「右や左に動いて」表示
        Phase4_WaitForAiming,    // ターゲット位置合わせ待ち
        Phase5_WaitForGesture,   // ジェスチャー待ち
        Completed                // チュートリアル完了
    }

    private TutorialPhase currentPhase = TutorialPhase.Phase1_CatchPrompt;
    private float phaseStartTime;
    private float phaseMinDuration;   // 音声の長さを考慮したそのフェーズの最低表示時間
    private float targetingTime = 0f; // ターゲットを捉えている時間
    private float tutorialStartTime;
    private int initialBugsCaughtCount = 0; // 捕獲受付開始時の捕獲数
    private bool catchWatchActive = false;  // 捕獲の監視中か
    private float nextVoiceRepeatTime = -1f;
    private AudioClip repeatingClip;
    private Coroutine phase3WaitCoroutine;
    private Coroutine video2Coroutine;

    void Start()
    {
        tutorialStartTime = Time.time;

        if (mainCamera == null)
            mainCamera = Camera.main;

        // 音声用AudioSourceの準備
        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
        }
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = 0f; // 2D音声（画面のどこからでも同じ音量）

        // PathFollowerの停止
        // if (pathFollowerScript != null)
        // {
        //     pathFollowerScript.enabled = false;
        //     // pauseMovementもtrueに設定して確実に停止
        //     pathFollowerScript.pauseMovement = true;
        //     // Debug.Log("TutorialManager: PathFollower3 を無効化しました");
        // }
        // else
        // {
        //     Debug.LogWarning("TutorialManager: PathFollower3スクリプトが設定されていません");
        // }

        // BugCatcher2をPhase3まで無効化（Phase3開始時にBeginCatchWatch()で有効化）
        // if (BugCatcherScript != null)
        // {
        //     BugCatcherScript.enabled = false;
        //     Debug.Log("TutorialManager: BugCatcher2 を無効化しました（Phase3で有効化）");
        // }

        // チュートリアル用の虫を配置・リセット
        if (tutorialBug != null)
        {
            if (targetPosition != null)
            {
                tutorialBug.transform.position = targetPosition.position;
            }
            tutorialBug.SetActive(false); // 最初は非表示

            Debug.Log("TutorialManager: チュートリアル用の虫をリセットしました");
        }

        // テキストに縁取りを適用
        // ApplyOutline(tutorialText);

        // 動画を初期化（全て非表示）
        HideAllVideos();

        // セッション記録: TutorialManager開始時刻を記録
        if (recordTutorialData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordTutorialStart();
            UserSessionManager.Instance.RecordCustomEvent("TutorialStart", "");
        }

        // Phase1開始
        StartPhase1();
    }

    // ------------------------------------------------------------
    // 音声
    // ------------------------------------------------------------

    /// <summary>音声を再生（前の音声は止める）。再生した音声の長さを返す</summary>
    float PlayVoice(AudioClip clip)
    {
        if (voiceSource == null) return 0f;

        voiceSource.Stop();
        if (clip == null) return 0f;

        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
        return clip.length;
    }

    /// <summary>待っている間に音声をくり返すよう設定</summary>
    void SetRepeatingVoice(AudioClip clip)
    {
        repeatingClip = clip;
        if (clip != null && voiceRepeatInterval > 0f)
            nextVoiceRepeatTime = Time.time + clip.length + voiceRepeatInterval;
        else
            nextVoiceRepeatTime = -1f;
    }

    void UpdateRepeatingVoice()
    {
        if (repeatingClip == null || nextVoiceRepeatTime < 0f) return;
        if (voiceSource != null && voiceSource.isPlaying) return;

        if (Time.time >= nextVoiceRepeatTime)
        {
            PlayVoice(repeatingClip);
            nextVoiceRepeatTime = Time.time + repeatingClip.length + voiceRepeatInterval;
        }
    }

    void StopVoice()
    {
        repeatingClip = null;
        nextVoiceRepeatTime = -1f;
        if (voiceSource != null) voiceSource.Stop();
    }

    /// <summary>表示時間と音声の長さのうち長い方を、そのフェーズの最低表示時間にする</summary>
    float MinDuration(float displayTime, float voiceLength)
    {
        return waitForVoiceToFinish ? Mathf.Max(displayTime, voiceLength) : displayTime;
    }

    // ------------------------------------------------------------
    // 動画
    // ------------------------------------------------------------
    void HideAllVideos()
    {
        if (video2Coroutine != null)
        {
            StopCoroutine(video2Coroutine);
            video2Coroutine = null;
        }
        if (videoRawImage1 != null)
        {
            videoRawImage1.gameObject.SetActive(false);
        }
        if (videoRawImage2 != null)
        {
            videoRawImage2.gameObject.SetActive(false);
        }
        if (videoPlayer1 != null)
        {
            videoPlayer1.Stop();
        }
        if (videoPlayer2 != null)
        {
            videoPlayer2.Stop();
        }
    }

    void ShowVideo1()
    {
        Debug.Log("ShowVideo1: 動画1への切り替え開始");

        // 動画2を停止・非表示
        if (videoRawImage2 != null)
        {
            videoRawImage2.gameObject.SetActive(false);
        }
        if (videoPlayer2 != null)
        {
            videoPlayer2.Stop();
        }

        // 動画1を表示・再生
        if (videoRawImage1 != null && videoPlayer1 != null)
        {
            videoRawImage1.gameObject.SetActive(true);
            videoPlayer1.Play();
            Debug.Log($"動画1を再生開始: RawImage Active={videoRawImage1.gameObject.activeSelf}, VideoPlayer Playing={videoPlayer1.isPlaying}");
        }
        else
        {
            Debug.LogWarning($"ShowVideo1: 動画1の設定が不完全です - RawImage={videoRawImage1 != null}, VideoPlayer={videoPlayer1 != null}");
        }
    }

    void ShowVideo2()
    {
        Debug.Log("ShowVideo2: 動画2への切り替え開始");

        // 動画1を停止・非表示
        if (videoRawImage1 != null)
        {
            videoRawImage1.gameObject.SetActive(false);
        }
        if (videoPlayer1 != null)
        {
            videoPlayer1.Stop();
        }

        // 少し待ってから動画2を再生（確実に切り替わるように）
        if (video2Coroutine != null) StopCoroutine(video2Coroutine);
        video2Coroutine = StartCoroutine(PlayVideo2AfterDelay());
    }

    IEnumerator PlayVideo2AfterDelay()
    {
        yield return new WaitForSeconds(0.1f); // 0.1秒待つ

        // 動画2を表示・再生
        if (videoRawImage2 != null && videoPlayer2 != null)
        {
            videoRawImage2.gameObject.SetActive(true);
            videoPlayer2.Play();
            Debug.Log($"動画2を再生開始: RawImage Active={videoRawImage2.gameObject.activeSelf}, VideoPlayer Playing={videoPlayer2.isPlaying}");
        }
        else
        {
            Debug.LogWarning($"ShowVideo2: 動画2の設定が不完全です - RawImage={videoRawImage2 != null}, VideoPlayer={videoPlayer2 != null}");
        }
        video2Coroutine = null;
    }

    // void ApplyOutline(TextMeshProUGUI textComponent)
    // {
    //     if (textComponent != null)
    //     {
    //         textComponent.outlineWidth = outlineWidth;
    //         textComponent.outlineColor = outlineColor;
    //     }
    // }

    // ------------------------------------------------------------
    // 更新
    // ------------------------------------------------------------
    void Update()
    {
        if (currentPhase == TutorialPhase.Completed) return;

        // ESCキーでチュートリアルスキップ
        if (allowSkip && Input.GetKeyDown(skipKey))
        {
            SkipTutorial();
            return;
        }

        // ★ どのフェーズでも、チュートリアルの虫を捕まえたら完了
        if (IsTutorialBugCaught())
        {
            Debug.Log($"チュートリアルの虫が捕獲されました！（{currentPhase}）");
            CompleteTutorial();
            return;
        }

        switch (currentPhase)
        {
            case TutorialPhase.Phase1_CatchPrompt:
                UpdatePhase1();
                break;
            case TutorialPhase.Phase2_HideText:
                UpdatePhase2();
                break;
            case TutorialPhase.Phase3_AimingPrompt:
                UpdatePhase3();
                break;
            case TutorialPhase.Phase4_WaitForAiming:
                UpdatePhase4();
                break;
            case TutorialPhase.Phase5_WaitForGesture:
                UpdatePhase5();
                break;
        }
    }

    // ------------------------------------------------------------
    // 捕獲の受付・判定
    // ------------------------------------------------------------

    /// <summary>
    /// チュートリアルの虫を捕まえられる状態にする（虫を表示した時点で1回だけ呼ぶ）
    /// </summary>
    void BeginCatchWatch()
    {
        if (catchWatchActive) return;

        if (BugCatcherScript != null)
        {
            // リストをクリアして、チュートリアルの虫だけを追加
            BugCatcherScript.bugs.Clear();
            if (tutorialBug != null) BugCatcherScript.bugs.Add(tutorialBug);

            // BugCatchDataを初期化（チュートリアルの虫1匹分）
            BugCatchData.Initialize(1);

            // BugCatcher2を有効化
            BugCatcherScript.enabled = true;
        }

        // 捕獲受付開始時の捕獲数を記録（この時点では0のはず）
        initialBugsCaughtCount = BugCatchData.GetCaughtCount();
        catchWatchActive = true;

        Debug.Log($"TutorialManager: チュートリアルの虫の捕獲受付を開始しました (初期捕獲数: {initialBugsCaughtCount})");
    }

    /// <summary>チュートリアルの虫が捕まったか</summary>
    bool IsTutorialBugCaught()
    {
        if (!catchWatchActive) return false;

        // BugCatcher2の捕獲数が増えた
        if (BugCatchData.GetCaughtCount() > initialBugsCaughtCount)
            return true;

        // 表示していたはずの虫が消えた（BugCatcher2が捕獲時に非表示にした）
        if (detectCatchByDeactivation && tutorialBug != null && !tutorialBug.activeSelf)
            return true;

        return false;
    }

    // ------------------------------------------------------------
    // スキップ
    // ------------------------------------------------------------
    void SkipTutorial()
    {
        Debug.Log("Tutorial skipped by user (ESC key)");

        // セッション記録: スキップされたことを記録
        if (recordTutorialData && UserSessionManager.Instance != null)
        {
            float tutorialTime = Time.time - tutorialStartTime;
            UserSessionManager.Instance.RecordCustomEvent(
                "TutorialSkipped",
                $"Phase={currentPhase},Time={tutorialTime:F2}s"
            );
        }

        currentPhase = TutorialPhase.Completed;
        catchWatchActive = false;
        StopPendingCoroutines();
        StopVoice();

        // チュートリアルの虫を非表示
        if (tutorialBug != null)
        {
            tutorialBug.SetActive(false);
        }

        // ゲーム本編用の虫を設定
        SetupMainGameBugs();

        // すぐにゲーム本編へ
        StartCoroutine(StartMainGameImmediate());
    }

    IEnumerator StartMainGameImmediate()
    {
        // 全ての動画を非表示・停止
        HideAllVideos();

        // UIを非表示
        if (tutorialText != null)
            tutorialText.gameObject.SetActive(false);
        if (targetIllustration != null)
            targetIllustration.gameObject.SetActive(true);

        yield return null; // 1フレーム待つ

        StartPathFollower("スキップ");

        // このスクリプトを無効化
        this.enabled = false;

        Debug.Log("Main game started (skipped)!");
    }

    // ------------------------------------------------------------
    // Phase1: 「捕まえてみよう」表示
    // ------------------------------------------------------------
    void StartPhase1()
    {
        currentPhase = TutorialPhase.Phase1_CatchPrompt;
        phaseStartTime = Time.time;

        if (tutorialText != null)
        {
            tutorialText.gameObject.SetActive(true);
            tutorialText.text = "つかまえてみよう！";
        }

        // イラストを非表示
        // if (targetIllustration != null)
        // {
        //     targetIllustration.gameObject.SetActive(false);
        // }

        float voiceLength = PlayVoice(voicePhase1);
        phaseMinDuration = MinDuration(phase1DisplayTime, voiceLength);

        Debug.Log("Tutorial Phase 1: つかまえてみよう");
    }

    void UpdatePhase1()
    {
        if (Time.time - phaseStartTime >= phaseMinDuration)
        {
            StartPhase2();
        }
    }

    // ------------------------------------------------------------
    // Phase2: テキスト消去、イラスト再表示
    // ------------------------------------------------------------
    void StartPhase2()
    {
        currentPhase = TutorialPhase.Phase2_HideText;
        phaseStartTime = Time.time;

        if (tutorialText != null)
        {
            tutorialText.gameObject.SetActive(false);
        }

        // イラストを再表示
        if (targetIllustration != null)
        {
            targetIllustration.gameObject.SetActive(true);
        }

        Debug.Log("Tutorial Phase 2: Text hidden, Illustration shown");

        // 1秒後にPhase3へ
        phase3WaitCoroutine = StartCoroutine(WaitAndStartPhase3());
    }

    void UpdatePhase2()
    {
        // コルーチンで処理
    }

    IEnumerator WaitAndStartPhase3()
    {
        yield return new WaitForSeconds(1.0f);
        phase3WaitCoroutine = null;

        // 待っている間に完了・スキップされていたら何もしない
        if (currentPhase == TutorialPhase.Phase2_HideText)
        {
            StartPhase3();
        }
    }

    // ------------------------------------------------------------
    // Phase3: 「右や左に動いて」表示
    // ------------------------------------------------------------
    void StartPhase3()
    {
        currentPhase = TutorialPhase.Phase3_AimingPrompt;
        phaseStartTime = Time.time;

        if (tutorialText != null)
        {
            tutorialText.gameObject.SetActive(true);
            tutorialText.text = "みぎやひだりにうごいて！";
        }

        // チュートリアル用の虫を表示
        if (tutorialBug != null)
        {
            tutorialBug.SetActive(true);
        }

        // ★ 虫が見えた時点から捕獲を受け付ける
        BeginCatchWatch();

        // 動画1を表示・再生
        ShowVideo1();

        float voiceLength = PlayVoice(voicePhase3);
        phaseMinDuration = MinDuration(phase3DisplayTime, voiceLength);

        Debug.Log("Tutorial Phase 3: 右や左に動いて");
    }

    void UpdatePhase3()
    {
        if (Time.time - phaseStartTime >= phaseMinDuration)
        {
            StartPhase4();
        }
    }

    // ------------------------------------------------------------
    // Phase4: ターゲット位置合わせ待ち
    // ------------------------------------------------------------
    void StartPhase4()
    {
        currentPhase = TutorialPhase.Phase4_WaitForAiming;
        phaseStartTime = Time.time;

        if (tutorialText != null)
        {
            tutorialText.text = "むしをまんなかに！";
        }

        targetingTime = 0f;

        // 動画1を継続表示（Phase3から継続）

        PlayVoice(voicePhase4);
        SetRepeatingVoice(voicePhase4);

        Debug.Log("Tutorial Phase 4: Waiting for aiming");
    }

    void UpdatePhase4()
    {
        UpdateRepeatingVoice();

        if (tutorialBug == null || mainCamera == null)
            return;

        // 虫が画面中央にいるかチェック
        if (IsBugInCenter())
        {
            targetingTime += Time.deltaTime;

            // 一定時間ターゲットを捉え続けたら次へ
            if (targetingTime >= detectionTime)
            {
                StartPhase5();
            }
        }
        else
        {
            // ターゲットを外したらリセット
            targetingTime = 0f;
        }
    }

    bool IsBugInCenter()
    {
        if (tutorialBug == null || mainCamera == null)
            return false;

        // 虫が非アクティブの場合はfalse
        if (!tutorialBug.activeSelf)
            return false;

        // ワールド座標をスクリーン座標に変換
        Vector3 screenPos = mainCamera.WorldToViewportPoint(tutorialBug.transform.position);

        // 画面外にいる場合はfalse
        if (screenPos.z < 0)
            return false;

        // 中心からの距離を計算（正規化座標: 0.5が中心）
        float distanceFromCenter = Vector2.Distance(
            new Vector2(screenPos.x, screenPos.y),
            new Vector2(0.5f, 0.5f)
        );

        return distanceFromCenter < targetCenterThreshold;
    }

    // ------------------------------------------------------------
    // Phase5: ジェスチャー待ち（BugCatcher2が捕獲を検出）
    // ------------------------------------------------------------
    void StartPhase5()
    {
        currentPhase = TutorialPhase.Phase5_WaitForGesture;
        phaseStartTime = Time.time;

        ShowVideo2();

        // ★ 捕獲の受付はPhase3で開始済み。
        //   ここでBugCatchDataを初期化し直すと、それまでの捕獲が消えてしまうので行わない。
        BeginCatchWatch(); // 念のため（開始済みなら何もしない）

        if (tutorialText != null)
        {
            tutorialText.text = "ジェスチャーでつかまえよう！";
        }

        PlayVoice(voicePhase5);
        SetRepeatingVoice(voicePhase5);

        Debug.Log("Tutorial Phase 5: Waiting for gesture (BugCatcher2 will handle)");
    }

    void UpdatePhase5()
    {
        UpdateRepeatingVoice();
        // 捕獲の判定はUpdate()の先頭（IsTutorialBugCaught）でどのフェーズでも行う
    }

    // ------------------------------------------------------------
    // 完了
    // ------------------------------------------------------------
    void CompleteTutorial()
    {
        if (currentPhase == TutorialPhase.Completed) return;

        TutorialPhase completedAt = currentPhase;
        currentPhase = TutorialPhase.Completed;
        catchWatchActive = false;

        Debug.Log("Tutorial Completed!");

        // 途中のフェーズ切り替えや動画の再生予約を止める
        StopPendingCoroutines();

        // 全ての動画を非表示
        HideAllVideos();

        // テキストを更新
        if (tutorialText != null)
        {
            tutorialText.gameObject.SetActive(true);
            tutorialText.text = "できた！ゲームスタート！";
        }

        // 完了の音声（それまでの音声は止める）
        repeatingClip = null;
        nextVoiceRepeatTime = -1f;
        float voiceLength = PlayVoice(voiceComplete);

        // セッション記録
        if (recordTutorialData && UserSessionManager.Instance != null)
        {
            float tutorialTime = Time.time - tutorialStartTime;
            UserSessionManager.Instance.RecordCustomEvent(
                "TutorialComplete",
                $"Time={tutorialTime:F2}s,CaughtAt={completedAt}"
            );
        }

        // 完了メッセージを表示してからゲーム本編へ
        StartCoroutine(StartMainGame(MinDuration(completeDisplayTime, voiceLength)));
    }

    IEnumerator StartMainGame(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);

        // UIを非表示
        if (tutorialText != null)
            tutorialText.gameObject.SetActive(false);
        if (targetIllustration != null)
            targetIllustration.gameObject.SetActive(true);

        // BugCatcher2にゲーム本編用の虫を設定
        SetupMainGameBugs();

        // PathFollowerを有効化（ここでようやく自動歩行開始）
        StartPathFollower("自動歩行開始");

        // このスクリプトを無効化
        this.enabled = false;

        Debug.Log("Main game started! PathFollower and BugCatcher are now active.");
    }

    // ------------------------------------------------------------
    // 共通処理
    // ------------------------------------------------------------

    /// <summary>BugCatcher2にゲーム本編用の虫を設定</summary>
    void SetupMainGameBugs()
    {
        if (BugCatcherScript == null)
        {
            Debug.LogWarning("TutorialManager: BugCatcher2が設定されていません");
            return;
        }

        // チュートリアルの虫をリストから削除
        BugCatcherScript.bugs.Clear();

        // ゲーム本編用の虫をリストに追加
        if (gameBugs != null && gameBugs.Count > 0)
        {
            foreach (GameObject bug in gameBugs)
            {
                if (bug != null)
                {
                    BugCatcherScript.bugs.Add(bug);
                    bug.SetActive(true); // 虫を表示
                }
            }

            // BugCatchDataを再初期化（ゲーム本編用の虫の数で）
            BugCatchData.Initialize(BugCatcherScript.bugs.Count);

            Debug.Log($"TutorialManager: ゲーム本編用の虫を設定しました（{BugCatcherScript.bugs.Count}匹）");
        }
        else
        {
            Debug.LogWarning("TutorialManager: gameBugsが設定されていません！");
        }

        BugCatcherScript.enabled = true;
    }

    /// <summary>PathFollowerを有効化してゲーム本編の移動を開始</summary>
    void StartPathFollower(string reason)
    {
        if (pathFollowerScript != null)
        {
            pathFollowerScript.enabled = true;
            pathFollowerScript.pauseMovement = false; // 移動を許可
            Debug.Log($"TutorialManager: PathFollower3 を有効化しました（{reason}）");

            // セッション記録: PathFollower開始時刻を記録
            if (recordTutorialData && UserSessionManager.Instance != null)
            {
                UserSessionManager.Instance.RecordTutorialEnd();
            }
        }
    }

    /// <summary>フェーズ切り替え待ち・動画再生待ちのコルーチンを止める</summary>
    void StopPendingCoroutines()
    {
        if (phase3WaitCoroutine != null)
        {
            StopCoroutine(phase3WaitCoroutine);
            phase3WaitCoroutine = null;
        }
        if (video2Coroutine != null)
        {
            StopCoroutine(video2Coroutine);
            video2Coroutine = null;
        }
    }

    void OnDestroy()
    {
        // 未完了の場合は記録
        if (currentPhase != TutorialPhase.Completed &&
            recordTutorialData &&
            UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent(
                "TutorialIncomplete",
                $"LastPhase={currentPhase}"
            );
        }
    }
}