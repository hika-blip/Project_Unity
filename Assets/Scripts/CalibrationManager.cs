using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;

/// <summary>
/// ユーザーの鼻の座標を受け取ってキャリブレーションを行う
/// </summary>
public class CalibrationManager : MonoBehaviour
{
    [Header("UDP設定")]
    public int udpPort = 9999;

    [Header("キャリブレーション設定")]
    public int calibrationFrames = 30;  // キャリブレーションに使用するフレーム数
    public float calibrationDelay = 5.0f;  // QRコード読み込み後の待機時間（秒）

    [Header("UI設定")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI countdownText;

    // UDP受信用
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;

    // 受信データ（スレッドセーフ）
    private readonly object lockObject = new object();
    private Vector3 latestPosition = Vector3.zero;
    private bool hasReceivedData = false;

    // キャリブレーション用
    private List<Vector3> calibrationSamples = new List<Vector3>();
    private Vector3 calibrationOffset = Vector3.zero;
    public Vector3 CalibrationOffset => calibrationOffset;

    // 状態管理
    public bool IsCalibrated { get; private set; } = false;
    public bool IsCalibrating { get; private set; } = false;

    void Awake()
    {
        // すでに存在するインスタンスがある場合は、自分を破棄
        var existingManagers = FindObjectsOfType<CalibrationManager>();
        if (existingManagers.Length > 1)
        {
            Debug.Log("CalibrationManager: Duplicate detected. Destroying the new instance.");
            Destroy(gameObject);
            return;
        }

        // 初回だけ生き残る
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // UDP受信開始
        StartUDPReceiver();
    }

    void StartUDPReceiver()
    {
        try
        {
            udpClient = new UdpClient(udpPort);
            //Debug.Log($"UDP受信開始: ポート{udpPort}");

            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"UDPクライアント初期化エラー: {e.Message}");
        }
    }

    //UDPで三次元座標を取得　正規化
    void ReceiveData()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, udpPort);

        try
        {
            while (isRunning)
            {
                if (udpClient.Available > 0)
                {
                    byte[] data = udpClient.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data).Trim();

                    // "x,y,z" 形式のデータをパース
                    string[] parts = message.Split(',');
                    if (parts.Length == 3)
                    {
                        if (float.TryParse(parts[0], out float x) &&
                            float.TryParse(parts[1], out float y) &&
                            float.TryParse(parts[2], out float z))
                        {
                            lock (lockObject)
                            {
                                latestPosition = new Vector3(x, y, z);
                                hasReceivedData = true;
                            }
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
    }

    /// <summary>
    /// キャリブレーションを開始
    /// </summary>
    public IEnumerator StartCalibration()
    {
        IsCalibrating = true;
        IsCalibrated = false;
        calibrationSamples.Clear();

        //if (statusText != null)
        //    statusText.text = "キャリブレーション...";

        // 遅延待機
        float elapsed = 0f;
        while (elapsed < calibrationDelay)
        {
            elapsed += Time.deltaTime;
            // if (countdownText != null)
            // {
            //     countdownText.text = $"{(calibrationDelay - elapsed):F1}秒";
            // }
            yield return null;
        }

        //if (statusText != null)
        //    statusText.text = "キャリブレーション中...";

        // キャリブレーションデータ収集
        int collectedFrames = 0;
        while (collectedFrames < calibrationFrames)
        {
            bool dataReceived = false;
            Vector3 position = Vector3.zero;

            lock (lockObject)
            {
                if (hasReceivedData)
                {
                    position = latestPosition;
                    dataReceived = true;
                }
            }

            if (dataReceived)
            {
                calibrationSamples.Add(position);
                collectedFrames++;

                // if (countdownText != null)
                // {
                //     float progress = (float)collectedFrames / calibrationFrames * 100f;
                //     //countdownText.text = $"キャリブレーション中...\n{progress:F0}%";
                // }
            }

            yield return new WaitForSeconds(0.033f); // 約30fps
        }

        // 平均を計算してオフセットとする
        if (calibrationSamples.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var sample in calibrationSamples)
            {
                sum += sample;
            }
            calibrationOffset = sum / calibrationSamples.Count;

            IsCalibrated = true;
            Debug.Log($"キャリブレーション完了: オフセット={calibrationOffset}");

            // if (statusText != null)
            //     //statusText.text = "キャリブレーション完了！";
            // if (countdownText != null)
            //     countdownText.text = "準備完了";
        }
        else
        {
            Debug.LogError("キャリブレーションデータを取得できませんでした");
            // if (statusText != null)
            //     statusText.text = "キャリブレーション失敗";
            // if (countdownText != null)
            //     countdownText.text = "データ受信エラー";
        }

        IsCalibrating = false;
    }

    /// <summary>
    /// キャリブレーション済みの座標を取得
    /// </summary>
    public Vector3 GetCalibratedPosition()
    {
        lock (lockObject)
        {
            if (hasReceivedData)
            {
                return latestPosition - calibrationOffset;
            }
        }
        return Vector3.zero;
    }

    /// <summary>
    /// 生の座標を取得
    /// </summary>
    public Vector3 GetRawPosition()
    {
        lock (lockObject)
        {
            return latestPosition;
        }
    }

    // UDPソケットを安全に閉じる処理
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
    }
}