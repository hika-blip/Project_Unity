using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 手首カーソルで「戻る」エリアを選ぶ
/// ・左右の手首のうちカメラに近い方を「有効な手」とする（InsectSelectMenuと同じ）
/// ・有効な手と同じ側の肩を基準に、手首の位置（肩幅で割った値）でカーソルを動かす
/// ・手を下ろしているとき（手首が肩より一定以上下）はカーソルを消し、戻る判定もしない
/// ・カーソルが一定時間「戻る」エリアに重なっていたら、指定したシーンへ移動
///
/// 受信するUDP（dual_camera_gesture_udp.py）
///   9995 左手首 "x,y,z" / 9994 右手首 "x,y,z"
///   9992 左肩   "x,y,z" / 9991 右肩   "x,y,z"
/// ※UDPの受信には InsectSelectMenu.cs 内の UdpFloatReceiver クラスを使う
/// </summary>
public class WristCursorSceneController : MonoBehaviour
{
    public enum HandPickMode
    {
        SmallerZ,      // zが小さい方（カメラに近い方）
        CloserToZero   // |z|が0に近い方
    }

    [Header("UDPポート")]
    public int leftWristPort = 9995;
    public int rightWristPort = 9994;
    public int leftShoulderPort = 9992;
    public int rightShoulderPort = 9991;
    [Tooltip("手首・肩のデータがこの秒数届かなければ、検出できていないとみなす")]
    public float dataTimeout = 0.3f;

    [Header("有効な手の選び方")]
    [Tooltip("SmallerZ: カメラに近い方（zが小さい方）／CloserToZero: |z|が0に近い方")]
    public HandPickMode handPickMode = HandPickMode.SmallerZ;
    [Tooltip("もう片方の手の方がこれ以上カメラに近いときだけ、使う手を切り替える")]
    public float handSwitchMargin = 0.05f;

    [Header("手を下ろしている判定（値は「肩幅の何倍か」）")]
    [Tooltip("手首が肩より肩幅のこの倍数以上下にあるときは「手を下ろしている＝操作していない」扱い")]
    public float handDownLimit = 1.0f;
    [Tooltip("手を下ろしているときはカーソルを非表示にする")]
    public bool hideCursorWhenHandDown = true;
    [Tooltip("両肩が見えていないときに使う肩幅（正規化座標）")]
    public float defaultShoulderWidth = 0.35f;

    [Header("Cursor Settings")]
    [Tooltip("カーソルオブジェクトのRectTransform")]
    public RectTransform cursorObject;

    [Tooltip("カーソルを表示するCanvas")]
    public Canvas targetCanvas;

    [Header("カーソルの動き（値は「肩幅の何倍か」）")]
    [Tooltip("手首を肩から肩幅のこの倍数だけ横に出すと、カーソルが画面の左右端に届く")]
    public float reachX = 1.5f;
    [Tooltip("手首を基準の高さから肩幅のこの倍数だけ上下させると、カーソルが画面の上下端に届く")]
    public float reachY = 1.0f;
    [Tooltip("手首がこの高さ（肩からの差。プラスで肩より上）のとき、カーソルが画面の上下中央に来る")]
    public float centerHeight = 0f;

    [Tooltip("スムージング時間")]
    public float smoothTime = 0.1f;

    [Header("Coordinate Settings")]
    [Tooltip("手を右に出したのにカーソルが左に動く場合は切り替える")]
    public bool invertX = true;
    [Tooltip("手を上げたのにカーソルが下に動く場合は切り替える")]
    public bool invertY = false;

    [Header("Scene Transition Settings")]
    [Tooltip("遷移先のシーン名")]
    public string targetSceneName = "Observation_selecter";

    [Header("Button Settings")]
    [Tooltip("戻るボタンのRectTransform")]
    public RectTransform backButton;

    [Tooltip("ボタンの画像コンポーネント（色変更用）")]
    public Image buttonImage;

    [Tooltip("ボタン上に滞在する必要がある時間（秒）")]
    public float hoverTimeRequired = 2.0f;

    [Tooltip("判定の余白（ピクセル）")]
    public float hitboxPadding = 10f;

    [Header("Visual Feedback")]
    [Tooltip("通常時のボタン色")]
    public Color normalColor = Color.white;

    [Tooltip("ホバー時のボタン色")]
    public Color hoverColor = Color.yellow;

    [Tooltip("決定時のボタン色")]
    public Color selectedColor = Color.green;

    [Tooltip("進捗バーを表示するか")]
    public bool showProgressBar = true;

