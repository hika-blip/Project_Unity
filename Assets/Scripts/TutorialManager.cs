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

    [Header("Next Script")]
    public PathFollower3 pathFollowerScript; // PathFollower2スクリプト（型を明示）
    public BugCatcher2 BugCatcherScript; // BugCather スクリプト

    [Header("Game Bugs Setup")]
    public List<GameObject> gameBugs; // ゲーム本編用の虫リスト

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

    // チュートリアルの状態
    private enum TutorialPhase
    {
        Phase1_CatchPrompt,      // 「捕まえてみよう」表示
        Phase2_HideText,         // テキスト非表示、イラスト再表示
        Phase3_AimingPrompt,     // 「右や左に動いて」表示
        Phase4_WaitForAiming,    // ターゲット位置合わせ待ち
        Phase5_WaitForGesture,   // ジェスチャー待ち
        Completed                // チュートリアル完了
    }

    private TutorialPhase currentPhase = TutorialPhase.Phase1_CatchPrompt;
    private float phaseStartTime;
    private float targetingTime = 0f; // ターゲットを捉えている時間
    private float tutorialStartTime;
    private int initialBugsCaughtCount = 0; // チュートリアル開始時の捕獲数

    void Start()
    {
        tutorialStartTime = Time.time;

        if (mainCamera == null)
            mainCamera = Camera.main;

        // PathFollowerを明示的に無効化（チュートリアル中は動かない）
        if (pathFollowerScript != null)
        {
            pathFollowerScript.enabled = false;
            // pauseMovementもtrueに設定して確実に停止
            pathFollowerScript.pauseMovement = true;
            Debug.Log("TutorialManager: PathFollower3 を無効化しました");
        }
        else
        {
            Debug.LogWarning("TutorialManager: PathFollower3スクリプトが設定されていません");
        }

        // BugCatcher2を一時的に無効化（Phase5まで）
        //if (BugCatcherScript != null)
        //{
        //    BugCatcherScript.enabled = false;
        //    Debug.Log("TutorialManager: BugCatcher2 を無効化しました");
        //}

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
        ApplyOutline(tutorialText);

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

    void HideAllVideos()
    {
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
            Debug.Log("ShowVideo1: 動画2を非表示にしました");
        }
        if (videoPlayer2 != null)
        {
            videoPlayer2.Stop();
            Debug.Log("ShowVideo1: 動画2を停止しました");
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
            Debug.Log("ShowVideo2: 動画1を非表示にしました");
        }
        if (videoPlayer1 != null)
        {
            videoPlayer1.Stop();
            Debug.Log("ShowVideo2: 動画1を停止しました");
        }

        // 少し待ってから動画2を再生（確実に切り替わるように）
        StartCoroutine(PlayVideo2AfterDelay());
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
    }

    void ApplyOutline(TextMeshProUGUI textComponent)
    {
        if (textComponent != null)
        {
            textComponent.outlineWidth = outlineWidth;
            textComponent.outlineColor = outlineColor;
        }
    }

    void Update()
    {
        // ESCキーでチュートリアルスキップ
        if (allowSkip && Input.GetKeyDown(skipKey) && currentPhase != TutorialPhase.Completed)
        {
            SkipTutorial();
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

        // チュートリアルの虫を非表示
        if (tutorialBug != null)
        {
            tutorialBug.SetActive(false);
        }

        // BugCatcher2を有効化（まだの場合）
        //if (BugCatcherScript != null && !BugCatcherScript.enabled)
        //{
        //    BugCatcherScript.enabled = true;

            // チュートリアルの虫をリストから削除（追加されている場合）
            if (BugCatcherScript.bugs.Contains(tutorialBug))
            {
                BugCatcherScript.bugs.Remove(tutorialBug);
            }

            // ゲーム本編用の虫を設定
            BugCatcherScript.bugs.Clear();
            if (gameBugs != null && gameBugs.Count > 0)
            {
                foreach (GameObject bug in gameBugs)
                {
                    if (bug != null)
                    {
                        BugCatcherScript.bugs.Add(bug);
                        bug.SetActive(true);
                    }
                }
                BugCatchData.Initialize(BugCatcherScript.bugs.Count);
                Debug.Log($"TutorialManager(Skip): ゲーム本編用の虫を設定しました（{BugCatcherScript.bugs.Count}匹）");
            }
        //}

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

        // PathFollowerを有効化
        if (pathFollowerScript != null)
        {
            pathFollowerScript.enabled = true;
            pathFollowerScript.pauseMovement = false;
            Debug.Log("TutorialManager: PathFollower3 を有効化しました（スキップ）");

            // セッション記録: PathFollower開始時刻を記録
            if (recordTutorialData && UserSessionManager.Instance != null)
            {
                UserSessionManager.Instance.RecordTutorialEnd();
            }
        }

        // このスクリプトを無効化
        currentPhase = TutorialPhase.Completed;
        this.enabled = false;

        Debug.Log("Main game started (skipped)!");
    }

    // Phase1: 「捕まえてみよう」表示
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
        if (targetIllustration != null)
        {
            targetIllustration.gameObject.SetActive(false);
        }

        //if (recordTutorialData && UserSessionManager.Instance != null)
        //{
        //    UserSessionManager.Instance.RecordCustomEvent("TutorialPhase1", "CatchPrompt");
        //}

        Debug.Log("Tutorial Phase 1: つかまえてみよう");
    }

    void UpdatePhase1()
    {
        if (Time.time - phaseStartTime >= phase1DisplayTime)
        {
            StartPhase2();
        }
    }

    // Phase2: テキスト消去、イラスト再表示
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

        // すぐにPhase3へ
        StartCoroutine(WaitAndStartPhase3());
    }

    void UpdatePhase2()
    {
        // コルーチンで処理
    }

    IEnumerator WaitAndStartPhase3()
    {
        yield return new WaitForSeconds(1.0f);
        StartPhase3();
    }

    // Phase3: 「右や左に動いて」表示
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

        // 動画1を表示・再生
        ShowVideo1();

        //if (recordTutorialData && UserSessionManager.Instance != null)
        //{
        //    UserSessionManager.Instance.RecordCustomEvent("TutorialPhase3", "AimingPrompt");
        //}

        Debug.Log("Tutorial Phase 3: 右や左に動いて");
    }

    void UpdatePhase3()
    {
        if (Time.time - phaseStartTime >= phase3DisplayTime)
        {
            StartPhase4();
        }
    }

    // Phase4: ターゲット位置合わせ待ち
    void StartPhase4()
    {
        currentPhase = TutorialPhase.Phase4_WaitForAiming;
        phaseStartTime = Time.time;

        if (tutorialText != null)
        {
            tutorialText.text = " むしをまんなかに！";
        }

        targetingTime = 0f;

        // 動画1を継続表示（Phase3から継続）
        // すでに表示されているのでそのまま

        //if (recordTutorialData && UserSessionManager.Instance != null)
        //{
        //    UserSessionManager.Instance.RecordCustomEvent("TutorialPhase4", "WaitForAiming");
        //}

        Debug.Log("Tutorial Phase 4: Waiting for aiming");
    }

    void UpdatePhase4()
    {
        if (tutorialBug == null || mainCamera == null)
            return;

        // 虫が画面中央にいるかチェック
        bool isInCenter = IsBugInCenter();

        if (isInCenter)
        {
            targetingTime += Time.deltaTime;

            // 進捗表示（オプション）
            if (tutorialText != null)
            {
                float progress = targetingTime / detectionTime;
                tutorialText.text = "むしをまんなかに！";
            }

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
            if (tutorialText != null)
            {
                tutorialText.text = "むしをまんなかに！";
            }
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

    // Phase5: ジェスチャー待ち（BugCatcher2が捕獲を検出）
    void StartPhase5()
    {
        currentPhase = TutorialPhase.Phase5_WaitForGesture;
        phaseStartTime = Time.time;

        ShowVideo2();

        // チュートリアル用の虫を再度有効化（念のため）
        if (tutorialBug != null)
        {
            tutorialBug.SetActive(true);
        }

        // BugCatcher2のbugsリストにチュートリアルの虫を設定
        if (BugCatcherScript != null)
        {
            // リストをクリアして、チュートリアルの虫だけを追加
            BugCatcherScript.bugs.Clear();
            BugCatcherScript.bugs.Add(tutorialBug);

            // BugCatchDataを再初期化（チュートリアルの虫1匹分）
            BugCatchData.Initialize(1);

            // 初期捕獲数を記録（この時点では0のはず）
            initialBugsCaughtCount = BugCatchData.GetCaughtCount();

            // BugCatcher2を有効化
            BugCatcherScript.enabled = true;

            Debug.Log($"TutorialManager: BugCatcher2 を有効化しました (初期捕獲数: {initialBugsCaughtCount})");
        }

        if (tutorialText != null)
        {
            tutorialText.text = "ジェスチャーでつかまえよう！";
        }

        //if (recordTutorialData && UserSessionManager.Instance != null)
        //{
        //    UserSessionManager.Instance.RecordCustomEvent("TutorialPhase5", "WaitForGesture");
        //}

        Debug.Log("Tutorial Phase 5: Waiting for gesture (BugCatcher2 will handle)");
    }

    void UpdatePhase5()
    {
        // BugCatcher2が虫を捕獲したかチェック
        int currentCaughtCount = BugCatchData.GetCaughtCount();

        Debug.Log($"UpdatePhase5: 現在の捕獲数={currentCaughtCount}, 初期捕獲数={initialBugsCaughtCount}");

        if (currentCaughtCount > initialBugsCaughtCount)
        {
            // チュートリアルの虫が捕まった
            Debug.Log("チュートリアルの虫が捕獲されました！");
            CompleteTutorial();
        }
    }

    void CompleteTutorial()
    {
        currentPhase = TutorialPhase.Completed;

        Debug.Log("Tutorial Completed!");

        // 全ての動画を非表示
        HideAllVideos();

        // テキストを更新
        if (tutorialText != null)
        {
            tutorialText.text = "できた！ゲームスタート！";
        }

        // セッション記録
        if (recordTutorialData && UserSessionManager.Instance != null)
        {
            float tutorialTime = Time.time - tutorialStartTime;
            UserSessionManager.Instance.RecordCustomEvent(
                "TutorialComplete",
                $"Time={tutorialTime:F2}s"
            );
        }

        // 2秒後にゲーム本編へ
        StartCoroutine(StartMainGame());
    }

    IEnumerator StartMainGame()
    {
        yield return new WaitForSeconds(2.0f);

        // UIを非表示
        if (tutorialText != null)
            tutorialText.gameObject.SetActive(false);
        if (targetIllustration != null)
            targetIllustration.gameObject.SetActive(true);

        // BugCatcher2にゲーム本編用の虫を設定
        if (BugCatcherScript != null)
        {
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
        }

        // PathFollowerを有効化（ここでようやく自動歩行開始）
        if (pathFollowerScript != null)
        {
            pathFollowerScript.enabled = true;
            pathFollowerScript.pauseMovement = false; // 移動を許可
            Debug.Log("TutorialManager: PathFollower3 を有効化しました - 自動歩行開始");

            // セッション記録: PathFollower開始時刻を記録
            if (recordTutorialData && UserSessionManager.Instance != null)
            {
                UserSessionManager.Instance.RecordTutorialEnd();
            }
        }

        // このスクリプトを無効化
        this.enabled = false;

        Debug.Log("Main game started! PathFollower and BugCatcher are now active.");
    }

    //void OnGUI()
    //{
    //    GUILayout.BeginArea(new Rect(10, 10, 300, 300));
    //    GUILayout.Label("=== Tutorial Debug ===");
    //    GUILayout.Label($"Phase: {currentPhase}");
    //    GUILayout.Label($"Targeting Time: {targetingTime:F2} / {detectionTime:F2}");

    //    if (tutorialBug != null && mainCamera != null)
    //    {
    //        bool inCenter = IsBugInCenter();
    //        GUILayout.Label($"Bug in Center: {inCenter}");
    //    }

    //    if (allowSkip)
    //    {
    //        GUILayout.Label($"Press {skipKey} to skip tutorial");
    //    }

    //    GUILayout.EndArea();
    //}

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