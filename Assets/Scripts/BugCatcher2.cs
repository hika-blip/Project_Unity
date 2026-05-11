using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine.UI;

public class BugCatcher2 : MonoBehaviour
{
    public List<GameObject> bugs;

    public float catchDistance = 15.0f;
    public float catchAngle = 30.0f;

    private UdpClient udpClient;
    private Thread receiveThread;

    // ジェスチャー検出フラグ(スレッドセーフ)
    private readonly object lockObject = new object();
    private bool gestureDetected = false;

    // デバッグ用
    public TextMeshProUGUI getText;
    public TextMeshProUGUI bugNameText; // 虫の名前表示用テキスト
    public TextMeshProUGUI debugText;

    [Header("虫の表示設定")]
    public float bugDisplayDistance = 2.0f; // メインカメラからの距離
    public float bugDisplayScale = 2.0f; // 表示時のスケール倍率
    public float bugRotationSpeed = 90.0f; // 虫の回転速度(度/秒)
    public Image targetIllustration; // ターゲットイラスト(捕獲時に非表示)

    private int gestureCount = 0;
    private bool isRunning = true;

    // 効果音用
    [Header("効果音設定")]
    public AudioSource audioSource;
    public AudioClip getCatchSound;
    public AudioClip failureCatchSound;

    // テキスト表示クールダウン
    private float lastTextDisplayTime = -999f;
    private float textCooldown = 1.0f;
    private bool isTextDisplaying = false;

    // セッション記録用
    [Header("セッション記録")]
    public bool recordBugCatchData = true;
    private int totalCatchAttempts = 0;
    private int successfulCatches = 0;
    private int failedCatches = 0;

    // スコープ画像切り替え用
    [Header("スコープ表示")]
    public Image scopeImage;
    public Sprite normalScopeSprite;
    public Sprite activeScopeSprite;

    [Header("通常時スコープ設定")]
    [Range(0f, 1f)]
    public float normalScopeAlpha = 1.0f;
    public float normalScopeScaleMin = 0.9f;
    public float normalScopeScaleMax = 1.1f;
    public float normalScopeAnimSpeed = 2.0f;

    [Header("獲得可能時スコープ設定")]
    [Range(0f, 1f)]
    public float activeScopeAlpha = 1.0f;
    public float activeScopeScale = 1.2f;

    private bool isBugInRange = false;
    private RectTransform scopeRectTransform;
    private float normalScopeAnimTimer = 0f;

    // 虫の表示用
    private GameObject currentDisplayBug = null; // 現在表示中の虫
    private Vector3 originalBugPosition; // 虫の元の位置
    private Quaternion originalBugRotation; // 虫の元の回転
    private Vector3 originalBugScale; // 虫の元のスケール
    private Transform originalBugParent; // 虫の元の親
    private List<MonoBehaviour> disabledOutlines = new List<MonoBehaviour>(); // 無効化したアウトライン

