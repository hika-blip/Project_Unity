using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;

public class BugCatcher : MonoBehaviour
{
    public List<GameObject> bugs;

    public float catchDistance = 15.0f;
    public float catchAngle = 30.0f;

    private UdpClient udpClient;
    private Thread receiveThread;

    // ジェスチャー検出フラグ（スレッドセーフ）
    private readonly object lockObject = new object();
    private bool gestureDetected = false;

    // デバッグ用
    public TextMeshProUGUI getText;
    public TextMeshProUGUI debugText;

    private int gestureCount = 0;
    private bool isRunning = true;

    // 効果音用
    [Header("効果音設定")]
    public AudioSource audioSource;
    public AudioClip getCatchSound;      // Get時の音
    public AudioClip failureCatchSound;  // 失敗時の音

    // テキスト表示クールダウン
    private float lastTextDisplayTime = -999f;
    private float textCooldown = 0.0f;    // テキスト表示後のクールダウン時間（秒）
    private bool isTextDisplaying = false;

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
    }

    void Update()
    {
        // ジェスチャーフラグをチェック（スレッドセーフ）
        bool gestureThisFrame = false;
        lock (lockObject)
        {
            gestureThisFrame = gestureDetected;
            gestureDetected = false;
        }

        // デバッグ情報の表示
        if (debugText != null)
        {
            int caughtCount = BugCatchData.GetCaughtCount();
            int totalCount = BugCatchData.GetTotalCount();
            float timeSinceLastText = Time.time - lastTextDisplayTime;
            float cooldownRemaining = Mathf.Max(0, textCooldown - timeSinceLastText);

            debugText.text = $"獲得状況: {caughtCount}/{totalCount}\n" +
                           $"ジェスチャー: {(gestureThisFrame ? "検出!" : "待機中")}\n" +
                           $"受信回数: {gestureCount}\n" +
                           $"クールダウン: {(isTextDisplaying ? $"{cooldownRemaining:F1}秒" : "準備完了")}";
        }

        // ジェスチャー検出時の処理（クールダウン中は無視）
        if (gestureThisFrame && !isTextDisplaying)
        {
            CheckCatch();
        }
    }

    void CheckCatch()
    {
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
        if (closestBug != null)
        {
            CatchBug(closestBug, closestIndex);
        }
        else
        {
            // 捕獲失敗のフィードバック
            ShowFailureFeedback();
        }
    }

    void ShowFailureFeedback()
    {
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
        if (minDistance > catchDistance)
        {
            feedbackText = "ちかづいて！";
        }
        else if (minAngle > catchAngle)
        {
            feedbackText = "むしををまんなかに！";
        }
        else
        {
            feedbackText = "もういちど！";
        }

        // 失敗音を再生
        PlaySound(failureCatchSound);

        if (getText != null)
        {
            StartCoroutine(ShowTextWithCooldown(feedbackText, 1.0f));
        }

        Debug.Log($"捕獲失敗 - 最近距離: {minDistance:F2} (必要: <{catchDistance}), 最小角度: {minAngle:F1} (必要: <{catchAngle})");
    }

    void CatchBug(GameObject bug, int bugIndex)
    {
        // 獲得状態を記録
        BugCatchData.SetCaught(bugIndex);

        // 虫を非表示にする
        bug.SetActive(false);

        Debug.Log($"虫 #{bugIndex} を捕まえた！ ({BugCatchData.GetCaughtCount()}/{BugCatchData.GetTotalCount()})");

        // Get音を再生
        PlaySound(getCatchSound);

        if (getText != null)
        {
            StartCoroutine(ShowTextWithCooldown("Get!", 1.0f));
        }
    }

    /// <summary>
    /// 効果音を再生する
    /// </summary>
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

    /// <summary>
    /// テキストを表示し、クールダウンを設定する
    /// </summary>
    System.Collections.IEnumerator ShowTextWithCooldown(string text, float displayDuration)
    {
        isTextDisplaying = true;
        getText.gameObject.SetActive(true);
        getText.text = text;

        // テキスト表示時間
        yield return new WaitForSeconds(displayDuration);
        getText.gameObject.SetActive(false);

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
                    Thread.Sleep(10); // CPU無駄食い防止
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
        isRunning = false;  // スレッド終了フラグを下ろす

        if (udpClient != null)
        {
            udpClient.Close();
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(); // 強制Abortではなく安全に終了を待つ
        }
    }

    void OnDestroy()
    {
        OnApplicationQuit();
    }
}