    [Tooltip("進捗バーの画像")]
    public Image progressBarImage;

    [Header("Keyboard Input")]
    [Tooltip("戻るためのキー")]
    public KeyCode backKey = KeyCode.Escape;

    [Tooltip("代替キー")]
    public KeyCode alternativeBackKey = KeyCode.Backspace;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // UDP受信
    private UdpFloatReceiver lWristRecv, rWristRecv, lShoulderRecv, rShoulderRecv;

    // 手の状態
    private int activeHand = -1;      // -1:なし 0:左手 1:右手
    private float shoulderWidth;
    private bool handOperating = false; // 有効な手を上げている（カーソル操作中）
    private bool cursorPlaced = false;  // 一度でもカーソル位置を決めたか
    private Vector2 handRatio;          // 肩からの手首の位置（肩幅で割った値）
    private float downRatio;
    private string handState = "-";

    // スムージング用
    private Vector2 currentPosition = Vector2.zero;
    private Vector2 targetPosition = Vector2.zero;
    private Vector2 velocity = Vector2.zero;

    // ボタンインタラクション用
    private float currentHoverTime = 0f;
    private bool isHovering = false;
    private bool isTransitioning = false;
    private Camera canvasCamera;

    void Start()
    {
        shoulderWidth = defaultShoulderWidth;

        // カーソルの初期化
        if (cursorObject != null)
        {
            cursorObject.anchorMin = new Vector2(0.5f, 0.5f);
            cursorObject.anchorMax = new Vector2(0.5f, 0.5f);
            cursorObject.pivot = new Vector2(0.5f, 0.5f);
            if (hideCursorWhenHandDown) cursorObject.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError("WristCursorSceneController: Cursor object is not assigned!");
        }

        // Canvasの自動検出
        if (targetCanvas == null && cursorObject != null)
        {
            targetCanvas = cursorObject.GetComponentInParent<Canvas>();
        }

        if (targetCanvas == null)
        {
            Debug.LogError("WristCursorSceneController: Canvas is not assigned!");
        }

        // ボタンの初期化
        if (backButton != null)
        {
            if (buttonImage == null)
            {
                buttonImage = backButton.GetComponent<Image>();
            }

            if (buttonImage != null)
            {
                buttonImage.color = normalColor;
            }

            // Canvasカメラの取得
            if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvasCamera = targetCanvas.worldCamera;
            }
        }

        // 進捗バーの初期化
        if (progressBarImage != null)
        {
            progressBarImage.fillAmount = 0f;
        }

