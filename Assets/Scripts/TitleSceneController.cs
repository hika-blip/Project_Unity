using System.Collections;
using System.Text.RegularExpressions;
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

    [Header("シーン設定")]
    public string nextSceneName = "Forest";

    [Header("キャリブレーション")]
    public CalibrationManager calibrationManager;
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

        UpdateUI();
    }

    void Update()
    {
        if (currentState == State.WaitingForQRCode && !isProcessing)
        {
            HandleUserIDInput();
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

        CalibrationManager calibrationManager = FindObjectOfType<CalibrationManager>();
        if (calibrationManager == null)
        {
            Debug.LogError("CalibrationManagerが見つかりません");
            yield break;
        }

        if (isProcessing) yield break;
        isProcessing = true;

        //Debug.Log($"ユーザー登録成功: {userID}");

        // UserSessionManagerにユーザー情報を登録
        if (UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.StartNewSession(userID);
            UserSessionManager.Instance.RecordCustomEvent("QRCodeScanned", userID);
        }

        // 状態を更新
        currentState = State.Calibrating;
        UpdateUI();

        // ゲージ開始（calibrationDelay + 1〜2秒かけて満タンになるように）
        if (gaugeCoroutine != null) StopCoroutine(gaugeCoroutine);
        float gaugeDuration = calibrationManager.calibrationDelay + calibrationGaugeExtraTime;
        gaugeCoroutine = StartCoroutine(FillCalibrationGauge(gaugeDuration));

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

                // 少し待ってからシーン遷移
                yield return new WaitForSeconds(1.0f);

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
                    instructionText.text = "QRコードをスキャンしてください";
                    break;
                case State.Calibrating:
                    instructionText.text = "まっすぐまえをみてください";
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