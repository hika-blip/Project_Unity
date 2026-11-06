using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI; 

/// <summary>
/// タイトルシーンでQRコード読み取り、キャリブレーション、シーン遷移を管理
/// </summary>
public class TitleSceneController : MonoBehaviour
{
    [Header("UI要素")]
    public TextMeshProUGUI userIDInputText;
    public TextMeshProUGUI instructionText;

    [Header("キャリブレーションゲージ")]
    public Image calibrationGaugeImage;
    public Image GaugeGuideImage;
    public float calibrationGaugeExtraTime = 1.5f; // calibrationDelayに上乗せする秒数
    private Coroutine gaugeCoroutine; // 実行中のゲージコルーチンを保持

    [Header("Textの上下アニメーション")]
    public float bobAmplitude = 16f;   // 上下に動く幅（px）
    public float bobSpeed = 1.8f;        // 動く速さ

    private RectTransform instructionTextRect;
    private Vector2 instructionTextBasePos;

    [Header("シーン設定")]
    public string nextSceneName = "Forest";

    [Header("キャリブレーション")]
    public CalibrationManager calibrationManager;

    [Header("Python のユーザー判定(9988 で CALIB_OK / CALIB_FAIL を受け取る)")]
    [Tooltip("Python の判定結果を待ってから、Unity のキャリブレーションを始める")]
    public bool usePythonCalibrationResult = true;
    [Tooltip("dual_camera.py の PORT_CALIB_RESULT と同じにする")]
    public int calibResultPort = 9988;
    [Tooltip("この秒数待っても返事がなければ、Python が動いていないとみなしてそのまま進む")]
    public float pythonResultTimeout = 6f;
    [Tooltip("失敗してから、もう一度 START を送るまでの秒数")]
    public float pythonRetryDelay = 0.5f;
    [Tooltip("失敗が続いたらQRコード待ちに戻る回数。0なら成功するまで続ける")]
    public int maxPythonRetries = 0;
    [Tooltip("判定に失敗したときに表示するテキスト")]
    [TextArea] public string calibFailText = "ひとりで黄色いせんに\nたってください";
    [Tooltip("ゲージの長さに足す、Python の判定にかかる秒数のめやす")]
    public float pythonCalibSeconds = 1.0f;

    [Header("取り消し(うまく進まなかったとき用)")]
    [Tooltip("このキーで今のIDを無効にして、QRコードを受け付ける状態に戻す")]
    public KeyCode cancelKey = KeyCode.Escape;
    [TextArea] public string cancelText = "IDをとりけしました";

    [Header("音声(VOICEVOX などで作った .wav を Assets に入れて、ここにドラッグする)")]
    [Tooltip("音声を鳴らす AudioSource。空なら自動で追加する")]
    public AudioSource voiceSource;
    [Range(0f, 1f)] public float voiceVolume = 1f;

    [Tooltip("QRコード待ちのあいだ、上から順番にくり返す音声\n例: 0「虫を捕まえて観察してみよう」1「QRコードをかざしてゲームスタート」")]
    public AudioClip[] idleVoices;
    [Tooltip("1つ流し終わってから、次の音声までの秒数")]
    public float idleVoiceInterval = 2f;

    [Tooltip("QRコードをかざしたあと、判定が終わるまでくり返す音声(「黄色い線に立ってね」)")]
    public AudioClip standOnLineVoice;
    [Tooltip("QRコードをかざしてから、最初に流すまでの秒数")]
    public float standOnLineFirstDelay = 0.5f;
    [Tooltip("流し終わってから、次に流すまでの秒数")]
    public float standOnLineInterval = 4f;
    [Tooltip("Python の判定に失敗したとき、すぐにもう一度流す")]
    public bool replayStandOnLineOnFail = true;

    [Tooltip("キャリブレーションに成功したときに1回流す音声(「準備完了」)")]
    public AudioClip readyVoice;
    [Tooltip("「準備完了」を最後まで流してから次のシーンへ進む(シーンが変わると音声が途中で切れるため)")]
    public bool waitReadyVoiceBeforeSceneChange = true;

    private Coroutine voiceCoroutine;

    private UdpClient calibResultClient;
    private Thread calibResultThread;
    private volatile bool calibResultReceiving = false;
    private readonly Queue<string> calibResults = new Queue<string>();

    // 内部状態
    private string currentInputUserID = "";
    private bool isProcessing = false;

