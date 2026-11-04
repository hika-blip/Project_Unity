using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 観察後のクイズ（クイズ用シーンに置く）
/// ・StreamingAssets の CSV から、観察した昆虫（SelectionData.SelectedZone）の問題を1つランダムに選ぶ
///   CSVの列: 問題番号, ゾーン番号, 問題, 正解, 不正解1, 不正解2, 不正解3（1行目は見出し）
/// ・4つの選択肢をランダムな順番で、画面の左から右へ並べる
/// ・立っている位置（鼻のx。CalibrationManagerから取得）で4つの区画のどれかを選ぶ
///   区画の境目のまわりは「反応しない区域」で、そこにいる間は直前の選択を保つ
/// ・しゃがみ（UDP 9993）で決定 → 正誤を表示 → 選択画面へ戻る
/// ・結果は UserSessionManager.RecordQuizResult() で記録（問題番号・正誤・回答時間など）
///
/// ※UDPの受信には InsectSelectMenu.cs 内の UdpFloatReceiver クラスを使う
/// </summary>
public class QuizManager : MonoBehaviour
{
    public class QuizQuestion
    {
        public int id;          // 問題番号
        public int zone;        // ゾーン番号（どの昆虫の問題か）
        public string question;
        public string[] choices = new string[4]; // [0]が正解、[1]〜[3]が不正解
    }

    private enum State { Answering, Result, Leaving }

    [Header("問題ファイル（Assets/StreamingAssets に置く。UTF-8で保存）")]
    public string csvFileName = "quiz_questions.csv";

    [Header("遷移先")]
    [Tooltip("クイズが終わったら戻る選択画面のシーン名")]
    public string selectSceneName = "Observation_selecter";

    [Header("UI")]
    public TextMeshProUGUI questionText;
    [Tooltip("選択肢のテキスト（画面の左から順に4つ）")]
    public TextMeshProUGUI[] choiceTexts = new TextMeshProUGUI[4];
    [Tooltip("選択肢の背景（左から順に4つ。色と大きさで選択中を表す）")]
    public Image[] choiceBackgrounds = new Image[4];
    [Tooltip("正誤を表示するテキスト（最初は非表示）")]
    public TextMeshProUGUI resultText;
    [Tooltip("残り時間を表示するテキスト（任意。制限時間なしなら非表示）")]
    public TextMeshProUGUI timerText;

    [Header("見た目")]
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color selectedColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color correctColor = new Color(0.3f, 0.85f, 0.4f, 1f);
    public Color wrongColor = new Color(0.9f, 0.35f, 0.35f, 1f);
    public float selectedScale = 1.15f;
    public float animSpeed = 10f;

    [Header("立ち位置による選択（値は正規化座標。1.0 ≒ 約1m）")]
    [Tooltip("空欄なら自動で探す（DontDestroyOnLoadのCalibrationManager）")]
    public CalibrationManager calibrationManager;
    [Tooltip("ON: キャリブレーション位置からのずれを使う／OFF: 生の座標を使う")]
    public bool useCalibration = true;
    [Tooltip("右に動いたのに左の選択肢が選ばれる場合はオンにする")]
    public bool invertX = false;
    [Tooltip("内側2つの区画の幅。境目は -この値 / 0 / +この値 の3か所")]
    public float zoneWidth = 0.25f;
    [Tooltip("境目からこの距離以内は「反応しない区域」（直前の選択を保つ）")]
    public float boundaryDeadBand = 0.05f;

    [Header("決定（しゃがみ）")]
    public int squatPort = 9993;
    [Tooltip("しゃがみのデータがこの秒数届かなければ、立っているとみなす")]
    public float squatTimeout = 0.5f;
    [Tooltip("シーン開始直後、この秒数はしゃがみを受け付けない（問題を読む時間）")]
    public float startCooldown = 2.0f;

    [Header("結果表示")]
    public string correctMessage = "せいかい！";
    [Tooltip("{0} に正解の選択肢が入る")]
    public string wrongMessage = "ざんねん！\nこたえは「{0}」";
    public string timeUpMessage = "じかんぎれ！\nこたえは「{0}」";
    [Tooltip("正誤を表示してから選択画面へ戻るまでの秒数")]
    public float resultDuration = 3.0f;
    [Tooltip("制限時間（秒）。0以下なら制限なし")]
    public float timeLimit = 0f;

