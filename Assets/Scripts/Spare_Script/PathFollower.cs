using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class PathFollower : MonoBehaviour
{
    [Header("パス設定")]
    [Tooltip("カメラが通過する座標リスト")]
    public Vector3[] pathPoints = new Vector3[]
    {
        new Vector3(0, 1.6f, 0),
        new Vector3(10, 1.6f, 10),
        new Vector3(20, 1.6f, 15),
        new Vector3(30, 1.6f, 10)
    };

    [Header("移動設定")]
    [Tooltip("速度の最小値(カメラに近い時)")]
    public float minSpeed = 0.5f;

    [Tooltip("速度の最大値(カメラから遠い時)")]
    public float maxSpeed = 4.0f;

    [Tooltip("曲線の滑らかさ(大きいほど滑らか)")]
    [Range(10, 200)]
    public int curveResolution = 50;

    [Header("視点回転設定")]
    [Tooltip("視点回転の滑らかさ(小さいほど滑らか、0.1～1.0推奨)")]
    [Range(0.01f, 1.0f)]
    public float rotationSmoothness = 0.1f;

    [Tooltip("先読み距離(m) - この距離先の方向を向く")]
    [Range(0.5f, 5.0f)]
    public float lookAheadDistance = 2.0f;

    [Header("追従対象")]
    [Tooltip("パスに沿って動かすオブジェクト(通常はカメラ)")]
    public Transform followTarget;

    [Header("ゲーム終了設定")]
    [Tooltip("ゲーム終了メッセージを表示するテキスト")]
    public TextMeshProUGUI gameOverText;

    [Header("縁どり")]
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;  // 縁取りの太さ（0～1）
    public Color outlineColor = Color.black;  // 縁取りの色

    [Tooltip("移動先のシーン名")]
    public string nextSceneName = "Observation_selector";

    [Tooltip("メッセージ表示時間(秒)")]
    public float messageDisplayTime = 2f;

    [Header("デバッグ表示")]
    public bool showPath = true;
    public Color pathColor = Color.green;
    public bool showDebugInfo = true;

    // UDP受信用（ポート9990）
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;
    private volatile float receivedZ = 0.5f; // YOLOから受信したz値(0~1)
    private float currentMoveSpeed = 2.0f;
    private Camera_Move cameraMove;

    private Vector3[] curvePoints;
    private float currentDistance = 0f;
    private float totalPathLength = 0f;
    private bool isGameOver = false;

    // 滑らかな回転用
    private Quaternion targetRotation;
    private Quaternion currentRotation;

    void Start()
    {
        if (followTarget == null)
        {
            followTarget = Camera.main.transform;
        }

        // ゲーム終了テキストを非表示にしておく
        if (gameOverText != null)
        {
            gameOverText.gameObject.SetActive(false);
        }

        // Camera_Moveの参照を取得
        cameraMove = followTarget.GetComponent<Camera_Move>();
        if (cameraMove == null)
        {
            Debug.LogWarning("PathFollower: Camera_Move component not found on followTarget!");
        }

        GenerateSmoothCurve();

        // 初期のパス方向を設定
        if (curvePoints != null && curvePoints.Length > 1)
        {
            Vector3 initialForward = (curvePoints[1] - curvePoints[0]).normalized;
            if (initialForward != Vector3.zero)
            {
                Quaternion initialRotation = Quaternion.LookRotation(initialForward);
                currentRotation = initialRotation;
                targetRotation = initialRotation;
                if (cameraMove != null)
                {
                    cameraMove.baseRotation = initialRotation;
                }
            }
        }

        // UDP受信開始（ポート9990）
        StartUDPReceiver();
    }

    void StartUDPReceiver()
    {
        try
        {
            udpClient = new UdpClient(9990); // ポート9990を使用
            udpClient.Client.ReceiveTimeout = 1000;

            isRunning = true;
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("PathFollower: UDP Receiver started on port 9990");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"PathFollower: Failed to start UDP receiver: {e.Message}");
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 9990);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string text = Encoding.UTF8.GetString(data);

                // データ形式: "z" の1つの値（例: "0.7234"）
                if (float.TryParse(text, out float z))
                {
                    receivedZ = z;
                }
            }
            catch (SocketException)
            {
                continue;
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"PathFollower UDP Receive Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }
    }

    void Update()
    {
        if (curvePoints == null || curvePoints.Length < 2)
            return;

        if (isGameOver)
            return;

        // receivedZ (0~1) に基づいて移動速度を計算
        currentMoveSpeed = Mathf.Lerp(minSpeed, maxSpeed, receivedZ);

        // パスに沿って移動
        currentDistance += currentMoveSpeed * Time.deltaTime;

        // パスの終わりに到達したらゲーム終了
        if (currentDistance >= totalPathLength)
        {
            StartGameOver();
            return;
        }

        // 現在位置を取得
        Vector3 newPosition = GetPositionAtDistance(currentDistance);

        // 先読み位置での進行方向を取得（より滑らかな回転のため）
        float lookAheadDist = Mathf.Min(currentDistance + lookAheadDistance, totalPathLength);
        Vector3 lookAheadForward = GetForwardAtDistance(lookAheadDist);

        // ターゲットの位置を更新
        followTarget.position = newPosition;

        // 目標の回転を計算
        if (lookAheadForward != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(lookAheadForward);
        }

        // 現在の回転から目標の回転へ滑らかに補間
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, rotationSmoothness);

        // Camera_MoveスクリプトのbaseRotationを更新
        if (cameraMove != null)
        {
            cameraMove.baseRotation = currentRotation;
        }
    }

    // ゲーム終了処理を開始
    void StartGameOver()
    {
        isGameOver = true;
        StartCoroutine(GameOverSequence());
    }

    // ゲーム終了シーケンス
    System.Collections.IEnumerator GameOverSequence()
    {
        // テキストに縁取りを適用
        ApplyOutline(gameOverText);

        // ゲーム終了メッセージを表示
        if (gameOverText != null)
        {
            gameOverText.gameObject.SetActive(true);
            gameOverText.text = "ゲームおわり！";
        }

        // 指定秒数待機
        yield return new WaitForSeconds(messageDisplayTime);

        // 次のシーンに移動
        LoadNextScene();
    }

    // TextMeshProに縁取りを適用するメソッド
    void ApplyOutline(TextMeshProUGUI textComponent)
    {
        if (textComponent != null)
        {
            // アウトラインを有効化
            textComponent.outlineWidth = outlineWidth;
            textComponent.outlineColor = outlineColor;
        }
    }

    // 次のシーンを読み込む
    void LoadNextScene()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
    }

    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(10, 250, 400, 250));
        GUILayout.Label("=== Path Follower Debug ===");
        GUILayout.Label($"Current Distance: {currentDistance:F2} / {totalPathLength:F2}");
        GUILayout.Label($"Received Z (Port 9990): {receivedZ:F3}");
        GUILayout.Label($"Current Speed: {currentMoveSpeed:F2} m/s");
        GUILayout.Label($"Speed Range: {minSpeed:F1} ~ {maxSpeed:F1}");
        GUILayout.Label($"Speed Calc: Lerp({minSpeed:F1}, {maxSpeed:F1}, {receivedZ:F3})");
        GUILayout.Label($"Rotation Smoothness: {rotationSmoothness:F2}");
        GUILayout.Label($"Look Ahead Distance: {lookAheadDistance:F2}m");

        if (cameraMove != null)
        {
            GUILayout.Label($"Target Rotation: {targetRotation.eulerAngles}");
            GUILayout.Label($"Current Rotation: {currentRotation.eulerAngles}");
        }

        GUILayout.EndArea();
    }

    // Catmull-Romスプライン補間で滑らかな曲線を生成
    void GenerateSmoothCurve()
    {
        if (pathPoints.Length < 2)
        {
            Debug.LogWarning("パスポイントが2つ以上必要です");
            return;
        }

        curvePoints = new Vector3[curveResolution * (pathPoints.Length - 1)];
        int index = 0;
        totalPathLength = 0f;

        for (int i = 0; i < pathPoints.Length - 1; i++)
        {
            Vector3 p0 = i > 0 ? pathPoints[i - 1] : pathPoints[i];
            Vector3 p1 = pathPoints[i];
            Vector3 p2 = pathPoints[i + 1];
            Vector3 p3 = i + 2 < pathPoints.Length ? pathPoints[i + 2] : pathPoints[i + 1];

            Vector3 previousPoint = p1;

            for (int j = 0; j < curveResolution; j++)
            {
                float t = j / (float)curveResolution;
                Vector3 point = CalculateCatmullRom(p0, p1, p2, p3, t);
                curvePoints[index++] = point;

                if (j > 0)
                {
                    totalPathLength += Vector3.Distance(previousPoint, point);
                }
                previousPoint = point;
            }
        }

        Debug.Log($"パス生成完了: 総距離 {totalPathLength:F2}m, ポイント数 {curvePoints.Length}");
    }

    // Catmull-Romスプライン計算
    Vector3 CalculateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    // 指定距離の位置を取得
    Vector3 GetPositionAtDistance(float distance)
    {
        float accumulatedDistance = 0f;

        for (int i = 0; i < curvePoints.Length - 1; i++)
        {
            float segmentLength = Vector3.Distance(curvePoints[i], curvePoints[i + 1]);

            if (accumulatedDistance + segmentLength >= distance)
            {
                float t = (distance - accumulatedDistance) / segmentLength;
                return Vector3.Lerp(curvePoints[i], curvePoints[i + 1], t);
            }

            accumulatedDistance += segmentLength;
        }

        return curvePoints[curvePoints.Length - 1];
    }

    // 指定距離での進行方向を取得
    Vector3 GetForwardAtDistance(float distance)
    {
        float accumulatedDistance = 0f;

        for (int i = 0; i < curvePoints.Length - 1; i++)
        {
            float segmentLength = Vector3.Distance(curvePoints[i], curvePoints[i + 1]);

            if (accumulatedDistance + segmentLength >= distance)
            {
                return (curvePoints[i + 1] - curvePoints[i]).normalized;
            }

            accumulatedDistance += segmentLength;
        }

        return Vector3.forward;
    }

    // Sceneビューにパスを表示
    void OnDrawGizmos()
    {
        if (!showPath || pathPoints == null || pathPoints.Length < 2)
            return;

        // 設定したポイントを赤い球で表示
        Gizmos.color = Color.red;
        foreach (Vector3 point in pathPoints)
        {
            Gizmos.DrawSphere(point, 0.3f);
        }

        // 曲線を緑の線で表示
        if (curvePoints != null && curvePoints.Length > 1)
        {
            Gizmos.color = pathColor;
            for (int i = 0; i < curvePoints.Length - 1; i++)
            {
                Gizmos.DrawLine(curvePoints[i], curvePoints[i + 1]);
            }
        }
        else
        {
            // Start前は直線で表示
            Gizmos.color = Color.yellow;
            for (int i = 0; i < pathPoints.Length - 1; i++)
            {
                Gizmos.DrawLine(pathPoints[i], pathPoints[i + 1]);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            if (receiveThread.IsAlive)
            {
                receiveThread.Abort();
            }
        }

        if (udpClient != null)
        {
            udpClient.Close();
        }
        Debug.Log("PathFollower: UDP Receiver stopped");
    }

    void OnDestroy()
    {
        OnApplicationQuit();
    }
}