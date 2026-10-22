using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;


/// <summary>
/// 観察する昆虫の選択メニュー
/// ・左右の手首のうち画面（カメラ）に近い方を「有効な手」とする
/// ・有効な手と同じ側の肩を基準に、手首が肩より右なら右の選択肢へ、左なら左の選択肢へ進む
///   （出し続けると一定間隔で1つずつ進む。どれだけ横に出したかは肩幅で割って判定）
/// ・手を下ろしているとき（手首が肩より一定以上下）は操作していない扱い
/// ・選択中のカードは拡大、それ以外はグレーのレイヤーをかける
/// ・しゃがみ（UDP 9993）で決定
///
/// 受信するUDP（dual_camera_gesture_udp.py）
///   9995 左手首 "x,y,z" / 9994 右手首 "x,y,z"
///   9992 左肩   "x,y,z" / 9991 右肩   "x,y,z"
///   9993 しゃがみ "0"(立っている) / "1"(しゃがんでいる) を毎フレーム
/// </summary>
public class InsectSelectMenu : MonoBehaviour
{
    [Serializable]
    public class MenuItem
    {
        public string label = "カブトムシ";
        public Sprite icon;
        public Color cardColor = new Color(0.35f, 0.4f, 0.9f);
        [Tooltip("昆虫のゾーン番号（1〜7）。0にすると「次へ」（Endingへ移動）として扱う")]
        public int zoneNumber = 0;
    }

    // SmallerZ: zが小さい方（カメラに近い方）を有効な手にする
    // CloserToZero: |z|が0に近い方を有効な手にする
    public enum HandPickMode
    {
        SmallerZ,
        CloserToZero
    }

    [Header("選択肢（左から順に。昆虫7種＋次へ）")]
    public List<MenuItem> items = new List<MenuItem>();

    [Header("遷移先シーン")]
    [Tooltip("昆虫を選んだときの遷移先")]
    public string observationSceneName = "Observation";
    [Tooltip("「次へ」を選んだときの遷移先")]
    public string endingSceneName = "Ending";
    [Tooltip("このキーで直接Endingへ移動（デバッグ用）")]
    public KeyCode endingKey = KeyCode.E;

    [Header("UI")]
    [Tooltip("カードを並べる親。アンカー・ピボットは中央にしておく")]
    public RectTransform content;
    [Tooltip("子に Icon(Image) / Label(TextMeshProUGUI) / Dim(Image) を持つカードのPrefab")]
    public GameObject cardPrefab;
    public float cardSpacing = 320f;     // カード中心どうしの間隔(px)
    public float selectedScale = 1.2f;   // 選択中カードの大きさ
    public float normalScale = 0.9f;     // 選択されていないカードの大きさ
    [Range(0f, 1f)] public float dimAlpha = 0.6f; // 選択外カードのグレーの濃さ
    public float animSpeed = 10f;        // 見た目の追従の速さ

    [Header("カード列の位置")]
    [Tooltip("カード列全体の上下位置(px)。マイナスにすると下がる（上にタイトルを置く場合など）。再生中に変えてもすぐ反映される")]
    public float cardsOffsetY = 0f;
    [Tooltip("カード列全体の左右位置(px)。プラスで右へ。選択中のカードはこの位置に来る")]
    public float cardsOffsetX = 0f;