    [Header("効果音（任意）")]
    public AudioSource sfxSource;
    public AudioClip selectSound;
    public AudioClip correctSound;
    public AudioClip wrongSound;

    [Header("その他")]
    [Tooltip("1〜4キーで選択、Enter/Spaceで決定")]
    public bool enableKeyboard = true;
    public bool showDebugInfo = false;

    // 問題
    private QuizQuestion current;
    private string[] displayChoices = new string[4]; // 画面の左から順の選択肢
    private int correctSlot = -1;                    // 正解が画面のどこにあるか（0〜3）

    // 状態
    private State state = State.Answering;
    private int selectedSlot = -1;   // 今選んでいる区画（-1:まだ選んでいない）
    private float questionStartTime;
    private float inputEnabledTime;
    private bool prevSquat = true;   // 最初は「しゃがんでいる」扱い。一度立たないと決定できない
    private string debugState = "-";

    private UdpFloatReceiver squatRecv;

    void Start()
    {
        // クイズシーンを開いた時点で「このセッションはクイズ済み」にする
        SessionFlags.QuizDone = true;

        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        if (calibrationManager == null)
        {
            calibrationManager = FindObjectOfType<CalibrationManager>();
            if (calibrationManager == null)
                Debug.LogWarning("[QuizManager] CalibrationManager が見つかりません。立ち位置での選択は使えません（キーボードは使えます）");
        }

        if (resultText != null) resultText.gameObject.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(timeLimit > 0f);

        // 問題を読み込んで1つ選ぶ
        List<QuizQuestion> all = LoadQuestions();
        int zone = SelectionData.SelectedZone;
        List<QuizQuestion> candidates = all.FindAll(q => q.zone == zone);

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[QuizManager] ゾーン{zone}の問題がCSVにありません。クイズを飛ばして選択画面へ戻ります");
            if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
                UserSessionManager.Instance.RecordCustomEvent("QuizSkipped", $"Zone={zone},Reason=NoQuestion");
            state = State.Leaving;
            TransitionTo(selectSceneName);
            return;
        }

        current = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        ShuffleChoices();
        ShowQuestion();

        StartReceivers();
        questionStartTime = Time.time;
        inputEnabledTime = Time.time + startCooldown;

        if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
            UserSessionManager.Instance.RecordCustomEvent("QuizStart", $"QuestionID={current.id},Zone={current.zone}");

