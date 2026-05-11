using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class CameraHeadTracking : MonoBehaviour
{
    // UDP通信用
    private UdpClient client;
    private Thread receiveThread;
    private bool isRunning = false;

    [Header("UDP Settings")]
    [Tooltip("UDP受信ポート番号")]
    public int udpPort = 9999;

    // 基準位置・回転
    [HideInInspector]
    public Quaternion baseRotation;
    private Vector3 basePosition;

    // 受信データ
    private volatile float noseX = 0f;
    private volatile float noseY = 0f;
    private volatile float noseZ = 0.5f;
    private volatile bool hasReceivedData = false;

    [Header("注視点設定")]
    [Tooltip("カメラが注視する対象物（立方体など）")]
    public Transform lookAtTarget;

    [Tooltip("注視点のオフセット")]
    public Vector3 lookAtOffset = Vector3.zero;

    [Header("球面座標パラメータ")]
    [Tooltip("カメラから注視点までの基準距離")]
    public float baseDistance = 3f;

    [Tooltip("水平方向の角度範囲（度）")]
    public float horizontalAngleRange = 45f;

    [Tooltip("垂直方向の角度範囲（度）")]
    public float verticalAngleRange = 30f;

    [Tooltip("距離の変化範囲")]
    public float distanceRange = 1f;

    [Tooltip("基準となるZ距離")]
    public float referenceDistance = 0.5f;

    [Header("楕円軌道パラメータ")]
    [Tooltip("楕円の横方向の半径（水平方向の距離）")]
    public float ellipseRadiusX = 3f;

    [Tooltip("楕円の前後方向の半径（奥行き方向の距離）")]
    public float ellipseRadiusZ = 2f;

    [Tooltip("横から見たときに近づく距離の減少率（0-1）")]
    [Range(0f, 1f)]
    public float sideViewDistanceReduction = 0.3f;

    [Header("スムージングパラメータ")]
    [Tooltip("角度のスムージング時間")]
    public float angleSmoothTime = 0.12f;

    [Tooltip("距離のスムージング時間")]
    public float distanceSmoothTime = 0.15f;

    // スムージング用変数
    private float currentNoseX = 0f;
    private float currentNoseY = 0f;
    private float currentNoseZ = 0.5f;
    private float velX = 0f;
    private float velY = 0f;
    private float velZ = 0f;

    private float currentHorizontalAngle = 0f;
    private float currentVerticalAngle = 0f;
    private float currentDistance;
    private float horizontalAngleVelocity = 0f;
    private float verticalAngleVelocity = 0f;
    private float distanceVelocity = 0f;

    // デバッグ情報
    private string lastReceivedData = "No data received";
    private int totalMessagesReceived = 0;
    private float lastReceiveTime = 0f;

    [Header("デバッグ")]
    public bool showDebugGUI = true;
    public bool showDebugGizmos = true;

    void Start()
    {
        StartUDPListener();
        baseRotation = transform.rotation;
        basePosition = transform.position;
        currentDistance = baseDistance;

        // lookAtTargetが設定されていない場合は自動で探す
        if (lookAtTarget == null)
        {
            Debug.LogWarning("CameraHeadTracking: lookAtTarget is not set. Please assign a target object in the Inspector.");
        }
    }

    void StartUDPListener()
    {
        try
        {
            // 既存の接続があれば先に閉じる
            if (client != null)
            {
                Debug.Log("CameraHeadTracking: Closing existing UDP client before reinitializing");
                StopUDPListener();
                Thread.Sleep(100);
            }

            // UdpClientを作成し、ReuseAddressオプションを設定
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
            client.Client.ReceiveTimeout = 1000;

            isRunning = true;
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log($"CameraHeadTracking: UDP Listener started successfully on port {udpPort}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"CameraHeadTracking: Failed to start UDP listener: {e.Message}");
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = client.Receive(ref remoteEndPoint);
                string text = Encoding.UTF8.GetString(data);

                string[] parts = text.Split(',');
                if (parts.Length == 3)
                {
                    if (float.TryParse(parts[0], out float x) &&
                        float.TryParse(parts[1], out float y) &&
                        float.TryParse(parts[2], out float z))
                    {
                        noseX = x;
                        noseY = y;
                        noseZ = z;
                        hasReceivedData = true;
                        lastReceivedData = text;
                        totalMessagesReceived++;
                    }
                }
            }
            catch (SocketException e)
            {
                // タイムアウトは正常な動作なのでログを出さない
                if (e.SocketErrorCode != SocketError.TimedOut && isRunning)
                {
                    Debug.LogWarning($"CameraHeadTracking: UDP Socket Error: {e.Message}");
                }
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"CameraHeadTracking: UDP Receive Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }

        Debug.Log("CameraHeadTracking: UDP listener thread stopped");
    }

    void Update()
    {
        if (lookAtTarget == null)
        {
            return;
        }

        if (!hasReceivedData)
        {
            lastReceiveTime = Time.time;
        }
        else
        {
            lastReceiveTime = Time.time;
        }

        // 受信データのスムージング
        currentNoseX = Mathf.SmoothDamp(currentNoseX, noseX, ref velX, angleSmoothTime);
        currentNoseY = Mathf.SmoothDamp(currentNoseY, noseY, ref velY, angleSmoothTime);
        currentNoseZ = Mathf.SmoothDamp(currentNoseZ, noseZ, ref velZ, distanceSmoothTime);

        // 頭部の位置を正規化
        float normalizedX = Mathf.Clamp(currentNoseX, -1f, 1f);
        float normalizedY = Mathf.Clamp(currentNoseY, -1f, 1f);
        float normalizedZ = Mathf.Clamp((currentNoseZ - referenceDistance) / referenceDistance, -1f, 1f);

        // 球面座標系での角度を計算
        // 頭が右に動いたら、カメラも右に回り込む（水平角度が増加）
        float targetHorizontalAngle = normalizedX * horizontalAngleRange;
        // 頭が上に動いたら、カメラは下に移動（垂直角度が減少）
        float targetVerticalAngle = -normalizedY * verticalAngleRange;
        // 頭が前に動いたら、カメラが遠ざかる
        float targetDistance = baseDistance + (normalizedZ * distanceRange);

        // スムーズに角度と距離を更新
        currentHorizontalAngle = Mathf.SmoothDampAngle(currentHorizontalAngle, targetHorizontalAngle, ref horizontalAngleVelocity, angleSmoothTime);
        currentVerticalAngle = Mathf.SmoothDampAngle(currentVerticalAngle, targetVerticalAngle, ref verticalAngleVelocity, angleSmoothTime);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, distanceSmoothTime);

        // 注視点の位置を取得
        Vector3 lookAtPoint = lookAtTarget.position + lookAtOffset;

        // 球面座標から楕円座標に変換してカメラ位置を計算
        float horizontalRad = currentHorizontalAngle * Mathf.Deg2Rad;
        float verticalRad = currentVerticalAngle * Mathf.Deg2Rad;

        // 水平角度に応じた距離の調整（横から見たときに近づく）
        // cos(0°) = 1 (正面) → 距離減少なし
        // cos(90°) = 0 (真横) → 最大距離減少
        float horizontalFactor = Mathf.Abs(Mathf.Cos(horizontalRad));
        float distanceModifier = 1f - (1f - horizontalFactor) * sideViewDistanceReduction;
        float adjustedDistance = currentDistance * distanceModifier;

        // 楕円軌道での位置計算
        // X軸: 楕円の横方向（ellipseRadiusX）
        // Z軸: 楕円の前後方向（ellipseRadiusZ）
        float verticalDistance = adjustedDistance * Mathf.Cos(verticalRad);

        Vector3 offset = new Vector3(
            ellipseRadiusX * Mathf.Sin(horizontalRad) * Mathf.Cos(verticalRad),
            adjustedDistance * Mathf.Sin(verticalRad),
            ellipseRadiusZ * Mathf.Cos(horizontalRad) * Mathf.Cos(verticalRad)
        );

        // カメラの位置を設定（基準回転を考慮）
        transform.position = lookAtPoint + baseRotation * offset;

        // カメラを注視点に向ける
        transform.LookAt(lookAtPoint);
    }

    public float GetCurrentNoseZ()
    {
        return currentNoseZ;
    }

    public Vector3 GetCurrentHeadPosition()
    {
        return new Vector3(currentNoseX, currentNoseY, currentNoseZ);
    }

    public Vector3 GetSphericalCoordinates()
    {
        return new Vector3(currentHorizontalAngle, currentVerticalAngle, currentDistance);
    }

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUILayout.BeginArea(new Rect(10, 10, 450, 320));
        GUILayout.Label("=== Camera Head Tracking (Elliptical) ===");
        GUILayout.Label($"UDP Status: {(isRunning ? "Running" : "Stopped")}");
        GUILayout.Label($"Messages Received: {totalMessagesReceived}");
        GUILayout.Label($"Last Data: {lastReceivedData}");
        GUILayout.Label($"Time Since Last: {Time.time - lastReceiveTime:F1}s");
        GUILayout.Label($"Raw Position: ({noseX:F3}, {noseY:F3}, {noseZ:F3})");
        GUILayout.Label($"Smoothed Position: ({currentNoseX:F3}, {currentNoseY:F3}, {currentNoseZ:F3})");
        GUILayout.Label($"Horizontal Angle: {currentHorizontalAngle:F1}°");
        GUILayout.Label($"Vertical Angle: {currentVerticalAngle:F1}°");
        GUILayout.Label($"Distance: {currentDistance:F2}m");
        GUILayout.Label($"Ellipse: ({ellipseRadiusX:F2}, {ellipseRadiusZ:F2})");
        GUILayout.Label($"Look At Target: {(lookAtTarget != null ? lookAtTarget.name : "Not Set")}");
        GUILayout.EndArea();
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || lookAtTarget == null) return;

        Vector3 lookAtPoint = lookAtTarget.position + lookAtOffset;

        // 注視点を表示
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lookAtPoint, 0.1f);

        // カメラから注視点への線
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, lookAtPoint);

        // カメラの位置
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.05f);

        // 楕円軌道を表示（水平方向）- XZ平面
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        int segments = 64;
        Vector3 prevPoint = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-horizontalAngleRange, horizontalAngleRange, i / (float)segments) * Mathf.Deg2Rad;

            // 楕円の計算
            Vector3 point = lookAtPoint + baseRotation * new Vector3(
                ellipseRadiusX * Mathf.Sin(angle),
                0f,
                ellipseRadiusZ * Mathf.Cos(angle)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, point);
            }
            prevPoint = point;
        }

        // 垂直方向の軌道を表示（中央）
        Gizmos.color = new Color(1f, 0f, 1f, 0.5f);
        prevPoint = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-verticalAngleRange, verticalAngleRange, i / (float)segments) * Mathf.Deg2Rad;
            float dist = baseDistance * Mathf.Cos(angle);
            Vector3 point = lookAtPoint + baseRotation * new Vector3(
                0f,
                baseDistance * Mathf.Sin(angle),
                ellipseRadiusZ
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, point);
            }
            prevPoint = point;
        }

        // 横から見たときの楕円を表示（側面、距離減少を考慮）
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        prevPoint = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-horizontalAngleRange, horizontalAngleRange, i / (float)segments) * Mathf.Deg2Rad;

            // 横から見たときの距離減少を計算
            float horizontalFactor = Mathf.Abs(Mathf.Cos(angle));
            float distanceModifier = 1f - (1f - horizontalFactor) * sideViewDistanceReduction;
            float adjustedEllipseRadiusX = ellipseRadiusX * distanceModifier;
            float adjustedEllipseRadiusZ = ellipseRadiusZ * distanceModifier;

            Vector3 point = lookAtPoint + baseRotation * new Vector3(
                adjustedEllipseRadiusX * Mathf.Sin(angle),
                0f,
                adjustedEllipseRadiusZ * Mathf.Cos(angle)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, point);
            }
            prevPoint = point;
        }
    }

    void StopUDPListener()
    {
        isRunning = false;

        // スレッドの終了を待つ
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            if (receiveThread.IsAlive)
            {
                Debug.LogWarning("CameraHeadTracking: UDP receiver thread did not stop in time");
            }
        }

        // UdpClientを確実に閉じる
        if (client != null)
        {
            try
            {
                client.Close();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CameraHeadTracking: Error closing UDP client: {e.Message}");
            }
            finally
            {
                client = null;
            }
        }

        Debug.Log("CameraHeadTracking: UDP Listener stopped");
    }

    void OnDisable()
    {
        StopUDPListener();
    }

    void OnApplicationQuit()
    {
        StopUDPListener();
    }

    void OnDestroy()
    {
        StopUDPListener();
    }
}