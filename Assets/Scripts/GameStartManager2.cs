using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityEngine.Video;

/// <summary>
/// ゲーム開始演出とセッション管理を統合
/// Forestシーンの初期化とユーザーセッションの記録を担当
/// </summary>
public class GameStartManager2 : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI startText;
    public TextMeshProUGUI MenuText;
    public Image gameImage;

    [Header("Outline Settings")]
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;
    public Color outlineColor = Color.black;

    [Header("Camera Settings")]
    public Camera mainCamera;
    private MonoBehaviour[] cameraScripts;
    private Vector3 initialCamPos;
    private Quaternion initialCamRot;

    [Header("Session Management")]
    public bool recordSessionData = true;

    [Header("Next Script")]
    //public PathFollower2 pathFollowerScript; // PathFollower2スクリプト
    public PathFollower3 pathFollowerScript; // PathFollower2スクリプト
    public TutorialManager TutorialScript; // Tutorialスクリプト
    public BugCatcher2 BugCatcherScript; // BugCather スクリプト

    [Header("Video Control")]
    public RawImage videoRawImage1; // チュートリアル動画1
    public VideoPlayer videoPlayer1; // チュートリアル動画1のVideoPlayer
    public RawImage videoRawImage2; // チュートリアル動画2
    public VideoPlayer videoPlayer2; // チュートリアル動画2のVideoPlayer

    // シーン開始時間
    private float sceneStartTime;

    void Start()
    {
        sceneStartTime = Time.time;

        // セッション開始を記録
        if (recordSessionData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent("ForestSceneStart", "");
            string userID = UserSessionManager.Instance.CurrentUserID;
            Debug.Log($"Forest Scene開始 - ユーザー: {userID}");
        }

        // PathFollowerを明示的に無効化
        if (pathFollowerScript != null)
        {
            pathFollowerScript.enabled = false;
            Debug.Log("GameStartManager2: PathFollower3 を無効化しました");
        }
        else
        {
            Debug.LogWarning("GameStartManager2: PathFollower3スクリプトが設定されていません");
        }

        // Tutorialを明示的に無効化
        if (TutorialScript != null)
        {
            TutorialScript.enabled = false;
            Debug.Log("GameStartManager2: TutorialScript を無効化しました");
        }
        else
        {
            Debug.LogWarning("GameStartManager2: TutorialScriptスクリプトが設定されていません");
        }

        // BugCatcherを明示的に無効化
        if (BugCatcherScript != null)
        {
            BugCatcherScript.enabled = false;
            Debug.Log("GameStartManager2: BugCatcherScript を無効化しました");
        }
        else
        {
            Debug.LogWarning("GameStartManager2: BugCatcherScriptスクリプトが設定されていません");
        }

        // チュートリアル動画を非表示・停止
        HideAllVideos();

        if (mainCamera == null) mainCamera = Camera.main;

        // カメラの位置と回転を保存
        initialCamPos = mainCamera.transform.position;
        initialCamRot = mainCamera.transform.rotation;

        // カメラに付いているスクリプトを全部停止しておく
        cameraScripts = mainCamera.GetComponents<MonoBehaviour>();
        foreach (var script in cameraScripts)
        {
            script.enabled = false;
        }

        // テキストに縁取りを適用
        ApplyOutline(startText);
        ApplyOutline(MenuText);

        // ゲーム開始時はImageを非表示にする
        if (gameImage != null)
        {
            gameImage.gameObject.SetActive(false);
        }

        // 「ゲームスタート！」を表示
        if (startText != null)
        {
            startText.gameObject.SetActive(true);
            startText.text = "ゲームスタート！";
        }

        if (MenuText != null)
        {
            MenuText.gameObject.SetActive(true);
            MenuText.text = "むしをさがしてつかまえよう！";
        }

        // コルーチンで4秒後に解除
        StartCoroutine(ReleaseStartSequence());
    }

    void HideAllVideos()
    {
        if (videoRawImage1 != null)
        {
            videoRawImage1.gameObject.SetActive(false);
            Debug.Log("GameStartManager2: 動画1を非表示にしました");
        }
        if (videoRawImage2 != null)
        {
            videoRawImage2.gameObject.SetActive(false);
            Debug.Log("GameStartManager2: 動画2を非表示にしました");
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

    void ApplyOutline(TextMeshProUGUI textComponent)
    {
        if (textComponent != null)
        {
            textComponent.outlineWidth = outlineWidth;
            textComponent.outlineColor = outlineColor;
        }
    }

    System.Collections.IEnumerator ReleaseStartSequence()
    {
        // 4秒間待機
        yield return new WaitForSeconds(4f);

        if (TutorialScript != null)
        {
            TutorialScript.enabled = true;
            Debug.Log("GameStartManager2: TutorialScript を有効化しました ");
        }

        if (BugCatcherScript != null)
        {
            BugCatcherScript.enabled = true;
            Debug.Log("GameStartManager2: BugCatcherScript を有効化しました ");
        }

        // 「ゲームスタート！」を消す
        if (startText != null)
        {
            startText.gameObject.SetActive(false);
            MenuText.gameObject.SetActive(false);
        }

        // Imageを表示する
        if (gameImage != null)
        {
            gameImage.gameObject.SetActive(true);
        }

        // カメラのスクリプトを有効化して動けるようにする
        foreach (var script in cameraScripts)
        {
            script.enabled = true;
        }

        // ゲーム本編開始を記録
        if (recordSessionData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent("GameplayStart", "IntroFinished");
        }
    }

    void LateUpdate()
    {
        // スクリプト無効化中はカメラを固定
        if (cameraScripts != null)
        {
            foreach (var script in cameraScripts)
            {
                if (!script.enabled)
                {
                    mainCamera.transform.position = initialCamPos;
                    mainCamera.transform.rotation = initialCamRot;
                    break;
                }
            }
        }
    }

    void OnDestroy()
    {
        // シーン終了を記録
        if (recordSessionData && UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
        {
            float playTime = Time.time - sceneStartTime;
            UserSessionManager.Instance.RecordCustomEvent("ForestSceneEnd", $"PlayTime={playTime:F2}s");
        }
    }
}