        Debug.Log($"[QuizManager] 出題: 問題{current.id}（ゾーン{current.zone}） 正解の位置={correctSlot + 1}番目");
    }

    void Update()
    {
        if (state == State.Answering)
        {
            HandlePosition();
            if (enableKeyboard) HandleKeyboard();
            HandleSquat();
            HandleTimeLimit();
        }
        UpdateVisuals();
    }

    // ------------------------------------------------------------
    // 問題の読み込み（CSV）
    // ------------------------------------------------------------
    private List<QuizQuestion> LoadQuestions()
    {
        var list = new List<QuizQuestion>();
        string path = Path.Combine(Application.streamingAssetsPath, csvFileName);

        if (!File.Exists(path))
        {
            Debug.LogError($"[QuizManager] 問題ファイルが見つかりません: {path}");
            return list;
        }

        string text = File.ReadAllText(path, Encoding.UTF8); // BOMの有無どちらでも読める
        foreach (List<string> row in ParseCsv(text))
        {
            // 見出し行・空行・列が足りない行は飛ばす
            if (row.Count < 7) continue;
            if (!int.TryParse(row[0].Trim(), out int id)) continue;
            if (!int.TryParse(row[1].Trim(), out int zone)) continue;

            var q = new QuizQuestion { id = id, zone = zone, question = row[2].Trim() };
            for (int i = 0; i < 4; i++) q.choices[i] = row[3 + i].Trim();
            list.Add(q);
        }

        Debug.Log($"[QuizManager] 問題を{list.Count}問読み込みました（{path}）");
        return list;
    }

    /// <summary>"..." で囲まれた値（カンマ・改行・"" を含む）にも対応したCSVの分割</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { /* 無視 */ }
                else if (c == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else if (c == '﻿') { /* BOM */ }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // ------------------------------------------------------------
    // 出題
    // ------------------------------------------------------------
    private void ShuffleChoices()
    {
        int[] order = { 0, 1, 2, 3 };
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int slot = 0; slot < 4; slot++)
        {
            displayChoices[slot] = current.choices[order[slot]];
            if (order[slot] == 0) correctSlot = slot;
        }
    }

    private void ShowQuestion()
    {
        if (questionText != null) questionText.text = current.question;
        for (int i = 0; i < 4; i++)
        {
            if (i < choiceTexts.Length && choiceTexts[i] != null) choiceTexts[i].text = displayChoices[i];
            if (i < choiceBackgrounds.Length && choiceBackgrounds[i] != null) choiceBackgrounds[i].color = normalColor;
        }
    }

    // ------------------------------------------------------------
    // 立ち位置による選択（4区画＋境目の反応しない区域）
    // ------------------------------------------------------------
    private void HandlePosition()
    {
        if (calibrationManager == null)
        {
            debugState = "CalibrationManagerなし";
            return;
        }

        Vector3 pos = useCalibration
            ? calibrationManager.GetCalibratedPosition()
            : calibrationManager.GetRawPosition();

        float dx = invertX ? -pos.x : pos.x;

        int slot = SlotFromX(dx, selectedSlot);
        if (slot != selectedSlot)
        {
            selectedSlot = slot;
            PlaySfx(selectSound);
        }
        debugState = $"x:{dx:F3}  区画:{(selectedSlot >= 0 ? (selectedSlot + 1).ToString() : "-")}";
    }

    /// <summary>xから区画（0〜3）を求める。境目の近くでは現在の区画を保つ</summary>
    private int SlotFromX(float x, int currentSlot)
    {
        float[] borders = { -zoneWidth, 0f, zoneWidth };
        foreach (float b in borders)
        {
            if (Mathf.Abs(x - b) < boundaryDeadBand) return currentSlot;
        }

        if (x < borders[0]) return 0;
        if (x < borders[1]) return 1;
        if (x < borders[2]) return 2;
        return 3;
    }

    private void HandleKeyboard()
    {
        for (int i = 0; i < 4; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                if (selectedSlot != i) PlaySfx(selectSound);
                selectedSlot = i;
            }
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            Answer(selectedSlot);
        }
    }

    private void HandleSquat()
    {
        Vector3 s = Vector3.zero;
        bool squat = squatRecv != null && squatRecv.TryGet(out s, squatTimeout) && s.x > 0.5f;

        // 立っている→しゃがんだ、に変わった瞬間だけ決定
        if (squat && !prevSquat && Time.time >= inputEnabledTime)
        {
            Answer(selectedSlot);
        }
        prevSquat = squat;
    }

    private void HandleTimeLimit()
    {
        if (timeLimit <= 0f) return;

        float remain = timeLimit - (Time.time - questionStartTime);
        if (timerText != null) timerText.text = Mathf.CeilToInt(Mathf.Max(remain, 0f)).ToString();

        if (remain <= 0f) TimeUp();
    }

    // ------------------------------------------------------------
    // 回答・結果
    // ------------------------------------------------------------
    private void Answer(int slot)
    {
        if (state != State.Answering) return;
        if (slot < 0 || slot > 3) return;           // まだどの区画も選んでいない
        if (Time.time < inputEnabledTime) return;

        bool correct = slot == correctSlot;
        float responseTime = Time.time - questionStartTime;

        Record(true, correct, displayChoices[slot], slot, responseTime);
        ShowResult(correct ? correctMessage : string.Format(wrongMessage, displayChoices[correctSlot]), correct, slot);
    }

    private void TimeUp()
    {
        if (state != State.Answering) return;
        float responseTime = Time.time - questionStartTime;

        Record(false, false, "", -1, responseTime);
        ShowResult(string.Format(timeUpMessage, displayChoices[correctSlot]), false, -1);
    }

    private void ShowResult(string message, bool correct, int chosenSlot)
    {
        state = State.Result;

        // 正解は緑、選んだ不正解は赤
        for (int i = 0; i < 4; i++)
        {
            if (i >= choiceBackgrounds.Length || choiceBackgrounds[i] == null) continue;
            if (i == correctSlot) choiceBackgrounds[i].color = correctColor;
            else if (i == chosenSlot) choiceBackgrounds[i].color = wrongColor;
            else choiceBackgrounds[i].color = normalColor;
        }

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = message;
        }

        PlaySfx(correct ? correctSound : wrongSound);
        StartCoroutine(ReturnAfterDelay());
    }

    private IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(resultDuration);
        state = State.Leaving;
        TransitionTo(selectSceneName);
    }

    private void Record(bool answered, bool correct, string chosenText, int chosenSlot, float responseTime)
    {
        Debug.Log($"[QuizManager] 回答: 問題{current.id} 回答={(answered ? chosenText : "なし")} " +
                  $"{(answered ? (correct ? "正解" : "不正解") : "時間切れ")} {responseTime:F1}秒");

        if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
        {
            UserSessionManager.Instance.RecordQuizResult(
                current.id, current.zone, answered, correct, chosenText, chosenSlot + 1, correctSlot + 1, responseTime);
        }
    }

    // ------------------------------------------------------------
    // 見た目
    // ------------------------------------------------------------
    private void UpdateVisuals()
    {
        float k = 1f - Mathf.Exp(-animSpeed * Time.deltaTime);

        for (int i = 0; i < 4; i++)
        {
            if (i >= choiceBackgrounds.Length || choiceBackgrounds[i] == null) continue;

            bool selected = i == selectedSlot;
            float target = selected ? selectedScale : 1f;
            RectTransform rt = choiceBackgrounds[i].rectTransform;
            float s = Mathf.Lerp(rt.localScale.x, target, k);
            rt.localScale = Vector3.one * s;

            // 回答中だけ、選択中の区画を色で示す（結果表示中は正誤の色のまま）
            if (state == State.Answering)
                choiceBackgrounds[i].color = selected ? selectedColor : normalColor;
        }
    }

    private void PlaySfx(AudioClip clip)
    {
        if (sfxSource != null && clip != null) sfxSource.PlayOneShot(clip);
    }

    // ------------------------------------------------------------
    // シーン遷移
    // ------------------------------------------------------------
    private void TransitionTo(string sceneName)
    {
        // 次のシーンで同じポートを使えるよう、先にUDPを閉じる
        StopReceivers();

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string name = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            if (name.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[QuizManager] Loading scene '{sceneName}'");
                SceneManager.LoadScene(i);
                return;
            }
        }
        Debug.LogError($"[QuizManager] Scene '{sceneName}' not found in build settings!");
    }

    // ------------------------------------------------------------
    // UDP
    // ------------------------------------------------------------
    private void StartReceivers()
    {
        StopReceivers();
        squatRecv = new UdpFloatReceiver(squatPort);
        squatRecv.Start();
    }

    private void StopReceivers()
    {
        squatRecv?.Stop();
        squatRecv = null;
    }

    void OnDisable() { StopReceivers(); }
    void OnDestroy() { StopReceivers(); }
    void OnApplicationQuit() { StopReceivers(); }

    void OnGUI()
    {
        if (!showDebugInfo) return;
        GUILayout.BeginArea(new Rect(10, 10, 520, 140));
        GUILayout.Label("=== QuizManager ===");
        GUILayout.Label($"問題: {(current != null ? $"{current.id}（ゾーン{current.zone}）" : "-")}  正解の位置: {correctSlot + 1}");
        GUILayout.Label($"立ち位置: {debugState}");
        GUILayout.Label($"境目: ±{zoneWidth:F2} / 0  反応しない幅: ±{boundaryDeadBand:F2}");
        GUILayout.Label($"状態: {state}");
        GUILayout.EndArea();
    }
}