    [Header("左右の矢印（任意）")]
    [Tooltip("左に移動中に光らせる矢印")]
    public Image leftArrow;
    [Tooltip("右に移動中に光らせる矢印")]
    public Image rightArrow;
    public Color arrowIdleColor = new Color(1f, 1f, 1f, 0.3f);
    public Color arrowActiveColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("次の選択肢に進むまでの溜まり具合を表示するゲージ（Image Type=Filled。任意）")]
    public Image stepGaugeImage;

    [Header("UDPポート")]
    public int leftWristPort = 9995;
    public int rightWristPort = 9994;
    public int leftShoulderPort = 9992;
    public int rightShoulderPort = 9991;
    public int squatPort = 9993;
    [Tooltip("手首・肩のデータがこの秒数届かなければ、検出できていないとみなす")]
    public float dataTimeout = 0.3f;
    [Tooltip("しゃがみのデータがこの秒数届かなければ、立っているとみなす")]
    public float squatTimeout = 0.5f;

    [Header("有効な手の選び方")]
    [Tooltip("SmallerZ: カメラに近い方（zが小さい方）／CloserToZero: |z|が0に近い方")]
    public HandPickMode handPickMode = HandPickMode.SmallerZ;
    [Tooltip("もう片方の手の方がこれ以上カメラに近いときだけ、使う手を切り替える")]
    public float handSwitchMargin = 0.05f;

    [Header("手の位置による選択（値はすべて「肩幅の何倍か」）")]
    [Tooltip("手首が肩から肩幅のこの倍数以上横に出たら、その方向に進む")]
    public float moveThreshold = 0.8f;
    [Tooltip("進んでいる状態から中央に戻ったとみなす位置（moveThresholdより小さくする）")]
    public float returnThreshold = 0.5f;
    [Tooltip("手首が肩より肩幅のこの倍数以上下にあるときは「手を下ろしている＝操作していない」扱い")]
    public float handDownLimit = 0.8f;
    [Tooltip("両肩が見えていないときに使う肩幅（正規化座標）")]
    public float defaultShoulderWidth = 0.35f;
    [Tooltip("手の位置の平滑化（小さいほど滑らか・反応は遅い）")]
    [Range(0.05f, 1f)] public float handSmoothing = 0.3f;
    [Tooltip("左右に出してから最初の1つ進むまでの秒数")]
    public float firstStepDelay = 0.5f;
    [Tooltip("左右に出し続けたとき、次の選択肢へ進む間隔(秒)")]
    public float repeatInterval = 1.0f;
    [Tooltip("手を右に出したのに選択が左に進む場合は切り替える")]
    public bool invertX = true;
    [Tooltip("シーン開始時、一度手を下ろす（中央に戻す）まで左右移動を受け付けない")]
    public bool requireCenterAtStart = true;

    [Header("決定（しゃがみ）")]
    [Tooltip("シーン開始直後、この秒数はしゃがみを受け付けない")]
    public float startCooldown = 1.5f;
    [Tooltip("決定してからシーンを移動するまでの秒数（決定演出用）")]
    public float decideDelay = 0.4f;

    [Header("効果音")]
    [Tooltip("効果音を鳴らすAudioSource（空欄なら自動で追加）")]
    public AudioSource sfxSource;
    [Tooltip("左右で選択肢が切り替わったときの音（左右共通）")]
    public AudioClip selectSound;
    [Range(0f, 1f)] public float selectVolume = 0.8f;
    [Tooltip("しゃがみで決定したときの音")]
    public AudioClip decideSound;
    [Range(0f, 1f)] public float decideVolume = 1.0f;
    [Tooltip("決定音が鳴り終わるまでシーン移動を待つ（オフだとDecide Delay後に移動し、音が途中で切れることがある）")]
    public bool waitForDecideSound = true;

    [Header("その他")]
    [Tooltip("前回観察した昆虫のカードから始める")]
    public bool startAtLastSelected = true;
    [Tooltip("←→キーで移動、Enter/Spaceで決定")]
    public bool enableKeyboard = true;
    public bool showDebugInfo = false;

    // 選択中のインデックス（外から参照用）
    public int SelectedIndex { get; private set; }

    // UDP受信
    private UdpFloatReceiver lWristRecv, rWristRecv, lShoulderRecv, rShoulderRecv, squatRecv;

    // 表示用
    private float displayPos;

    // 手の状態
    private int activeHand = -1;      // -1:なし 0:左手 1:右手
    private float shoulderWidth;
    private bool hasSmoothedRatio;
    private float smoothedRatio;      // （手首x - 肩x）/ 肩幅 を平滑化したもの
    private string handState = "-";   // デバッグ表示用

    // 移動の状態
    private int moveDir = 0;          // -1:左 0:中央 1:右
    private float nextStepTime;
    private float currentStepWait;
    private bool centerConfirmed;

    // 決定
    private bool prevSquat = true;    // 最初は「しゃがんでいる」扱い。一度立たないと決定できない
    private float inputEnabledTime;
    private bool decided;

    // 生成したカード
    private class CardView
    {
        public RectTransform rect;
        public Image dim;
    }
    private readonly List<CardView> cards = new List<CardView>();

    void Start()
    {
        shoulderWidth = defaultShoulderWidth;

        // 効果音用AudioSourceの準備
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f; // 2D音声

        BuildCards();
        StartReceivers();

        // 前回観察した昆虫のカードから始める
        SelectedIndex = 0;
        if (startAtLastSelected)
        {
            int idx = items.FindIndex(it => it.zoneNumber == SelectionData.SelectedZone && it.zoneNumber >= 1);
            if (idx >= 0) SelectedIndex = idx;
        }
        displayPos = SelectedIndex;

        centerConfirmed = !requireCenterAtStart;
        inputEnabledTime = Time.time + startCooldown;
        UpdateVisuals(true);
    }

    void Update()
    {
        if (items.Count == 0) return;

        if (!decided)
        {
            HandleHandPosition();
            if (enableKeyboard) HandleKeyboard();
            HandleSquatDecide();
        }

        UpdateVisuals(false);
    }

    // ------------------------------------------------------------
    // カード生成
    // ------------------------------------------------------------
    private void BuildCards()
    {
        if (content == null || cardPrefab == null)
        {
            Debug.LogError("[InsectSelectMenu] content または cardPrefab が設定されていません");
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            GameObject go = Instantiate(cardPrefab, content);
            go.name = $"Card_{i}_{items[i].label}";

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(i * cardSpacing, 0f);

            Image bg = go.GetComponent<Image>();
            if (bg != null) bg.color = items[i].cardColor;

            Transform iconT = go.transform.Find("Icon");
            if (iconT != null)
            {
                Image icon = iconT.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = items[i].icon;
                    icon.enabled = items[i].icon != null;
                    icon.preserveAspect = true;
                }
            }

            Transform labelT = go.transform.Find("Label");
            if (labelT != null)
            {
                TextMeshProUGUI label = labelT.GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = items[i].label;
            }

            Image dim = null;
            Transform dimT = go.transform.Find("Dim");
            if (dimT != null)
            {
                dim = dimT.GetComponent<Image>();
                dimT.SetAsLastSibling();
            }

            cards.Add(new CardView { rect = rt, dim = dim });
        }
    }

    // ------------------------------------------------------------
    // 手の位置（有効な手の手首と、同じ側の肩の位置関係）による選択
    // ------------------------------------------------------------
    private void HandleHandPosition()
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

        if (candidate != activeHand)
        {
            activeHand = candidate;
            hasSmoothedRatio = false; // 手が変わったら平滑化をやり直す
        }

        // 手の横方向のずれ（肩幅で割る）を計算。手を下ろしている・手がない場合は中央扱い
        bool operating = false;
        if (activeHand != -1)
        {
            Vector3 wrist = activeHand == 0 ? lw : rw;
            Vector3 shoulder = activeHand == 0 ? ls : rs;
            float width = Mathf.Max(shoulderWidth, 0.05f);

            // y は上が正（Python側で変換済み）。手首が肩より一定以上下なら「手を下ろしている」
            float downRatio = (shoulder.y - wrist.y) / width;
            if (downRatio <= handDownLimit)
            {
                float dx = wrist.x - shoulder.x;
                if (invertX) dx = -dx;
                float ratio = dx / width;

                smoothedRatio = hasSmoothedRatio ? Mathf.Lerp(smoothedRatio, ratio, handSmoothing) : ratio;
                hasSmoothedRatio = true;
                operating = true;
                handState = $"{(activeHand == 0 ? "左手" : "右手")}  横:{smoothedRatio:F2}  下:{downRatio:F2}";
            }
            else
            {
                handState = $"{(activeHand == 0 ? "左手" : "右手")}（下ろしている）  下:{downRatio:F2}";
            }
        }
        else
        {
            handState = "手が見えていない";
        }

        if (!operating)
        {
            hasSmoothedRatio = false;
            smoothedRatio = 0f;
        }

        float x = operating ? smoothedRatio : 0f;

        // 今どちら側に出しているか（入る位置と戻る位置を分けてパタパタ防止）
        int dir = moveDir;
        if (moveDir == 0)
        {
            if (x > moveThreshold) dir = 1;
            else if (x < -moveThreshold) dir = -1;
        }
        else
        {
            if (Mathf.Abs(x) < returnThreshold) dir = 0;
            else if (moveDir == 1 && x < -moveThreshold) dir = -1;
            else if (moveDir == -1 && x > moveThreshold) dir = 1;
        }

        // 開始直後は、一度中央（手を下ろす・手が見えない含む）に戻ってから受け付ける
        if (!centerConfirmed)
        {
            if (Mathf.Abs(x) < returnThreshold) centerConfirmed = true;
            moveDir = 0;
            return;
        }

        if (dir != moveDir)
        {
            moveDir = dir;
            if (moveDir != 0)
            {
                currentStepWait = firstStepDelay;
                nextStepTime = Time.time + firstStepDelay;
            }
        }

        // 左右に出し続けている間、一定間隔で1つずつ進む
        if (moveDir != 0 && Time.time >= nextStepTime)
        {
            Step(moveDir);
            currentStepWait = repeatInterval;
            nextStepTime = Time.time + repeatInterval;
        }
    }

    private bool TryRead(UdpFloatReceiver recv, out Vector3 v)
    {
        v = Vector3.zero;
        return recv != null && recv.TryGet(out v, dataTimeout);
    }

    // 小さいほど「カメラに近い」
    private float HandScore(float z)
    {
        return handPickMode == HandPickMode.SmallerZ ? z : Mathf.Abs(z);
    }

    private void Step(int dir)
    {
        int next = Mathf.Clamp(SelectedIndex + dir, 0, items.Count - 1);
        if (next != SelectedIndex)
        {
            SelectedIndex = next;
            PlaySfx(selectSound, selectVolume); // 選択肢が実際に切り替わったときだけ鳴らす（端では鳴らない）
            Debug.Log($"[InsectSelectMenu] 選択: {items[SelectedIndex].label}");
        }
    }

    private void PlaySfx(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null) return;
        // PlayOneShotなら、前の音が鳴り終わる前に次の音を鳴らしても途切れない
        sfxSource.PlayOneShot(clip, volume);
    }

    private void HandleKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Step(1);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1);

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            Decide();
        }
        if (Input.GetKeyDown(endingKey))
        {
            Debug.Log($"[InsectSelectMenu] Ending key pressed ({endingKey})");
            decided = true;
            TransitionTo(endingSceneName);
        }
    }

    // ------------------------------------------------------------
    // しゃがみによる決定
    // ------------------------------------------------------------
    private void HandleSquatDecide()
    {
        // "1"（0.5より大きい値）が届いていればしゃがみ中とみなす
        Vector3 s = Vector3.zero;
        bool squat = squatRecv != null && squatRecv.TryGet(out s, squatTimeout) && s.x > 0.5f;

        // 立っている→しゃがんだ、に変わった瞬間だけ決定
        if (squat && !prevSquat && Time.time >= inputEnabledTime)
        {
            Decide();
        }
        prevSquat = squat;
    }

    private void Decide()
    {
        if (decided || Time.time < inputEnabledTime) return;

        decided = true;
        PlaySfx(decideSound, decideVolume);
        StartCoroutine(DecideRoutine(items[SelectedIndex]));
    }

    private IEnumerator DecideRoutine(MenuItem item)
    {
        // 決定した感を出すため、選択中カードを一瞬大きくする
        RectTransform rt = cards.Count > SelectedIndex ? cards[SelectedIndex].rect : null;
        float t = 0f;
        while (t < decideDelay)
        {
            t += Time.deltaTime;
            if (rt != null)
            {
                float pulse = selectedScale * (1f + 0.15f * Mathf.Sin(Mathf.Clamp01(t / decideDelay) * Mathf.PI));
                rt.localScale = Vector3.one * pulse;
            }
            yield return null;
        }

        // 決定音が鳴り終わるまで待つ（シーンを移動するとAudioSourceごと消えて音が切れるため）
        if (waitForDecideSound && decideSound != null && decideSound.length > t)
        {
            yield return new WaitForSeconds(decideSound.length - t);
        }

        if (item.zoneNumber >= 1)
        {
            // ZoneSelectorと同じく、選んだ昆虫をSelectionDataに渡してObservationへ
            Debug.Log($"[InsectSelectMenu] Zone {item.zoneNumber}（{item.label}）selected");
            SelectionData.SelectedZone = item.zoneNumber;

            if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
            {
                UserSessionManager.Instance.RecordZoneSelection(item.zoneNumber);
            }

            TransitionTo(observationSceneName);
        }
        else
        {
            Debug.Log("[InsectSelectMenu] 「次へ」selected");
            TransitionTo(endingSceneName);
        }
    }

    // ------------------------------------------------------------
    // シーン遷移（ZoneSelectorと同じ流れ）
    // ------------------------------------------------------------
    private void TransitionTo(string sceneName)
    {
        Debug.Log($"[InsectSelectMenu] Transitioning to scene '{sceneName}'");

        // 次のシーンで同じポートを使えるよう、先にUDPを閉じる
        StopReceivers();

        int sceneIndex = FindSceneIndex(sceneName);
        if (sceneIndex >= 0)
        {
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError($"[InsectSelectMenu] Scene '{sceneName}' not found in build settings!");
            LogAvailableScenes();

            // 遷移できなかったので選択画面に戻す
            decided = false;
            StartReceivers();
        }
    }

    private int FindSceneIndex(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string nameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (nameInBuild.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private void LogAvailableScenes()
    {
        Debug.Log("=== Available scenes in Build Settings ===");
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            Debug.Log($"  [{i}] {System.IO.Path.GetFileNameWithoutExtension(scenePath)}");
        }
    }

    // ------------------------------------------------------------
    // 見た目の更新
    // ------------------------------------------------------------
    private void UpdateVisuals(bool immediate)
    {
        if (content == null) return;

        float k = immediate ? 1f : 1f - Mathf.Exp(-animSpeed * Time.deltaTime);

        // 選択中のカードが基準位置（cardsOffsetX, cardsOffsetY）に来るように全体をずらす
        displayPos = Mathf.Lerp(displayPos, SelectedIndex, k);
        content.anchoredPosition = new Vector2(cardsOffsetX - displayPos * cardSpacing, cardsOffsetY);

        for (int i = 0; i < cards.Count; i++)
        {
            bool selected = i == SelectedIndex;
            CardView c = cards[i];

            if (!(decided && selected)) // 決定演出中のカードはDecideRoutine側で大きさを動かす
            {
                float targetScale = selected ? selectedScale : normalScale;
                float s = immediate ? targetScale : Mathf.Lerp(c.rect.localScale.x, targetScale, k);
                c.rect.localScale = Vector3.one * s;
            }

            if (c.dim != null)
            {
                Color col = c.dim.color;
                float targetA = selected ? 0f : dimAlpha;
                col.a = immediate ? targetA : Mathf.Lerp(col.a, targetA, k);
                c.dim.color = col;
            }

            if (selected) c.rect.SetAsLastSibling();
        }

        // 左右の矢印：進んでいる方向を光らせる（端でそれ以上進めない方向は光らせない）
        bool canLeft = SelectedIndex > 0;
        bool canRight = SelectedIndex < items.Count - 1;
        if (leftArrow != null)
            leftArrow.color = (moveDir == -1 && canLeft && !decided) ? arrowActiveColor : arrowIdleColor;
        if (rightArrow != null)
            rightArrow.color = (moveDir == 1 && canRight && !decided) ? arrowActiveColor : arrowIdleColor;

        // 次の1歩までの溜まり具合
        if (stepGaugeImage != null)
        {
            bool canMove = (moveDir == 1 && canRight) || (moveDir == -1 && canLeft);
            if (canMove && !decided && currentStepWait > 0f)
                stepGaugeImage.fillAmount = 1f - Mathf.Clamp01((nextStepTime - Time.time) / currentStepWait);
            else
                stepGaugeImage.fillAmount = 0f;
        }
    }

    // ------------------------------------------------------------
    // UDP
    // ------------------------------------------------------------
    private void StartReceivers()
    {
        StopReceivers();
        lWristRecv = new UdpFloatReceiver(leftWristPort);
        rWristRecv = new UdpFloatReceiver(rightWristPort);
        lShoulderRecv = new UdpFloatReceiver(leftShoulderPort);
        rShoulderRecv = new UdpFloatReceiver(rightShoulderPort);
        squatRecv = new UdpFloatReceiver(squatPort);
        lWristRecv.Start();
        rWristRecv.Start();
        lShoulderRecv.Start();
        rShoulderRecv.Start();
        squatRecv.Start();
    }

    private void StopReceivers()
    {
        lWristRecv?.Stop();
        rWristRecv?.Stop();
        lShoulderRecv?.Stop();
        rShoulderRecv?.Stop();
        squatRecv?.Stop();
        lWristRecv = rWristRecv = lShoulderRecv = rShoulderRecv = squatRecv = null;
    }

    void OnDisable() { StopReceivers(); }
    void OnDestroy() { StopReceivers(); }
    void OnApplicationQuit() { StopReceivers(); }

    void OnGUI()
    {
        if (!showDebugInfo) return;
        GUILayout.BeginArea(new Rect(10, 10, 480, 180));
        GUILayout.Label("=== InsectSelectMenu ===");
        GUILayout.Label($"有効な手: {handState}");
        GUILayout.Label($"肩幅: {shoulderWidth:F3}  (進む: ±{moveThreshold:F2} / 戻る: ±{returnThreshold:F2} / 下ろし判定: {handDownLimit:F2})");
        GUILayout.Label($"方向: {(moveDir == 1 ? "右" : moveDir == -1 ? "左" : "中央")}  中央確認: {centerConfirmed}");
        GUILayout.Label($"選択中: [{SelectedIndex}] {(items.Count > 0 ? items[SelectedIndex].label : "-")}");
        GUILayout.EndArea();
    }
}