    //QRコード読み取りまで待つ
    private enum State
    {
        WaitingForQRCode,
        Calibrating,
        ReadyToStart
    }
    private State currentState = State.WaitingForQRCode;

    void Start()
    {
        // CalibrationManagerの参照を取得
        if (calibrationManager == null)
        {
            calibrationManager = FindObjectOfType<CalibrationManager>();
        }

        if (instructionText != null)
        {
            instructionTextRect = instructionText.GetComponent<RectTransform>();
            instructionTextBasePos = instructionTextRect.anchoredPosition;
        }

        if (usePythonCalibrationResult) StartCalibResultReceiver();

        if (voiceSource == null) voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;

        UpdateUI();
        StartIdleVoices();
    }

    // ───────── 音声 ─────────

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null || voiceSource == null) return;
        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
    }

    /// <summary>くり返し中の音声を止める(鳴っている途中の音声も止める)</summary>
    private void StopVoices()
    {
        if (voiceCoroutine != null) StopCoroutine(voiceCoroutine);
        voiceCoroutine = null;
        if (voiceSource != null) voiceSource.Stop();
    }

    /// <summary>QRコード待ち: idleVoices を上から順番にくり返す</summary>
    private void StartIdleVoices()
    {
        StopVoices();
        if (idleVoices != null && idleVoices.Length > 0) voiceCoroutine = StartCoroutine(IdleVoiceLoop());
    }

    private IEnumerator IdleVoiceLoop()
    {
        int i = 0;
        while (true)
        {
            AudioClip clip = idleVoices[i % idleVoices.Length];
            i++;
            if (clip != null)
            {
                PlayVoice(clip);
                yield return new WaitForSeconds(clip.length);
            }
            yield return new WaitForSeconds(idleVoiceInterval);
        }
    }

    /// <summary>QRコードをかざしたあと: 「黄色い線に立ってね」を間をあけてくり返す</summary>
    private void StartStandOnLineVoice(float firstDelay)
    {
        StopVoices();
        if (standOnLineVoice != null) voiceCoroutine = StartCoroutine(StandOnLineLoop(firstDelay));
    }

    private IEnumerator StandOnLineLoop(float firstDelay)
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            PlayVoice(standOnLineVoice);
            yield return new WaitForSeconds(standOnLineVoice.length + standOnLineInterval);
        }
    }

    // ───────── Python からの判定結果(9988)の受信 ─────────

    private void StartCalibResultReceiver()
    {
        try
        {
            calibResultClient = new UdpClient();
            calibResultClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            calibResultClient.Client.Bind(new IPEndPoint(IPAddress.Any, calibResultPort));
            calibResultReceiving = true;
            calibResultThread = new Thread(ReceiveCalibResults) { IsBackground = true };
            calibResultThread.Start();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"判定結果の受信(ポート{calibResultPort})を開始できません: {e.Message}");
        }
    }

    private void ReceiveCalibResults()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (calibResultReceiving)
        {
            try
            {
                byte[] data = calibResultClient.Receive(ref remote);
                string msg = Encoding.UTF8.GetString(data).Trim().ToUpper();
                lock (calibResults) calibResults.Enqueue(msg);
            }
            catch (System.Exception)
            {
                if (!calibResultReceiving) break;   // 閉じたときは抜ける
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>受け取った判定結果を1つ取り出す("CALIB_OK" / "CALIB_FAIL")。なければ null</summary>
    private string PopCalibResult()
    {
        lock (calibResults)
        {
            while (calibResults.Count > 0)
            {
                string msg = calibResults.Dequeue();
                if (msg == "CALIB_OK" || msg == "CALIB_FAIL") return msg;
            }
        }
        return null;
    }

    /// <summary>前の判定結果が残っていると取り違えるので、START を送る前に消す</summary>
    private void ClearCalibResults()
    {
        lock (calibResults) calibResults.Clear();
    }

    private void StopCalibResultReceiver()
    {
        calibResultReceiving = false;
        calibResultClient?.Close();   // Receive の待ちを解除する
        calibResultClient = null;
    }

    /// <summary>
    /// Python の判定結果を待つ。失敗したら同じIDのまま START を送り直す。
    /// 戻り値の代わりに pythonCalibSucceeded に結果を入れる(false = QRコード待ちに戻る)
    /// </summary>
    private bool pythonCalibSucceeded;
    private IEnumerator WaitForPythonCalibration()
    {
        pythonCalibSucceeded = false;
        int failCount = 0;
        while (true)
        {
            // 返事を待つ
            string result = null;
            float waited = 0f;
            while (result == null && waited < pythonResultTimeout)
            {
                result = PopCalibResult();
                waited += Time.deltaTime;
                yield return null;
            }

            if (result == "CALIB_OK")
            {
                Debug.Log("Python のユーザー判定: 成功");
                pythonCalibSucceeded = true;
                yield break;
            }
            if (result == null)
            {
                Debug.LogWarning($"Python から {pythonResultTimeout} 秒返事がありません。判定なしで進みます(Python は起動していますか?)");
                pythonCalibSucceeded = true;
                yield break;
            }

            // 失敗: テキストを変えて、同じIDのまま START を送り直す
            failCount++;
            Debug.Log($"Python のユーザー判定: 失敗 ({failCount}回目)");
            if (UserSessionManager.Instance != null)
                UserSessionManager.Instance.RecordCustomEvent("PythonCalibrationFailed", $"Count={failCount}");
            if (instructionText != null) instructionText.text = calibFailText;
            if (replayStandOnLineOnFail) StartStandOnLineVoice(0f);   // 「黄色い線に立ってね」をすぐ流す
            ResetGauge();

            if (maxPythonRetries > 0 && failCount >= maxPythonRetries)
            {
                yield return new WaitForSeconds(2.0f);   // 失敗のテキストを少し見せてから戻る
                yield break;
            }

            yield return new WaitForSeconds(pythonRetryDelay);
            ClearCalibResults();
            if (UserSessionManager.Instance != null) UserSessionManager.Instance.SendPythonStart();
            StartGauge();
        }
    }

    /// <summary>
    /// 今のIDを無効にして、QRコードを受け付ける状態に戻す
    /// </summary>
    private void CancelRegistration()
    {
        // 判定待ち・ゲージ・キャリブレーションなど、このスクリプトで動いている処理をすべて止める
        StopAllCoroutines();
        gaugeCoroutine = null;
        ResetGauge();
        ClearCalibResults();

        // セッションを終わらせる(Python に END が送られ、骨格推定が止まる)
        // CSV には「SessionCancelled」の記録付きで残るので、あとで分析から外せる
        if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
        {
            string id = UserSessionManager.Instance.CurrentUserID;
            UserSessionManager.Instance.RecordCustomEvent("SessionCancelled", $"State={currentState}");
            UserSessionManager.Instance.EndCurrentSession();
            Debug.Log($"ID {id} を取り消しました");
        }

        currentState = State.WaitingForQRCode;
        currentInputUserID = "";
        isProcessing = false;
        UpdateUI();
        if (voiceSource != null) voiceSource.Stop();
        StartIdleVoices();   // StopAllCoroutines で止まった、QRコード待ちの音声を再開する

        // 少しのあいだ「取り消しました」を表示してから、元のテキストに戻す
        if (instructionText != null && !string.IsNullOrEmpty(cancelText))
        {
            instructionText.text = cancelText;
            StartCoroutine(ResetInstructionText(1.5f));
        }
    }

    private void StartGauge()
    {
        if (gaugeCoroutine != null) StopCoroutine(gaugeCoroutine);
        float gaugeDuration = calibrationManager.calibrationDelay + calibrationGaugeExtraTime
                              + (usePythonCalibrationResult ? pythonCalibSeconds : 0f);
        gaugeCoroutine = StartCoroutine(FillCalibrationGauge(gaugeDuration));
    }

    private void ResetGauge()
    {
        if (gaugeCoroutine != null) StopCoroutine(gaugeCoroutine);
        if (calibrationGaugeImage != null) calibrationGaugeImage.fillAmount = 0f;
        if (GaugeGuideImage != null) GaugeGuideImage.fillAmount = 1f;
    }

    void Update()
    {
        // 取り消しキー: 判定中・準備完了のときは今のIDを無効にして、QRコード待ちに戻す
        // (QRコード待ちのときは、入力途中の文字を消す)
        if (Input.GetKeyDown(cancelKey))
        {
            if (isProcessing || currentState != State.WaitingForQRCode)
            {
                CancelRegistration();
            }
            else if (currentInputUserID.Length > 0)
            {
                currentInputUserID = "";
                UpdateUserIDDisplay();
            }
            return;
        }

        if (currentState == State.WaitingForQRCode && !isProcessing)
        {
            HandleUserIDInput();
        }

        if (instructionTextRect != null)
        {
            float offsetY = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            instructionTextRect.anchoredPosition = instructionTextBasePos + new Vector2(0f, offsetY);
        }
    }

    /// <summary>
    /// UserIDの入力を受け付ける関数
    /// </summary>
    private void HandleUserIDInput()
    {
        // pキーを押したとき、QRコードの登録なしのデバッグユーザーとしてコンテンツを開始
        if (Input.GetKeyDown(KeyCode.P))
        {
            //Debug.Log("Pキーでデバッグユーザーを開始します。");
            StartCoroutine(OnRegistrationSuccess("debug_user"));
            return;
        }

        // BackSpaceを押したとき、仮入力中のUserIDを一文字消去
        if (Input.GetKeyDown(KeyCode.Backspace) && currentInputUserID.Length > 0)
        {
            currentInputUserID = currentInputUserID.Substring(0, currentInputUserID.Length - 1);
            UpdateUserIDDisplay();
        }

        // キーボードのpキー以外の文字を押したとき、UserIDを仮入力
        foreach (char c in Input.inputString)
        {
            if (c != '\b' && c != '\n' && c != '\r' && c != 'p' && c != 'P')
            {
                currentInputUserID += c;
                UpdateUserIDDisplay();
            }
        }

        // Enterを押したとき、UserIDが正しいかをチェックする
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ValidateAndStartGame(currentInputUserID.ToUpper());
        }
    }

    /// <summary>
    /// UserIDが正しいかをチェックする関数
    /// </summary>
    private void ValidateAndStartGame(string rawInput)
    {
        Debug.Log("入力されたID文字列: [" + rawInput + "]");

        string finalUserID = "";

        // UserIDが「sc」+「4文字の英数字」であるかを確認
        if (rawInput.StartsWith("SC") && rawInput.Length == 6)
        {
            string hexPart = rawInput.Substring(2);
            if (Regex.IsMatch(hexPart, @"^[0-9A-F]{4}$"))
            {
                finalUserID = hexPart;
            }
        }
        else if (rawInput.Length == 4 && Regex.IsMatch(rawInput, @"^[0-9A-F]{4}$"))
        {
            finalUserID = rawInput;
        }

        // 形式チェック後のUserID(finalUserID)が空であればエラー
        if (!string.IsNullOrEmpty(finalUserID))
        {
            Debug.Log("有効なユーザーID: " + finalUserID);
            StartCoroutine(OnRegistrationSuccess(finalUserID));
        }
        else
        {
            Debug.LogWarning("ユーザーIDの形式が不正です。入力をリセットします。");
            currentInputUserID = "";
            UpdateUserIDDisplay();

            if (instructionText != null)
            {
                instructionText.text = "無効なIDです\nもう一度入力してください";
                StartCoroutine(ResetInstructionText(2.0f));
            }
        }
    }

    /// <summary>
    /// ユーザー登録成功時の処理
    /// </summary>
    private IEnumerator OnRegistrationSuccess(string userID)
    {
        // ★ 1フレーム待ってから探す（DontDestroyOnLoadが登録されるまで）
        yield return null;

        calibrationManager = FindObjectOfType<CalibrationManager>();
        if (calibrationManager == null)
        {
            Debug.LogError("CalibrationManagerが見つかりません");
            yield break;
        }

        if (isProcessing) yield break;
        isProcessing = true;

        // UserSessionManagerにユーザー情報を登録(ここで Python に START が送られる)
        ClearCalibResults();
        if (UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.StartNewSession(userID);
            UserSessionManager.Instance.RecordCustomEvent("QRCodeScanned", userID);
        }

        // 状態を更新
        currentState = State.Calibrating;
        UpdateUI();
        StartStandOnLineVoice(standOnLineFirstDelay);   // QRコード待ちの音声を止めて「黄色い線に立ってね」へ

        // ゲージ開始(Python の判定 + calibrationDelay + 1〜2秒かけて満タンになるように)
        StartGauge();

        // Python のユーザー判定を待つ(失敗したら、同じIDのまま START を送り直す)
        if (usePythonCalibrationResult)
        {
            yield return StartCoroutine(WaitForPythonCalibration());
            if (!pythonCalibSucceeded)
            {
                // 失敗が maxPythonRetries 回続いた: QRコード待ちに戻る
                ResetGauge();
                currentState = State.WaitingForQRCode;
                currentInputUserID = "";
                isProcessing = false;
                UpdateUI();
                StartIdleVoices();
                yield break;
            }
            UpdateUI();   // 失敗のテキストを表示していたら、元のテキストに戻す
        }

        // キャリブレーション開始
        if (calibrationManager != null)
        {
            yield return StartCoroutine(calibrationManager.StartCalibration());

            if (calibrationManager.IsCalibrated)
            {
                // キャリブレーション成功
                currentState = State.ReadyToStart;
                UpdateUI();

                // セッションにキャリブレーション情報を記録
                if (UserSessionManager.Instance != null)
                {
                    Vector3 offset = calibrationManager.CalibrationOffset;
                    UserSessionManager.Instance.RecordCustomEvent(
                        "CalibrationCompleted",
                        $"Offset=({offset.x:F3},{offset.y:F3},{offset.z:F3})"
                    );
                }

                // 「準備完了」を流す
                StopVoices();
                PlayVoice(readyVoice);

                // 少し待ってからシーン遷移(音声があれば、最後まで流れるのを待つ)
                float wait = 1.0f;
                if (readyVoice != null && waitReadyVoiceBeforeSceneChange) wait = Mathf.Max(wait, readyVoice.length);
                yield return new WaitForSeconds(wait);

                // 次のシーンへ
                LoadNextScene();
            }
            else
            {
                // キャリブレーション失敗
                Debug.LogError("キャリブレーションに失敗しました");
                if (instructionText != null)
                {
                    instructionText.text = "キャリブレーション失敗\nもう一度試してください";
                }

                // ★ 失敗時はゲージを止めて0に戻す
                if (gaugeCoroutine != null) StopCoroutine(gaugeCoroutine);
                if (calibrationGaugeImage != null) calibrationGaugeImage.fillAmount = 0f;
                if (GaugeGuideImage != null) GaugeGuideImage.fillAmount = 1f;


                currentState = State.WaitingForQRCode;
                currentInputUserID = "";
                isProcessing = false;
                UpdateUI();
                StartIdleVoices();
            }
        }
        else
        {
            Debug.LogError("CalibrationManagerが見つかりません");
            isProcessing = false;
        }
    }

    /// <summary>
    /// 次のシーンをロード
    /// </summary>
    private void LoadNextScene()
    {
        // シーン遷移を記録
        if (UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordSceneTransition("Title", nextSceneName);
        }

        Debug.Log($"シーン遷移: Title → {nextSceneName}");
        SceneManager.LoadScene(nextSceneName);
    }

    void OnDestroy()
    {
        StopCalibResultReceiver();

        // Titleシーンが破棄される際、セッションは継続（Forestシーンへ引き継ぐ）
        // セッションの終了はForestシーン側で行う
        Debug.Log("TitleSceneController破棄 - セッションは継続中");
    }

    /// <summary>
    /// UI表示を更新
    /// </summary>
    private void UpdateUI()
    {
        UpdateUserIDDisplay();

        if (instructionText != null)
        {
            switch (currentState)
            {
                case State.WaitingForQRCode:
                    instructionText.text = "QRコードをわたしてね";
                    break;
                case State.Calibrating:
                    instructionText.text = "黄色いせんにたって\nまっすぐまえをみてください";
                    break;
                case State.ReadyToStart:
                    instructionText.text = "準備完了！\nゲームを開始します...";
                    break;
            }
        }
    }

    /// <summary>
    /// UserID表示を更新
    /// </summary>
    private void UpdateUserIDDisplay()
    {
        if (userIDInputText != null)
        {
            userIDInputText.text = $"UserID: {currentInputUserID.ToUpper()}";
        }
    }

    /// <summary>
    /// 指示テキストを一定時間後にリセット
    /// </summary>
    private IEnumerator ResetInstructionText(float delay)
    {
        yield return new WaitForSeconds(delay);
        UpdateUI();
    }
    /// <summary>
    /// 指定した秒数かけてゲージを0→1まで満タンにする
    /// </summary>
    private IEnumerator FillCalibrationGauge(float duration)
    {
        float elapsed = 0f;

        if (calibrationGaugeImage != null) calibrationGaugeImage.fillAmount = 0f;
        if (GaugeGuideImage != null) GaugeGuideImage.fillAmount = 1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (calibrationGaugeImage != null)
                calibrationGaugeImage.fillAmount = t;

            yield return null;
        }

        // 念のため最後は必ず満タンにしておく
        if (calibrationGaugeImage != null)
            calibrationGaugeImage.fillAmount = 1f;
    }
}