        // UDP受信開始
        StartReceivers();
    }

    void Update()
    {
        if (isTransitioning) return;

        // キーボード入力チェック
        if (Input.GetKeyDown(backKey) || Input.GetKeyDown(alternativeBackKey))
        {
            StartCoroutine(TransitionToScene());
            return;
        }

        // 有効な手と、肩からの手首の位置を求める
        UpdateHand();

        // カーソル位置の更新
        UpdateCursorPosition();

        // ボタンインタラクションの処理
        UpdateButtonInteraction();
    }

    // ------------------------------------------------------------
    // 有効な手の判定（InsectSelectMenuと同じ考え方）
    // ------------------------------------------------------------
    void UpdateHand()
    {
        bool lwOk = TryRead(lWristRecv, out Vector3 lw);
        bool rwOk = TryRead(rWristRecv, out Vector3 rw);
        bool lsOk = TryRead(lShoulderRecv, out Vector3 ls);
        bool rsOk = TryRead(rShoulderRecv, out Vector3 rs);

        // 肩幅を更新（両肩が見えているときだけ。xy平面上の距離）
        if (lsOk && rsOk)
        {
            float w = Vector2.Distance(new Vector2(ls.x, ls.y), new Vector2(rs.x, rs.y));
            if (w > 0.05f) shoulderWidth = Mathf.Lerp(shoulderWidth, w, 0.2f);
        }

        // 手首と同じ側の肩の両方が見えている手だけが候補
        bool leftValid = lwOk && lsOk;
        bool rightValid = rwOk && rsOk;

        // 有効な手を選ぶ（カメラに近い方）
        int candidate = -1;
        if (leftValid && rightValid) candidate = HandScore(lw.z) <= HandScore(rw.z) ? 0 : 1;
        else if (leftValid) candidate = 0;
        else if (rightValid) candidate = 1;

        // 両手の近さがほぼ同じときは今の手を使い続ける（パタパタ防止）
        bool activeStillValid = (activeHand == 0 && leftValid) || (activeHand == 1 && rightValid);
        if (activeStillValid && candidate != activeHand && candidate != -1)
        {
            float cur = HandScore(activeHand == 0 ? lw.z : rw.z);
            float other = HandScore(candidate == 0 ? lw.z : rw.z);
            if (cur - other < handSwitchMargin) candidate = activeHand;
        }
        activeHand = candidate;

        handOperating = false;
        if (activeHand == -1)
        {
            handState = "手が見えていない";
            return;
        }

        Vector3 wrist = activeHand == 0 ? lw : rw;
        Vector3 shoulder = activeHand == 0 ? ls : rs;
        float width = Mathf.Max(shoulderWidth, 0.05f);

        // y は上が正（Python側で変換済み）
        downRatio = (shoulder.y - wrist.y) / width;
        string handName = activeHand == 0 ? "左手" : "右手";

        if (downRatio > handDownLimit)
        {
            handState = $"{handName}（下ろしている）  下:{downRatio:F2}";
            return;
        }

        float dx = (wrist.x - shoulder.x) / width;
        float dy = (wrist.y - shoulder.y) / width;
        if (invertX) dx = -dx;
        if (invertY) dy = -dy;

        handRatio = new Vector2(dx, dy);
        handOperating = true;
        handState = $"{handName}  横:{dx:F2}  縦:{dy:F2}";
    }

    bool TryRead(UdpFloatReceiver recv, out Vector3 v)
    {
        v = Vector3.zero;
        return recv != null && recv.TryGet(out v, dataTimeout);
    }

    // 小さいほど「カメラに近い」
    float HandScore(float z)
    {
        return handPickMode == HandPickMode.SmallerZ ? z : Mathf.Abs(z);
    }

    // ------------------------------------------------------------
    // カーソル
    // ------------------------------------------------------------
    void UpdateCursorPosition()
    {
        if (cursorObject == null || targetCanvas == null) return;

        // 手を下ろしている・見えていないときはカーソルを消す
        if (hideCursorWhenHandDown && cursorObject.gameObject.activeSelf != handOperating)
        {
            cursorObject.gameObject.SetActive(handOperating);
        }

        if (!handOperating)
        {
            // カーソルを消した場合は、次に手を上げたときにその場に出し直す
            if (hideCursorWhenHandDown) cursorPlaced = false;
            return;
        }

        RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
        float canvasWidth = canvasRect.rect.width;
        float canvasHeight = canvasRect.rect.height;

        // 肩からの位置（肩幅単位）→ -1〜1 → Canvas座標
        float nx = Mathf.Clamp(handRatio.x / Mathf.Max(reachX, 0.01f), -1f, 1f);
        float ny = Mathf.Clamp((handRatio.y - centerHeight) / Mathf.Max(reachY, 0.01f), -1f, 1f);

        targetPosition.x = nx * canvasWidth * 0.5f;
        targetPosition.y = ny * canvasHeight * 0.5f;

        // 手を上げ直した直後は、前の位置からスーッと動かずにその場に出す
        if (!cursorPlaced)
        {
            currentPosition = targetPosition;
            velocity = Vector2.zero;
            cursorPlaced = true;
        }
        else
        {
            currentPosition = Vector2.SmoothDamp(currentPosition, targetPosition, ref velocity, smoothTime);
        }

        cursorObject.anchoredPosition = currentPosition;
    }

    // ------------------------------------------------------------
    // 戻るボタン
    // ------------------------------------------------------------
    void UpdateButtonInteraction()
    {
        if (backButton == null || cursorObject == null) return;

        // 手を上げてカーソルを操作しているときだけ判定する
        bool isInside = handOperating && IsPointInsideButton();

        if (isInside)
        {
            if (!isHovering)
            {
                isHovering = true;
                currentHoverTime = 0f;
                if (buttonImage != null)
                {
                    buttonImage.color = hoverColor;
                }
            }

            currentHoverTime += Time.deltaTime;

            if (progressBarImage != null && showProgressBar)
            {
                progressBarImage.fillAmount = currentHoverTime / hoverTimeRequired;
            }

            if (currentHoverTime >= hoverTimeRequired)
            {
                StartCoroutine(TransitionToScene());
            }
        }
        else
        {
            if (isHovering)
            {
                ResetHoverState();
            }
        }
    }

    bool IsPointInsideButton()
    {
        if (backButton == null || cursorObject == null) return false;

        // Screen Space - Cameraの場合
        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && canvasCamera != null)
        {
            Vector2 cursorScreenPos = RectTransformUtility.WorldToScreenPoint(canvasCamera, cursorObject.position);

            Vector2 localPoint;
            bool isInRect = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backButton,
                cursorScreenPos,
                canvasCamera,
                out localPoint
            );

            if (!isInRect) return false;

            Rect rect = backButton.rect;
            rect.xMin -= hitboxPadding;
            rect.xMax += hitboxPadding;
            rect.yMin -= hitboxPadding;
            rect.yMax += hitboxPadding;

            return rect.Contains(localPoint);
        }
        // Screen Space - Overlayの場合
        else if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Vector2 cursorScreenPos = cursorObject.position;
            Rect buttonRect = GetScreenRect(backButton);
            buttonRect.xMin -= hitboxPadding;
            buttonRect.xMax += hitboxPadding;
            buttonRect.yMin -= hitboxPadding;
            buttonRect.yMax += hitboxPadding;

            return buttonRect.Contains(cursorScreenPos);
        }
        else
        {
            return RectTransformUtility.RectangleContainsScreenPoint(backButton, cursorObject.position, canvasCamera);
        }
    }

    Rect GetScreenRect(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        float xMin = corners[0].x;
        float xMax = corners[2].x;
        float yMin = corners[0].y;
        float yMax = corners[2].y;

        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    void ResetHoverState()
    {
        isHovering = false;
        currentHoverTime = 0f;

        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }

        if (progressBarImage != null && showProgressBar)
        {
            progressBarImage.fillAmount = 0f;
        }
    }

    // ------------------------------------------------------------
    // シーン遷移
    // ------------------------------------------------------------
    IEnumerator TransitionToScene()
    {
        if (isTransitioning) yield break;

        isTransitioning = true;

        if (buttonImage != null)
        {
            buttonImage.color = selectedColor;
        }

        if (progressBarImage != null && showProgressBar)
        {
            progressBarImage.fillAmount = 1f;
        }

        int targetSceneIndex = -1;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneNameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);

            // 大文字小文字を無視して比較
            if (sceneNameInBuild.Equals(targetSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                targetSceneIndex = i;
            }
        }

        yield return new WaitForSeconds(0.3f);

        if (targetSceneIndex >= 0)
        {
            // 次のシーンで同じポートを使えるよう、先にUDPを閉じる
            StopReceivers();
            Debug.Log($"WristCursorSceneController: Loading scene '{targetSceneName}' (index {targetSceneIndex})");
            SceneManager.LoadScene(targetSceneIndex);
        }
        else
        {
            Debug.LogError($"WristCursorSceneController: Scene '{targetSceneName}' not found in build settings!");
            Debug.LogError("Make sure the scene is added to File > Build Settings > Scenes In Build");
            isTransitioning = false;
            ResetHoverState();
        }
    }

    [ContextMenu("Test Scene Transition")]
    public void TestTransition()
    {
        StartCoroutine(TransitionToScene());
    }

    // ------------------------------------------------------------
    // UDP
    // ------------------------------------------------------------
    void StartReceivers()
    {
        StopReceivers();
        lWristRecv = new UdpFloatReceiver(leftWristPort);
        rWristRecv = new UdpFloatReceiver(rightWristPort);
        lShoulderRecv = new UdpFloatReceiver(leftShoulderPort);
        rShoulderRecv = new UdpFloatReceiver(rightShoulderPort);
        lWristRecv.Start();
        rWristRecv.Start();
        lShoulderRecv.Start();
        rShoulderRecv.Start();
    }

    void StopReceivers()
    {
        lWristRecv?.Stop();
        rWristRecv?.Stop();
        lShoulderRecv?.Stop();
        rShoulderRecv?.Stop();
        lWristRecv = rWristRecv = lShoulderRecv = rShoulderRecv = null;
    }

    void OnDisable() { StopReceivers(); }
    void OnDestroy() { StopReceivers(); }
    void OnApplicationQuit() { StopReceivers(); }

    void OnGUI()
    {
        if (!showDebugInfo) return;
        GUILayout.BeginArea(new Rect(10, 10, 480, 160));
        GUILayout.Label("=== Wrist Cursor (Back) ===");
        GUILayout.Label($"有効な手: {handState}");
        GUILayout.Label($"肩幅: {shoulderWidth:F3}  下ろし判定: {handDownLimit:F2}");
        GUILayout.Label($"カーソル: ({currentPosition.x:F0}, {currentPosition.y:F0})  操作中: {handOperating}");
        GUILayout.Label($"戻る: {(isHovering ? $"{currentHoverTime:F1} / {hoverTimeRequired:F1}秒" : "-")}");
        GUILayout.EndArea();
    }
}