    void Start()
    {
        // データ管理クラスを初期化
        BugCatchData.Initialize(bugs.Count);

        // AudioSourceの自動設定
        if (audioSource == null)
        {
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // スコープ画像の初期化
        if (scopeImage != null && normalScopeSprite != null)
        {
            scopeImage.sprite = normalScopeSprite;
            scopeRectTransform = scopeImage.GetComponent<RectTransform>();

            // 初期状態を通常スコープに設定
            Color color = scopeImage.color;
            color.a = normalScopeAlpha;
            scopeImage.color = color;
        }
        else
        {
            Debug.LogWarning("スコープ画像またはスプライトが設定されていません");
        }

        // UDPクライアントの初期化
        try
        {
            udpClient = new UdpClient(9998);
            Debug.Log("UDP受信開始: ポート9998");
        }
        catch (System.Exception e)
        {
            Debug.LogError("UDPクライアント初期化エラー: " + e.Message);
            return;
        }

        // 受信スレッドの開始
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // 全ての虫を表示
        foreach (GameObject bug in bugs)
        {
            bug.SetActive(true);
        }

        if (getText != null)
            getText.gameObject.SetActive(false);

        if (bugNameText != null)
            bugNameText.gameObject.SetActive(false);

        // セッション開始を記録
        if (recordBugCatchData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent("BugCatcherStart", $"TotalBugs={bugs.Count}");
        }

        // BugInfoコンポーネントの確認
        ValidateBugInfoComponents();
    }

    /// <summary>
    /// 全ての虫にBugInfoコンポーネントがアタッチされているか確認
    /// </summary>
    void ValidateBugInfoComponents()
    {
        for (int i = 0; i < bugs.Count; i++)
        {
            if (bugs[i] == null)
            {
                Debug.LogWarning($"bugsリストのインデックス{i}がnullです");
                continue;
            }

            BugInfo bugInfo = bugs[i].GetComponent<BugInfo>();
            if (bugInfo == null)
            {
                Debug.LogWarning($"{bugs[i].name}にBugInfoコンポーネントがアタッチされていません");
            }
            else
            {
                Debug.Log($"虫を確認: インデックス={i}, ID={bugInfo.bugId}, 名前={bugInfo.bugName}");
            }
        }
    }

    void Update()
    {
        // ジェスチャーフラグをチェック(スレッドセーフ)
        bool gestureThisFrame = false;
        lock (lockObject)
        {
            gestureThisFrame = gestureDetected;
            gestureDetected = false;
        }

        // スコープ状態を更新(常に)
        UpdateScopeDisplay();

        // 通常時のスケールアニメーション
        if (!isBugInRange && scopeRectTransform != null)
        {
            normalScopeAnimTimer += Time.deltaTime * normalScopeAnimSpeed;
            float scale = Mathf.Lerp(normalScopeScaleMin, normalScopeScaleMax,
                                    (Mathf.Sin(normalScopeAnimTimer) + 1f) / 2f);
            scopeRectTransform.localScale = Vector3.one * scale;
        }

        // デバッグ情報の表示
        if (debugText != null)
        {
            int caughtCount = BugCatchData.GetCaughtCount();
            int totalCount = BugCatchData.GetTotalCount();
            float timeSinceLastText = Time.time - lastTextDisplayTime;
            float cooldownRemaining = Mathf.Max(0, textCooldown - timeSinceLastText);

            string userInfo = "";
            if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
            {
                userInfo = $"User: {UserSessionManager.Instance.CurrentUserID}\n";
            }

            debugText.text = userInfo +
                           $"獲得状況: {caughtCount}/{totalCount}\n" +
                           $"ジェスチャー: {(gestureThisFrame ? "検出!" : "待機中")}\n" +
                           $"受信回数: {gestureCount}\n" +
                           $"成功/失敗: {successfulCatches}/{failedCatches}\n" +
                           $"スコープ: {(isBugInRange ? "獲得可能" : "通常")}\n" +
                           $"クールダウン: {(isTextDisplaying ? $"{cooldownRemaining:F1}秒" : "準備完了")}";
        }

        // ジェスチャー検出時の処理(クールダウン中は無視)
        if (gestureThisFrame && !isTextDisplaying)
        {
            CheckCatch();
        }

        // 表示中の虫を回転させる
        if (currentDisplayBug != null)
        {
            currentDisplayBug.transform.Rotate(Vector3.up, bugRotationSpeed * Time.deltaTime);
        }
    }

    void UpdateScopeDisplay()
    {
        if (scopeImage == null || normalScopeSprite == null || activeScopeSprite == null)
            return;

        Camera cam = Camera.main;
        Vector3 cameraPos = cam.transform.position;
        Vector3 cameraForward = cam.transform.forward;

        bool bugFound = false;

        // 獲得可能な虫があるかチェック
        for (int i = 0; i < bugs.Count; i++)
        {
            // すでに獲得済みの虫はスキップ
            if (BugCatchData.IsCaught(i))
                continue;

            GameObject bug = bugs[i];
            Vector3 toBug = bug.transform.position - cameraPos;
            float distance = toBug.magnitude;
            float angle = Vector3.Angle(cameraForward, toBug);

            // 捕獲範囲内かチェック
            if (distance < catchDistance && angle < catchAngle)
            {
                bugFound = true;
                break;
            }
        }

        // スコープ画像を切り替え
        if (bugFound != isBugInRange)
        {
            isBugInRange = bugFound;

            if (isBugInRange)
            {
                // 獲得可能時:固定サイズ
                scopeImage.sprite = activeScopeSprite;
                Color color = scopeImage.color;
                color.a = activeScopeAlpha;
                scopeImage.color = color;

                if (scopeRectTransform != null)
                {
                    scopeRectTransform.localScale = Vector3.one * activeScopeScale;
                }
            }
            else
            {
                // 通常時:アニメーションするのでスプライトとアルファのみ設定
                scopeImage.sprite = normalScopeSprite;
                Color color = scopeImage.color;
                color.a = normalScopeAlpha;
                scopeImage.color = color;
                // スケールはUpdate()でアニメーション
            }
        }
    }

    void CheckCatch()
    {
        totalCatchAttempts++;

        Camera cam = Camera.main;
        Vector3 cameraPos = cam.transform.position;
        Vector3 cameraForward = cam.transform.forward;

        GameObject closestBug = null;
        float closestDistance = float.MaxValue;
        int closestIndex = -1;

        // カメラに最も近い、未獲得の虫を探す
        for (int i = 0; i < bugs.Count; i++)
        {
            // すでに獲得済みの虫はスキップ
            if (BugCatchData.IsCaught(i))
                continue;

            GameObject bug = bugs[i];

            // nullチェック
            if (bug == null)
                continue;

            Vector3 toBug = bug.transform.position - cameraPos;
            float distance = toBug.magnitude;
            float angle = Vector3.Angle(cameraForward, toBug);

            // 捕獲範囲内かチェック
            if (distance < catchDistance && angle < catchAngle)
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestBug = bug;
                    closestIndex = i;
                }
            }
        }

