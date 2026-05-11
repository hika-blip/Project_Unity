using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ZoneSelector : MonoBehaviour
{
    [Header("UI (必ずセット)")]
    public RectTransform cursor;        // UI上のカーソル(RectTransform)
    public Image[] zoneImages;          // ゾーン用の Image を7個割り当てる (Zone1〜Zone7)
    public Text headerText;             // 任意: 上部表示用テキスト

    [Header("Canvas Settings")]
    [Tooltip("カーソルを表示するCanvas")]
    public Canvas targetCanvas;

    [Header("Ending遷移設定")]
    [Tooltip("右下に配置する終了ボタンのRectTransform")]
    public RectTransform endingButton;

    [Tooltip("終了ボタンの画像コンポーネント")]
    public Image endingButtonImage;

    [Tooltip("終了ボタン上に滞在する必要がある時間(秒)")]
    public float endingHoverTime = 2f;

    [Tooltip("遷移先のシーン名")]
    public string endingSceneName = "Ending";

    [Header("Ending キーボード操作")]
    [Tooltip("Endingに移動するキー")]
    public KeyCode endingKey = KeyCode.E;

    [Tooltip("代替キー")]
    public KeyCode alternativeEndingKey = KeyCode.Return;

    [Header("Ending視覚フィードバック")]
    [Tooltip("通常時の色")]
    public Color endingNormalColor = Color.white;

    [Tooltip("ホバー時の色")]
    public Color endingHoverColor = Color.yellow;

    [Tooltip("決定時の色")]
    public Color endingSelectedColor = Color.green;

    [Tooltip("進捗バーを表示するか")]
    public bool showEndingProgressBar = true;

    [Tooltip("進捗バーの画像")]
    public Image endingProgressBarImage;

    [Header("動作設定")]
    public float dwellTime = 2f;        // 決定までの時間(秒)

    [Header("Movement Settings")]
    [Tooltip("座標のスケール調整(X軸)")]
    public float movementScaleX = 1.0f;

    [Tooltip("座標のスケール調整(Y軸)")]
    public float movementScaleY = 1.0f;

    [Tooltip("スムージング時間")]
    public float smoothTime = 0.1f;

    [Header("Coordinate Settings")]
    [Tooltip("Y軸を反転するか")]
    public bool invertY = true;

    [Tooltip("X軸を反転するか")]
    public bool invertX = false;

    [Header("デバッグ")]
    public bool debugLogZoneChange = false;
    public bool showDebugInfo = true;

    // --- UDP受信用 ---
    private UdpClient udpClient;
    private Thread udpThread;
    private volatile float wristX = 0f;
    private volatile float wristY = 0f;
    private volatile bool hasData = false;
    private volatile bool running = false;
    private readonly object udpLock = new object();

    // --- 内部状態 ---
    private Vector2 currentPosition = Vector2.zero;
    private Vector2 targetPosition = Vector2.zero;
    private Vector2 velocity = Vector2.zero;
    private int currentZone = -1;
    private float zoneEnterTime = 0f;

    // --- Ending遷移用 ---
    private bool isHoveringEnding = false;
    private float endingEnterTime = 0f;
    private bool isTransitioning = false;
    private Camera canvasCamera;

    // デバッグ用
    private int messagesReceived = 0;
    private string lastMessage = "No data";

    void Start()
    {
        // 必要な参照がセットされているかチェック
        if (cursor == null)
        {
            Debug.LogError("[ZoneSelector] cursor が未設定です。Inspectorで割り当ててください。");
            enabled = false;
            return;
        }
        if (zoneImages == null || zoneImages.Length < 7)
        {
            Debug.LogError("[ZoneSelector] zoneImages に 7つの Image を割り当ててください (Zone1〜Zone7)。");
            enabled = false;
            return;
        }

        // カーソルの初期化
        cursor.anchorMin = new Vector2(0.5f, 0.5f);
        cursor.anchorMax = new Vector2(0.5f, 0.5f);
        cursor.pivot = new Vector2(0.5f, 0.5f);
        Debug.Log("[ZoneSelector] Cursor initialized");

        // Canvasの自動検出
        if (targetCanvas == null)
        {
            targetCanvas = cursor.GetComponentInParent<Canvas>();
        }

        if (targetCanvas == null)
        {
            Debug.LogError("[ZoneSelector] Canvas is not assigned!");
        }

        if (headerText != null) headerText.text = "選択してください";

        // Ending関連の初期化
        if (endingButton != null)
        {
            if (endingButtonImage != null)
            {
                endingButtonImage.color = endingNormalColor;
            }

            if (endingProgressBarImage != null)
            {
                endingProgressBarImage.fillAmount = 0f;
            }

            // Canvasカメラの取得
            if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvasCamera = targetCanvas.worldCamera;
                Debug.Log("[ZoneSelector] Canvas is Screen Space - Camera mode");
            }
        }

        // UDP受信開始
        StartUDPReceiver();
    }

    void StartUDPReceiver()
    {
        lock (udpLock)
        {
            try
            {
                if (udpClient != null)
                {
                    Debug.Log("[ZoneSelector] Closing existing UDP client");
                    StopUDPReceiverInternal();
                    Thread.Sleep(150);
                }

                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 9996));
                udpClient.Client.ReceiveTimeout = 1000;

                running = true;
                udpThread = new Thread(ReceiveData);
                udpThread.IsBackground = true;
                udpThread.Start();

                Debug.Log("[ZoneSelector] UDP Receiver started on port 9996");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[ZoneSelector] UDP開始に失敗: " + ex.Message);

                if (udpClient != null)
                {
                    try { udpClient.Close(); } catch { }
                    udpClient = null;
                }
            }
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                if (udpClient != null && udpClient.Client != null)
                {
                    byte[] data = udpClient.Receive(ref remoteEP);
                    string text = Encoding.UTF8.GetString(data);
                    string[] parts = text.Split(',');

                    if (parts.Length == 2)
                    {
                        if (float.TryParse(parts[0], out float x) &&
                            float.TryParse(parts[1], out float y))
                        {
                            wristX = x;
                            wristY = y;
                            hasData = true;
                            messagesReceived++;
                            lastMessage = text;
                        }
                    }
                }
                else
                {
                    break;
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.TimedOut && running)
                {
                    Debug.LogWarning($"[ZoneSelector] UDP Socket Error: {e.Message}");
                }
            }
            catch (System.ObjectDisposedException)
            {
                break;
            }
            catch (System.Exception ex)
            {
                if (running)
                {
                    Debug.LogWarning("[ZoneSelector] UDP受信エラー: " + ex.Message);
                }
                Thread.Sleep(100);
            }
        }
        Debug.Log("[ZoneSelector] UDP receiver thread stopped");
    }

    void Update()
    {
        if (isTransitioning) return;

        // キーボード入力チェック(Ending遷移)
        if (Input.GetKeyDown(endingKey) || Input.GetKeyDown(alternativeEndingKey))
        {
            Debug.Log($"[ZoneSelector] Ending key pressed ({endingKey} or {alternativeEndingKey})");
            TransitionToEnding();
            return;
        }

        // カーソル位置の更新
        UpdateCursorPosition();

        // Endingボタンの判定(優先)
        if (endingButton != null)
        {
            bool isInsideEnding = IsPointInsideEndingButton();

            if (isInsideEnding)
            {
                if (!isHoveringEnding)
                {
                    isHoveringEnding = true;
                    endingEnterTime = Time.time;
                    if (endingButtonImage != null)
                    {
                        endingButtonImage.color = endingHoverColor;
                    }
                    Debug.Log("[ZoneSelector] Ending button hover started");
                }

                float elapsed = Time.time - endingEnterTime;

                if (endingProgressBarImage != null && showEndingProgressBar)
                {
                    endingProgressBarImage.fillAmount = elapsed / endingHoverTime;
                }

                if (elapsed >= endingHoverTime)
                {
                    // Endingシーンに遷移
                    if (endingButtonImage != null)
                    {
                        endingButtonImage.color = endingSelectedColor;
                    }

                    if (endingProgressBarImage != null)
                    {
                        endingProgressBarImage.fillAmount = 1f;
                    }

                    Debug.Log("[ZoneSelector] Ending button selected");
                    TransitionToEnding();
                }

                return; // Endingボタンホバー中はゾーン判定をスキップ
            }
            else
            {
                if (isHoveringEnding)
                {
                    ResetEndingHoverState();
                }
            }
        }

        // ゾーン判定
        int zone = GetZoneFromPosition();

        // 全ゾーンを透明にリセット
        for (int i = 0; i < zoneImages.Length; i++)
        {
            zoneImages[i].color = new Color(1f, 1f, 1f, 0f);
        }

        // 有効ゾーン(1..7)だけ色付け
        if (zone >= 1 && zone <= zoneImages.Length)
        {
            if (zone != currentZone)
            {
                currentZone = zone;
                zoneEnterTime = Time.time;
                if (debugLogZoneChange) Debug.Log($"[ZoneSelector] Enter zone {zone}");
            }

            float elapsed = Time.time - zoneEnterTime;
            if (elapsed < dwellTime)
            {
                float t = Mathf.Clamp01(elapsed / dwellTime);
                zoneImages[zone - 1].color = Color.Lerp(new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 0f, 0.85f), t);
            }
            else
            {
                // 決定: 緑にする
                zoneImages[zone - 1].color = new Color(0f, 1f, 0f, 0.95f);
                Debug.Log($"[ZoneSelector] Zone {zone} selected");
                SelectionData.SelectedZone = zone;

                // セッションにゾーン選択を記録
                if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
                {
                    UserSessionManager.Instance.RecordZoneSelection(zone);
                }

                // Observationシーンに遷移
                TransitionToObservation();
            }
        }
        else
        {
            currentZone = -1;
        }
    }

    void UpdateCursorPosition()
    {
        if (cursor == null || targetCanvas == null) return;

        if (hasData)
        {
            RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
            float canvasWidth = canvasRect.rect.width;
            float canvasHeight = canvasRect.rect.height;

            // 正規化座標を取得
            float normalizedX = wristX;
            float normalizedY = wristY;

            // 反転オプション
            if (invertX) normalizedX = -normalizedX;
            if (invertY) normalizedY = -normalizedY;

            // スケール適用
            normalizedX *= movementScaleX;
            normalizedY *= movementScaleY;

            // クランプ
            normalizedX = Mathf.Clamp(normalizedX, -1f, 1f);
            normalizedY = Mathf.Clamp(normalizedY, -1f, 1f);

            // Canvas座標系に変換
            targetPosition.x = normalizedX * canvasWidth * 0.5f;
            targetPosition.y = normalizedY * canvasHeight * 0.5f;

            // スムージング(SmoothDamp使用)
            currentPosition = Vector2.SmoothDamp(currentPosition, targetPosition, ref velocity, smoothTime);

            // カーソルの位置を更新
            cursor.anchoredPosition = currentPosition;
        }
    }

    bool IsPointInsideEndingButton()
    {
        if (endingButton == null || cursor == null) return false;

        // Screen Space - Cameraの場合
        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && canvasCamera != null)
        {
            Vector2 cursorScreenPos = RectTransformUtility.WorldToScreenPoint(canvasCamera, cursor.position);

            Vector2 localPoint;
            bool isInRect = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                endingButton,
                cursorScreenPos,
                canvasCamera,
                out localPoint
            );

            if (!isInRect) return false;

            return endingButton.rect.Contains(localPoint);
        }
        // Screen Space - Overlayの場合
        else if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Vector2 cursorScreenPos = cursor.position;
            Rect buttonRect = GetScreenRect(endingButton);
            return buttonRect.Contains(cursorScreenPos);
        }
        else
        {
            return RectTransformUtility.RectangleContainsScreenPoint(endingButton, cursor.position, canvasCamera);
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

    void ResetEndingHoverState()
    {
        isHoveringEnding = false;
        endingEnterTime = 0f;

        if (endingButtonImage != null)
        {
            endingButtonImage.color = endingNormalColor;
        }

        if (endingProgressBarImage != null && showEndingProgressBar)
        {
            endingProgressBarImage.fillAmount = 0f;
        }

        Debug.Log("[ZoneSelector] Ending button hover ended");
    }

    void TransitionToEnding()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        Debug.Log($"[ZoneSelector] Transitioning to scene '{endingSceneName}'");

        StopUDPReceiverInternal();

        // シーン名でロード(Build Settingsの順番に関係なく動作)
        int sceneIndex = FindSceneIndex(endingSceneName);
        if (sceneIndex >= 0)
        {
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError($"[ZoneSelector] Scene '{endingSceneName}' not found in build settings!");
            LogAvailableScenes();
            isTransitioning = false;
        }
    }

    void TransitionToObservation()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        Debug.Log("[ZoneSelector] Transitioning to Observation scene");

        StopUDPReceiverInternal();

        int sceneIndex = FindSceneIndex("Observation");
        if (sceneIndex >= 0)
        {
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError("[ZoneSelector] Scene 'Observation' not found in build settings!");
            LogAvailableScenes();
            isTransitioning = false;
        }
    }

    int FindSceneIndex(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneNameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (sceneNameInBuild.Equals(sceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    void LogAvailableScenes()
    {
        Debug.Log("=== Available scenes in Build Settings ===");
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneNameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            Debug.Log($"  [{i}] {sceneNameInBuild}");
        }
    }

    int GetZoneFromPosition()
    {
        // 各ゾーンのRectTransformから正確な範囲を取得して判定
        for (int i = 0; i < zoneImages.Length; i++)
        {
            if (zoneImages[i] != null)
            {
                RectTransform zoneRect = zoneImages[i].rectTransform;

                // Screen Space - Cameraの場合
                if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && canvasCamera != null)
                {
                    Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(canvasCamera, cursor.position);
                    Vector2 localPoint;
                    bool isInRect = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        zoneRect,
                        screenPos,
                        canvasCamera,
                        out localPoint
                    );

                    if (isInRect && zoneRect.rect.Contains(localPoint))
                    {
                        return i + 1; // ゾーン番号は1から始まる
                    }
                }
                // Screen Space - Overlayの場合
                else if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    Rect zoneScreenRect = GetScreenRect(zoneRect);
                    if (zoneScreenRect.Contains(cursor.position))
                    {
                        return i + 1;
                    }
                }
                else
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(zoneRect, cursor.position, canvasCamera))
                    {
                        return i + 1;
                    }
                }
            }
        }

        return -1; // どのゾーンにも入っていない
    }

    //void OnGUI()
    //{
    //    if (!showDebugInfo) return;

    //    GUILayout.BeginArea(new Rect(10, 10, 400, 300));
    //    GUILayout.Label("=== Zone Selector Debug ===");
    //    GUILayout.Label($"UDP Running: {running}");
    //    GUILayout.Label($"Has Data: {hasData}");
    //    GUILayout.Label($"Messages Received: {messagesReceived}");
    //    GUILayout.Label($"Last Message: {lastMessage}");
    //    GUILayout.Label($"Raw Wrist: ({wristX:F3}, {wristY:F3})");
    //    GUILayout.Label($"Cursor Position: ({currentPosition.x:F1}, {currentPosition.y:F1})");
    //    GUILayout.Label("---");
    //    GUILayout.Label($"Current Zone: {currentZone}");
    //    GUILayout.Label($"Hovering Ending: {isHoveringEnding}");
    //    if (isHoveringEnding)
    //    {
    //        GUILayout.Label($"Ending Progress: {((Time.time - endingEnterTime) / endingHoverTime * 100f):F1}%");
    //    }
    //    GUILayout.Label($"Ending Key: {endingKey} or {alternativeEndingKey}");
    //    GUILayout.EndArea();
    //}

    void StopUDPReceiverInternal()
    {
        running = false;

        if (udpThread != null && udpThread.IsAlive)
        {
            bool joined = udpThread.Join(1500);
            if (!joined)
            {
                Debug.LogWarning("[ZoneSelector] UDP receiver thread did not stop in time");
            }
            udpThread = null;
        }

        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ZoneSelector] Error closing UDP client: {e.Message}");
            }
            finally
            {
                udpClient.Dispose();
                udpClient = null;
            }
        }

        Debug.Log("[ZoneSelector] UDP Receiver stopped");
    }

    void OnDisable()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }

    void OnApplicationQuit()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }

    void OnDestroy()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }
}