using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace BuichaLive
{
    /// <summary>パネルの表示の言語。</summary>
    public enum BuichaLiveLanguage
    {
        Japanese = 0,
        English = 1,
    }

    /// <summary>
    /// プレハブのルートに付ける設定。ワールドを作る人が触るのはここだけでよいようにする。
    /// パネル (BuichaLivePanel) が Start で読む。値は同期しない (全員が同じプレハブの値を持っている)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class BuichaLiveSettings : UdonSharpBehaviour
    {
        [Header("表示の言語")]
        [Tooltip("パネルの文言を日本語 (Japanese) と英語 (English) で切り替えます")]
        public BuichaLiveLanguage language = BuichaLiveLanguage.Japanese;

        [Header("チャンネルの切り替え")]
        [Tooltip("オン: 誰かがチャンネルを切り替える・止めると、全員の画面が変わります。オフ: 切り替えと停止はその人の画面だけです (チャンネルの中身と、消す操作は全員で共有)。ステージモードはいつもオンと同じです")]
        public bool syncChannelSwitch = false;

        [Header("最初に自動で再生する動画")]
        [Tooltip("インスタンスを最初に開いた人が、チャンネル 1 (ステージモードはステージ) にこの URL を入れて再生します。後から来た人にも同じものが映ります。空なら何もしません")]
        public VRCUrl autoPlayUrl;
        [Tooltip("自動再生の動画を繰り返す (配信ではなく動画のとき)")]
        public bool autoPlayLoop = true;

        [Header("何も再生していないときの画像")]
        [Tooltip("映像が出ていない間 (停止中・接続中・範囲の外) にスクリーンに出す画像。16:9 に引き伸ばして表示します。空なら付属の画像 (Textures/BuichaLiveIdleImage.jpg)")]
        public Texture idleTexture;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        // エディタで値を変えたら、その場でパネルとスクリーンに映す (実行時はパネルが Start で設定する)
        void OnValidate()
        {
            BuichaLivePanel panel = GetComponentInChildren<BuichaLivePanel>(true);
            if (panel == null) return;
            if (panel.idleRenderer != null)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                if (idleTexture != null) block.SetTexture("_MainTex", idleTexture);
                panel.idleRenderer.SetPropertyBlock(block);
            }
            // 文字の書き換えは OnValidate の中ではできないので、次のエディタ更新で行う
            int lang = (int)language;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (panel == null) return;
                UnityEngine.UI.Text[] texts = panel.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                RectTransform[] rects = panel.GetComponentsInChildren<RectTransform>(true);
                UnityEditor.Undo.RecordObjects(texts, "Buicha Live language");
                UnityEditor.Undo.RecordObjects(rects, "Buicha Live language");
                panel.SetLanguage(lang);
                panel.ApplyStaticTexts();
                foreach (UnityEngine.UI.Text t in texts) UnityEditor.EditorUtility.SetDirty(t);
                foreach (RectTransform r in rects) UnityEditor.EditorUtility.SetDirty(r);
            };
        }
#endif
    }
}