        // 最も近い虫を捕まえる
        if (closestBug != null && closestIndex >= 0)
        {
            Debug.Log($"捕獲: bugsリストのインデックス {closestIndex} の虫");
            CatchBug(closestBug, closestIndex, closestDistance);
        }
        else
        {
            // 捕獲失敗のフィードバック
            ShowFailureFeedback();
        }
    }

    void ShowFailureFeedback()
    {
        failedCatches++;

        Camera cam = Camera.main;
        Vector3 cameraPos = cam.transform.position;
        Vector3 cameraForward = cam.transform.forward;

        // 最も近い未獲得の虫を探してフィードバック
        float minDistance = float.MaxValue;
        float minAngle = float.MaxValue;

        for (int i = 0; i < bugs.Count; i++)
        {
            if (BugCatchData.IsCaught(i))
                continue;

            GameObject bug = bugs[i];
            Vector3 toBug = bug.transform.position - cameraPos;
            float distance = toBug.magnitude;
            float angle = Vector3.Angle(cameraForward, toBug);

            if (distance < minDistance)
                minDistance = distance;
            if (angle < minAngle)
                minAngle = angle;
        }

        string feedbackText = "";
        string failReason = "";

        if (minDistance > catchDistance)
        {
            feedbackText = "ちかづいて!";
            failReason = "TooFar";
        }
        else if (minAngle > catchAngle)
        {
            feedbackText = "むしをまんなかに!";
            failReason = "WrongAngle";
        }
        else
        {
            feedbackText = "もういちど!";
            failReason = "NoBugInRange";
        }

        // セッションに失敗を記録
        if (recordBugCatchData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent(
                "BugCatchFailed",
                $"Reason={failReason},Distance={minDistance:F2},Angle={minAngle:F1}"
            );
        }

        // 失敗音を再生
        PlaySound(failureCatchSound);

        if (getText != null)
        {
            StartCoroutine(ShowTextWithCooldown(feedbackText, "", 1.0f));
        }

        Debug.Log($"捕獲失敗 - 理由:{failReason}, 距離: {minDistance:F2} (必要: <{catchDistance}), 角度: {minAngle:F1} (必要: <{catchAngle})");
    }

    void CatchBug(GameObject bug, int bugIndex, float distance)
    {
        successfulCatches++;

        // 獲得状態を記録
        BugCatchData.SetCaught(bugIndex);

        // BugInfoコンポーネントから虫の名前とIDを取得
        string bugName = "";
        int bugId = -1;

        BugInfo bugInfo = bug.GetComponent<BugInfo>();
        if (bugInfo != null)
        {
            bugName = bugInfo.bugName;
            bugId = bugInfo.bugId;
            Debug.Log($"虫のインデックス: {bugIndex}, ID: {bugId}, 名前: {bugName}");
        }
        else
        {
            bugName = $"虫 #{bugIndex}";
            Debug.LogWarning($"{bug.name}にBugInfoコンポーネントが見つかりません。インデックス: {bugIndex}");
        }

        Debug.Log($"{bugName} を捕まえた! ({BugCatchData.GetCaughtCount()}/{BugCatchData.GetTotalCount()})");

        // セッションに成功を記録
        if (recordBugCatchData && UserSessionManager.Instance != null)
        {
            int caughtCount = BugCatchData.GetCaughtCount();
            int totalCount = BugCatchData.GetTotalCount();
            UserSessionManager.Instance.RecordCustomEvent(
                "BugCatchSuccess",
                $"BugName={bugName},BugID={bugId},BugIndex={bugIndex},Distance={distance:F2},Progress={caughtCount}/{totalCount}"
            );
        }

        // Get音を再生
        PlaySound(getCatchSound);

        if (getText != null)
        {
            // 虫を画面中央に表示
            StartCoroutine(ShowBugAndTextWithCooldown(bug, "つかまえた!", bugName, 1.0f));
        }

        // 全て捕まえたかチェック
        if (BugCatchData.GetCaughtCount() >= BugCatchData.GetTotalCount())
        {
            OnAllBugsCaught();
        }
    }

    void OnAllBugsCaught()
    {
        Debug.Log("全ての虫を捕まえました!");

        // セッションに完了を記録
        if (recordBugCatchData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent(
                "AllBugsCaught",
                $"Attempts={totalCatchAttempts},Success={successfulCatches},Failed={failedCatches}"
            );
        }

        if (debugText != null)
        {
            debugText.text = "全ての虫を捕まえました!";
        }
    }

    void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else if (clip == null)
        {
            Debug.LogWarning("効果音が設定されていません");
        }
    }

    System.Collections.IEnumerator ShowBugAndTextWithCooldown(GameObject bug, string text, string bugName, float displayDuration)
    {
        isTextDisplaying = true;

        // ターゲットイラストを非表示
        if (targetIllustration != null)
        {
            targetIllustration.gameObject.SetActive(false);
        }

        // 虫の元の状態を保存
        originalBugPosition = bug.transform.position;
        originalBugRotation = bug.transform.rotation;
        originalBugScale = bug.transform.localScale;
        originalBugParent = bug.transform.parent;

        // アウトラインを無効化
        DisableOutlines(bug);

        // メインカメラの前に虫を配置
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // カメラの前方に配置
            Vector3 displayPosition = mainCam.transform.position + mainCam.transform.forward * bugDisplayDistance;

            bug.transform.SetParent(null); // 親を解除(ワールド座標で操作)
            bug.transform.position = displayPosition;
            bug.transform.rotation = Quaternion.Euler(0, 0, 0); // 初期回転
            bug.transform.localScale = originalBugScale * bugDisplayScale; // スケールを拡大
        }

        // 現在表示中の虫を設定(回転用)
        currentDisplayBug = bug;
        bug.SetActive(true);

        // "Get!"テキストを表示
        if (getText != null)
        {
            getText.gameObject.SetActive(true);
            getText.text = text;
        }

        // 虫の名前を表示
        if (bugNameText != null && !string.IsNullOrEmpty(bugName))
        {
            bugNameText.gameObject.SetActive(true);
            bugNameText.text = bugName;
        }

        // テキスト表示時間
        yield return new WaitForSeconds(displayDuration);

        // テキストを非表示
        if (getText != null)
            getText.gameObject.SetActive(false);
        if (bugNameText != null)
            bugNameText.gameObject.SetActive(false);

        // アウトラインを再有効化
        EnableOutlines();

        // 虫を元の状態に戻してから非表示
        bug.transform.SetParent(originalBugParent);
        bug.transform.position = originalBugPosition;
        bug.transform.rotation = originalBugRotation;
        bug.transform.localScale = originalBugScale;
        bug.SetActive(false);

        currentDisplayBug = null;

        // ターゲットイラストを再表示
        if (targetIllustration != null)
        {
            targetIllustration.gameObject.SetActive(true);
        }

        // クールダウン開始
        lastTextDisplayTime = Time.time;

        // クールダウン時間待機
        yield return new WaitForSeconds(textCooldown - displayDuration);

        isTextDisplaying = false;
    }

    void DisableOutlines(GameObject bug)
    {
        // 無効化リストをクリア
        disabledOutlines.Clear();

        // Quick Outlineコンポーネントを探して無効化
        // Quick Outlineは "Outline" という名前のコンポーネントの可能性が高い
        MonoBehaviour[] outlineComponents = bug.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (MonoBehaviour component in outlineComponents)
        {
            // コンポーネントの型名に "Outline" が含まれているかチェック
            if (component.GetType().Name.Contains("Outline"))
            {
                if (component.enabled)
                {
                    component.enabled = false;
                    disabledOutlines.Add(component);
                    Debug.Log($"アウトラインを無効化: {component.GetType().Name}");
                }
            }
        }
    }

    void EnableOutlines()
    {
        // 無効化したアウトラインを再有効化
        foreach (MonoBehaviour outline in disabledOutlines)
        {
            if (outline != null)
            {
                outline.enabled = true;
                Debug.Log($"アウトラインを再有効化: {outline.GetType().Name}");
            }
        }

        disabledOutlines.Clear();
    }

    System.Collections.IEnumerator ShowTextWithCooldown(string text, string bugName, float displayDuration)
    {
        isTextDisplaying = true;

        // "Get!"テキストを表示
        if (getText != null)
        {
            getText.gameObject.SetActive(true);
            getText.text = text;
        }

        // 虫の名前を表示
        if (bugNameText != null && !string.IsNullOrEmpty(bugName))
        {
            bugNameText.gameObject.SetActive(true);
            bugNameText.text = bugName;
        }

        // テキスト表示時間
        yield return new WaitForSeconds(displayDuration);

        if (getText != null)
            getText.gameObject.SetActive(false);
        if (bugNameText != null)
            bugNameText.gameObject.SetActive(false);

        // クールダウン開始
        lastTextDisplayTime = Time.time;

        // クールダウン時間待機
        yield return new WaitForSeconds(textCooldown - displayDuration);

        isTextDisplaying = false;
    }

    void ReceiveData()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 9998);

        try
        {
            while (isRunning)
            {
                if (udpClient.Available > 0)
                {
                    byte[] data = udpClient.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data).Trim();

                    if (message == "GESTURE_DETECTED")
                    {
                        lock (lockObject)
                        {
                            gestureDetected = true;
                            gestureCount++;
                        }
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        catch (SocketException e)
        {
            Debug.LogError($"UDP受信エラー: {e.Message}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"UDP受信エラー: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;

        if (udpClient != null)
        {
            udpClient.Close();
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join();
        }
    }

    void OnDestroy()
    {
        OnApplicationQuit();

        // 最終統計を記録
        if (recordBugCatchData && UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
        {
            int finalCaught = BugCatchData.GetCaughtCount();
            int finalTotal = BugCatchData.GetTotalCount();
            UserSessionManager.Instance.RecordCustomEvent(
                "BugCatcherEnd",
                $"FinalScore={finalCaught}/{finalTotal},Attempts={totalCatchAttempts},Success={successfulCatches},Failed={failedCatches}"
            );
        }
    }
}