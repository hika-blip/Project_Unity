using UnityEngine;
using System.Linq;

public class NearestInsectHighlighter : MonoBehaviour
{
    [Header("虫オブジェクトのタグ")]
    public string insectTag = "Insect";

    [Header("ハイライト設定")]
    public GameObject highlightPrefab; // 円形エフェクト (QuadやLineRenderer付きPrefab)
    private GameObject currentHighlight;

    private Transform nearestInsect;

    void Start()
    {
        // ハイライト用オブジェクトを生成して非表示にしておく
        if (highlightPrefab != null)
        {
            currentHighlight = Instantiate(highlightPrefab);
            currentHighlight.SetActive(false);
        }
    }

    void Update()
    {
        // 全ての虫を検索
        GameObject[] insects = GameObject.FindGameObjectsWithTag(insectTag);

        if (insects.Length == 0) return;

        // 一番近い虫を探す
        Transform cam = Camera.main.transform;
        nearestInsect = insects
            .OrderBy(go => Vector3.Distance(cam.position, go.transform.position))
            .First()
            .transform;

        // ハイライトを更新
        if (nearestInsect != null && currentHighlight != null)
        {
            currentHighlight.SetActive(true);

            // 虫の位置に追従（少し下にオフセットして足元に置く）
            Vector3 pos = nearestInsect.position;
            pos.y -= 0.1f;
            currentHighlight.transform.position = pos;

            // スケール調整（虫の大きさに合わせたい場合は localScale を調整）
            currentHighlight.transform.localScale = Vector3.one * 1.5f;

            // カメラに対して平面を常に水平にする
            currentHighlight.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
    }
}