/// <summary>
/// "x,y,z" や "1" のようなカンマ区切りの数値をUDPで受け取るクラス（最大3つ）
/// </summary>
public class UdpFloatReceiver
{
    private readonly int port;
    private UdpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly object lockObj = new object();
    private Vector3 value;
    private int count;
    private DateTime lastReceived = DateTime.MinValue;

    public UdpFloatReceiver(int port) { this.port = port; }

    public void Start()
    {
        try
        {
            // ZoneSelectorと同じく、シーンを行き来してもポートを再利用できるようにする
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            running = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true };
            thread.Start();
            Debug.Log($"UDP受信開始: ポート{port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"UDP初期化エラー(ポート{port}): {e.Message}");
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                if (client.Available > 0)
                {
                    byte[] data = client.Receive(ref ep);
                    Parse(Encoding.UTF8.GetString(data));
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (!running) break; }
            catch (NullReferenceException) { break; }
        }
    }

    private void Parse(string message)
    {
        string[] parts = message.Trim().Split(',');
        float[] v = new float[3];
        int n = 0;
        foreach (string p in parts)
        {
            if (n >= 3) break;
            if (!float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[n])) return;
            n++;
        }
        if (n == 0) return;

        lock (lockObj)
        {
            value = new Vector3(v[0], v[1], v[2]);
            count = n;
            lastReceived = DateTime.UtcNow;
        }
    }

    /// <summary>最新の値を取得。timeout秒以内に受信していればtrue</summary>
    public bool TryGet(out Vector3 v, float timeout)
    {
        lock (lockObj)
        {
            v = value;
            if (count == 0) return false;
            return (DateTime.UtcNow - lastReceived).TotalSeconds <= timeout;
        }
    }

    public void Stop()
    {
        running = false;
        if (thread != null && thread.IsAlive) thread.Join(200);
        try { client?.Close(); } catch { }
        client = null;
        thread = null;
    }
}