using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Video.Components.Base;
using VRC.Udon.Common.Interfaces;

namespace BuichaLive
{
    /// <summary>
    /// ぶいちゃライブ (live.buicha.jp) をワールド内で見る「テレビ」パネル。
    ///
    /// チャンネルの数はチャンネルボタンの数 (4 チャンネルモード = 4、ステージモード = 1)。どのチャンネルも 2 つの入れ方ができる:
    /// - ワールドから発行: 空きチャンネルを選んで「同意して発行」。サーバーがプールから空きを 1 本割り当てる。
    ///   同期するのはプール内の番号 (index) と割り当て世代 (gen) だけ。再生 URL は埋め込んだ poolUrls[index]。
    ///   受け取りコードは押した本人にだけ表示し、同期しない。発行カードのリンク欄に claim ページの URL を入れる。
    /// - URL を貼る: 入力欄に URL を貼って再生。live.buicha.jp の配信のほか、YouTube などの動画も再生できる (サイトでは止めない)。
    ///
    /// 状態確認: 発行で入れたチャンネルを順番に GET /api/vrc/channel/&lt;name&gt; で確認し、
    /// gen が変わっていたらそのチャンネルを空きに戻す (オーナーだけが同期する)。live で配信中の表示を出す。
    ///
    /// 決まり:
    /// - 貼った URL はサイトで止めない (オーナーの方針。普通の動画プレイヤーと同じ)。どのサイトに接続するかは、見る人の
    ///   VRChat の設定 (信頼されていない URL を許可) が決める。同期で届いた URL も、形が URL かだけを確かめる。
    /// - サーバーの応答は型と範囲を確かめてから使う。受け取りコードは英数字だけを受け付ける。
    /// - 受け取りコードとリンクは同期しない。
    /// - 文言は Localize の表 (日本語 / English) にまとめ、ルートの設定の language で切り替える。
    ///
    /// 切り替え: チャンネルの中身 (URL・発行) と消す (✕) は全員で同期。どれを見るかと止める (■) は、ルートの設定
    ///   syncChannelSwitch が false なら自分だけ (何も見ていない人だけ、誰かが始めたチャンネル _syncedCurrent を見る)。
    ///   ステージモード (チャンネル 1 つ) はいつも全員。再生位置はチャンネルごとに _slotStartAt で全員そろえる。
    /// 自動再生: インスタンスを最初に開いた人がチャンネル 1 に入れて同期する (普通の URL チャンネルと同じ扱いで、✕ で消せる)。
    /// 待機画像: 映像が出ていない間だけ、スクリーンの手前の板 (idleScreen) を出す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class BuichaLivePanel : UdonSharpBehaviour
    {
        [Header("チャンネルプール (配布元が埋め込み済み。変更しない)")]
        [Tooltip("発行 API: https://dash.live.buicha.jp/api/vrc/claim?agree=1")]
        public VRCUrl claimUrl;
        [Tooltip("channels[i].vrchat を index 順に。rtspt://live.buicha.jp/<配信名>")]
        public VRCUrl[] poolUrls;
        [Tooltip("channels[i].status を index 順に。https://dash.live.buicha.jp/api/vrc/channel/<配信名>")]
        public VRCUrl[] poolStatusUrls;
        [Tooltip("コードを入力するページ。発行後に ?code=XXXX を付けて発行カードのリンク欄に入れる")]
        public string claimPageUrl = "https://dash.live.buicha.jp/claim";

        [Header("プレイヤー")]
        public BaseVRCVideoPlayer videoPlayer;
        public AudioSource speaker;
        [Tooltip("ルートの設定 (自動再生の URL・待機画像)")]
        public BuichaLiveSettings settings;
        [Tooltip("映像が出ていない間だけ表示する、スクリーンの手前の板")]
        public GameObject idleScreen;
        public Renderer idleRenderer;

        [Header("再生中")]
        public Text streamNameText;
        public Text streamNameSubText;
        public Text statusText;
        [Tooltip("配信中のときだけ表示する「● LIVE」")]
        public GameObject liveBadge;

        [Header("音量")]
        public Slider volumeSlider;
        public Text volumeValueText;

        [Header("再生バー (動画のときだけ、状態の文字の場所に出す)")]
        public GameObject seekBar;
        public Slider seekSlider;
        public Text seekTimeText;
        public Text seekDurationText;

        [Header("チャンネル (どれも発行でも URL 貼り付けでも使える)")]
        public GameObject[] channelButtons;
        public Image[] channelBackgrounds;
        public Image[] channelFrames;
        public Image[] channelDots;
        public Text[] channelTitleTexts;
        public Text[] channelStateTexts;
        [Tooltip("チャンネルごとの入れ方アイコン (空き = ＋ / URL = リンク / ワールドから発行 = 地球)")]
        public Image[] channelIcons;
        [Tooltip("アイコンの丸 (塗り / 枠)")]
        public Image[] channelIconCircles;
        public Image[] channelIconRings;
        [Tooltip("状態のピル (配信中 = オレンジ / 配信待ち = 薄いオレンジ / それ以外は透明)")]
        public Image[] channelStatePills;
        public Sprite iconEmpty;
        public Sprite iconUrl;
        public Sprite iconWorld;

        [Header("入れ方 (URL を入力 / live.buicha.jp で直接配信)")]
        [Tooltip("空きチャンネルで、まだ選んでいないときに出す 2 枚の大きな選択肢")]
        public GameObject choosePanel;
        [Tooltip("選ぶ前の見出し (「チャンネル 3 の配信方法を選ぶ」)")]
        public Text chooseTitle;
        [Tooltip("選んだあとの見出し行 (アイコン + 「チャンネル 3 ・ URL を入力」 + 選び直す)")]
        public GameObject formHeader;
        public Image formIcon;
        public Text formTitle;
        [Tooltip("「選び直す」。入っているチャンネルでは隠す")]
        public GameObject backButton;
        [Tooltip("URL を入力: 入力欄 + ▶")]
        public GameObject urlPanel;
        [Tooltip("live.buicha.jp で直接配信: 案内 + リンク欄 + コード + 同意して発行")]
        public GameObject livePanel;

        [Header("URL を入力")]
        public VRCUrlInputField urlInput;
        [Tooltip("入力欄 + ▶ (空きチャンネルのとき)")]
        public GameObject urlEntry;
        [Tooltip("URL を貼ったチャンネルを選んでいるとき、入力欄の代わりに出す表示欄")]
        public GameObject urlShowBar;
        public Text urlShowText;

        [Header("発行")]
        public Button claimButton;
        public Text claimButtonLabel;
        public Text claimStatusText;
        public Text codeText;
        public Text codeTimerText;
        [Tooltip("残り時間のピル (codeTimerText の親)")]
        public GameObject codeTimerPill;
        [Tooltip("発行後だけ出すリンク欄 (全体)。中の copyField に claim ページの URL を入れる")]
        public GameObject linkField;
        [Tooltip("普通の InputField。Udon から文字列を入れられる (VRCUrlInputField は入れられない)")]
        public InputField copyField;
        [Tooltip("発行後 / 発行済みチャンネルの案内 (左端)")]
        public Text claimGuideText;

        [Header("設定")]
        [Tooltip("発行で入れたチャンネル 1 つあたりの状態確認の間隔 (秒)。使用中の数で割って順番に取得する")]
        public float statusPollSeconds = 20f;
        [Tooltip("URL チャンネルの再接続までの秒数")]
        public float retrySeconds = 15f;
        [Tooltip("発行の応答を待つ最大秒数 (超えたらボタンを戻す)")]
        public float claimTimeoutSeconds = 30f;
        [Tooltip("この距離 (m) より離れたら、自分の画面だけ映像を止める (通信と負荷を減らす)。0 で無効")]
        public float cutoffDistance = 30f;
        [Tooltip("距離を測る基準 (スクリーン)。空ならパネル")]
        public Transform distanceAnchor;
        [Tooltip("映像を流す範囲の親。この下のコライダー (キューブなど) のどれかに頭が入っている間だけ再生する。コライダーが 1 つも無ければ距離で決める")]
        public Transform viewAreasRoot;

        [Header("言語で切り替える文字 (Build Panel が配線。言語はルートの設定)")]
        public Text brandText;
        public Text asideText;
        [Tooltip("見出しの横のオレンジの点と小さい文字。言語ごとに見出しの幅が違うので、x 位置を言語ごとに持つ")]
        public RectTransform brandDot;
        public RectTransform asideRect;
        [Tooltip("[日本語の点の x, 英語の点の x, 日本語の小さい文字の x, 英語の小さい文字の x]")]
        public float[] brandLayoutX;
        public Text[] optionTitleTexts;
        public Text[] optionSubTexts;
        public Text backLabelText;
        public RectTransform backUnderline;
        [Tooltip("[日本語の下線の幅, 英語の下線の幅]")]
        public float[] backUnderlineWidths;
        public Text playLabelText;
        public Text urlPlaceholderText;

        [Header("配色")]
        // live.buicha.jp と同じトークン (src/app/globals.css)
        public Color colorSurface = new Color(0.953f, 0.953f, 0.945f);          // --surface #f3f3f1
        public Color colorInk = new Color(0.067f, 0.067f, 0.067f);              // --ink #111111
        public Color colorOnInk = Color.white;
        public Color colorOnInk2 = new Color(0.788f, 0.788f, 0.769f);           // #c9c9c4
        public Color colorLine = new Color(0.812f, 0.812f, 0.796f);             // --line #cfcfcb
        public Color colorLineStrong = new Color(0.663f, 0.663f, 0.643f);       // --line-strong #a9a9a4
        public Color colorText = new Color(0.086f, 0.086f, 0.086f);             // --text #161616
        public Color colorText2 = new Color(0.361f, 0.361f, 0.345f);            // --text-2 #5c5c58
        public Color colorText3 = new Color(0.557f, 0.557f, 0.537f);            // --text-3 #8e8e89
        public Color colorAccent = new Color(0.961f, 0.647f, 0.141f);           // --accent #f5a524
        public Color colorAccentInk = new Color(0.647f, 0.353f, 0f);            // --accent-ink #a55a00
        public Color colorAccentSoft = new Color(0.984f, 0.890f, 0.722f);       // --accent-soft #fbe3b8
        public Color colorDanger = new Color(0.847f, 0.204f, 0.165f);           // --danger #d8342a

        const string EmptyName = "— — —";
        const int MaxUrlLength = 2048;
        const int MaxCodeLength = 12;

        // ---- 表示の文言 (日本語 / English)。ルートの設定 (BuichaLiveSettings.language) で切り替える ----
        public const int S_Brand = 0;
        public const int S_Aside = 1;
        public const int S_Opt0Title = 2;
        public const int S_Opt0Sub = 3;
        public const int S_Opt1Title = 4;
        public const int S_Opt1Sub = 5;
        public const int S_Back = 6;
        public const int S_Play = 7;
        public const int S_Placeholder = 8;
        public const int S_SelectChannel = 9;
        public const int S_SelectMethod = 10;
        public const int S_ChannelN = 11;
        public const int S_Stage = 12;
        public const int S_StateEmpty = 13;
        public const int S_StateLive = 14;
        public const int S_StateWaiting = 15;
        public const int S_AutoPlay = 16;
        public const int S_ChooseTitleN = 17;
        public const int S_ChooseTitle = 18;
        public const int S_HeaderSep = 19;
        public const int S_ModeUrl = 20;
        public const int S_ModeLive = 21;
        public const int S_PasteUrl = 22;
        public const int S_BadUrl = 23;
        public const int S_NoEmptyChannel = 24;
        public const int S_ClaimIdle = 25;
        public const int S_ClaimBusy = 26;
        public const int S_Terms = 27;
        public const int S_Claiming = 28;
        public const int S_OtherChannel = 29;
        public const int S_NoClaimHere = 30;
        public const int S_ConnectError = 31;
        public const int S_WorldUpdate = 32;
        public const int S_ClaimFailed = 33;
        public const int S_Full = 34;
        public const int S_RateLimited = 35;
        public const int S_ChannelTaken = 36;
        public const int S_OpenLink = 37;
        public const int S_Remain = 38;
        public const int S_CodeExpired = 39;
        public const int S_Stopped = 40;
        public const int S_Connecting = 41;
        public const int S_Reconnecting = 42;
        public const int S_Ready = 43;
        public const int S_Playing = 44;
        public const int S_StreamEnded = 45;
        public const int S_VideoEnded = 46;
        public const int S_WaitingStart = 47;
        public const int S_ErrPool = 48;
        public const int S_ErrUrl = 49;
        public const int S_NothingToRemove = 50;
        public const int S_OutOfArea = 51;
        public const int S_FarAway = 52;

        /// <summary>文言を返す。lang: 0 = 日本語、1 = English。{N} などは呼び出し側で置き換える。</summary>
        public string Localize(int id, int lang)
        {
            bool en = lang == 1;
            switch (id)
            {
                case S_Brand: return en ? "Buicha Live" : "ぶいちゃライブ";
                case S_Aside: return en ? "Live streaming\nfor VRChat" : "VRChat向け\nライブ配信";
                case S_Opt0Title: return en ? "Play a URL" : "URL を再生";
                case S_Opt0Sub: return en ? "Play a link from YouTube and other sites" : "YouTubeなどのリンクを再生";
                case S_Opt1Title: return en ? "Go live with BuichaLive" : "BuichaLive でライブ配信";
                case S_Opt1Sub: return en ? "Stream from OBS or your browser with Buicha Live" : "ぶいちゃライブを使用して、OBS やブラウザからライブ配信";
                case S_Back: return en ? "Back" : "戻る";
                case S_Play: return en ? "Play" : "再生";
                case S_Placeholder: return en ? "Paste a URL" : "URL を貼り付け";
                case S_SelectChannel: return en ? "Select a channel" : "チャンネルを選んでください";
                case S_SelectMethod: return en ? "Choose what to play" : "配信方法を選んでください";
                case S_ChannelN: return en ? "Channel {N}" : "チャンネル {N}";
                case S_Stage: return en ? "Stage" : "ステージ";
                case S_StateEmpty: return en ? "Empty" : "空き";
                case S_StateLive: return en ? "Live" : "配信中";
                case S_StateWaiting: return en ? "Waiting" : "配信待ち";
                case S_AutoPlay: return en ? "Auto play" : "自動再生";
                case S_ChooseTitleN: return en ? "Choose what to play on {CH}" : "{CH} の配信方法を選ぶ";
                case S_ChooseTitle: return en ? "Choose what to play" : "配信方法を選ぶ";
                case S_HeaderSep: return en ? " · " : " ・ ";
                case S_ModeUrl: return en ? "Play a URL" : "URL を再生";
                case S_ModeLive: return en ? "Go live with BuichaLive" : "BuichaLive でライブ配信";
                case S_PasteUrl: return en ? "Paste a URL first" : "URL を貼り付けてください";
                case S_BadUrl: return en ? "This link can't be played" : "許可していないリンクです";
                case S_NoEmptyChannel: return en ? "No empty channel. Remove a channel you aren't using" : "空きチャンネルがありません。使っていないチャンネルを消してください";
                case S_ClaimIdle: return en ? "Agree and get a code" : "同意してコードを発行";
                case S_ClaimBusy: return en ? "Getting a code" : "発行中";
                case S_Terms: return en ? "Getting a code means you agree to the Terms of Service." : "発行すると利用規約に同意したことになります。";
                case S_Claiming: return en ? "Getting a code" : "発行しています";
                case S_OtherChannel: return en ? "{CH} streams from BuichaLive" : "{CH} は BuichaLive から配信します";
                case S_NoClaimHere: return en ? "Codes can't be issued in this world" : "このワールドでは発行できません";
                case S_ConnectError: return en ? "Can't reach the server. Try again in a moment" : "サーバーに接続できません。しばらくして試してください";
                case S_WorldUpdate: return en ? "This world needs an update" : "ワールドの更新が必要です";
                case S_ClaimFailed: return en ? "Couldn't get a code" : "発行できません";
                case S_Full: return en ? "No channels are free. Create a new stream link on BuichaLive." : "空きがありません。BuichaLive で新しくライブ配信用のリンクを作成してください。";
                case S_RateLimited: return en ? "Wait a moment and try again" : "少し待ってからもう一度押してください";
                case S_ChannelTaken: return en ? "No empty channel is left. Remove a channel, then try again" : "空きチャンネルがなくなりました。チャンネルを消してから、もう一度発行してください";
                case S_OpenLink: return en ? "Open the link in a browser on your PC" : "リンクを PC のブラウザで開く";
                case S_Remain: return en ? "{T} left" : "残り {T}";
                case S_CodeExpired: return en ? "The code expired. Get a new one" : "コードの期限が切れました。もう一度発行してください";
                case S_Stopped: return en ? "Stopped" : "停止中";
                case S_Connecting: return en ? "Connecting" : "接続中";
                case S_Reconnecting: return en ? "Reconnecting" : "再接続中";
                case S_Ready: return en ? "Ready to play" : "再生の準備ができました";
                case S_Playing: return en ? "Playing" : "再生中";
                case S_StreamEnded: return en ? "The stream has ended" : "配信が終わりました";
                case S_VideoEnded: return en ? "Playback finished" : "再生が終わりました";
                case S_WaitingStart: return en ? "Waiting for the stream to start" : "配信が始まるのを待っています";
                case S_ErrPool: return en ? "Can't connect ({E}). Retrying on the next check" : "接続できません ({E})。次の確認で再接続します";
                case S_ErrUrl: return en ? "Can't connect ({E}). Retrying in {S} s" : "接続できません ({E})。{S} 秒後に再試行";
                case S_NothingToRemove: return en ? "Select a channel to remove" : "消すチャンネルを選んでください";
                case S_OutOfArea: return en ? "Video paused: you're outside the viewing area" : "再生範囲外なので映像を止めています";
                case S_FarAway: return en ? "Video paused: you're too far away" : "離れているので映像を止めています";
            }
            return "";
        }

        private string L(int id) { return Localize(id, _lang); }

        // ---- 同期する状態 ----
        [UdonSynced] private int[] _slotIndex;      // チャンネルごとのプール番号 (発行で入れたとき)。それ以外は -1
        [UdonSynced] private int[] _slotGen;        // チャンネルごとの割り当て世代。それ以外は 0
        [UdonSynced] private VRCUrl[] _channelUrls; // チャンネルごとの貼った URL。それ以外は空
        [UdonSynced] private int _syncedCurrent = -1; // 全員で見るチャンネル。「自分だけ」のときは、最後に始めたチャンネル (まだ選んでいない人が見る)
        [UdonSynced] private double[] _slotStartAt;   // チャンネルごとに、動画の 0 秒がサーバー時刻でいつだったか (再生位置をそろえる)。0 = 不明

        // ---- ローカル ----
        private int _channelCount;
        private int _current = -1;         // 自分が見ているチャンネル (ローカル)。-1 = 止めている
        private bool _syncSwitch = true;   // true: 切り替えを全員で同期 / false: 切り替えと停止は自分だけ
        private bool _userChose;           // 「自分だけ」のとき、自分でチャンネルを選んだ / 止めた (止めた後に、ほかの人が始めたチャンネルへ勝手に移らない)
        private int _focus = -1;           // パネルで選んでいるチャンネル (ローカル)。空きなら発行 / URL の入れ先
        private int _mode = -1;            // 空きチャンネルの入れ方。-1 = 未選択、0 = URL を入力、1 = live.buicha.jp で直接配信
        private string _claimMsg = "";
        private Color _claimColor;
        private string _playingUrl = "";   // いま PlayURL した URL (重複再生を避ける)
        private bool _hasStatusApi;        // 再生中のチャンネルが発行で入れたものか (状態 API がある)
        private bool _videoPlaying;
        private float _lastPlayAt = -999f; // 最後に PlayURL した時刻 (再接続の間隔を空ける)
        private bool[] _slotLive;          // チャンネルごとの live (状態 API の結果)
        private int _pollCursor;

        private bool _claimPending;
        private int _claimSlot = -1;
        private float _claimStartedAt;
        private string _code = "";
        private float _codeExpireAt;
        private bool _codeTicking;
        private bool _farAway;             // 離れていて映像を止めている (ローカル)
        private string _retryUrl = "";     // 再試行を予約した URL (古い予約を無視するため)
        private float _retryAt;
        private Collider[] _viewAreas;     // viewAreasRoot の下のコライダー (Start で集める)
        private VRCUrl _autoPlayUrl;       // settings.autoPlayUrl (空なら VRCUrl.Empty)
        private bool _autoPlayLoop;
        private int _lang;                 // 0 = 日本語、1 = English (settings.language)
        private bool _seekUpdating;        // スクリプトが再生バーを動かしている間 (つまみの変更を操作と見なさない)
        private float _seekUserAt = -999f; // 最後に人が再生バーを動かした時刻
        private float _seekTarget;         // 動かした先 (0〜1)
        private float _lastSyncSeekAt = -999f;
        private bool _seekShown;

        void Start()
        {
            EnsureState();
            if (viewAreasRoot != null) _viewAreas = viewAreasRoot.GetComponentsInChildren<Collider>(true);

            _autoPlayUrl = VRCUrl.Empty;
            _lang = 0;
            if (settings != null)
            {
                _lang = (int)settings.language == 1 ? 1 : 0;
                _syncSwitch = settings.syncChannelSwitch;
                if (settings.autoPlayUrl != null) _autoPlayUrl = settings.autoPlayUrl;
                _autoPlayLoop = settings.autoPlayLoop;
                if (settings.idleTexture != null && idleRenderer != null) idleRenderer.material.SetTexture("_MainTex", settings.idleTexture);
            }

            // ステージモードはチャンネルのボタンが無く自分で選べないので、いつも全員で同じものを見る
            if (_channelCount <= 1) _syncSwitch = true;
            ApplyStaticTexts();
            if (volumeSlider != null && speaker != null) volumeSlider.value = speaker.volume;
            RefreshVolumeText();

            SetStreamName(EmptyName, "");
            SetStatus(_channelCount > 1 ? L(S_SelectChannel) : L(S_SelectMethod));
            SetLive(false);
            SetClaimButton(true);
            SetClaimStatus(L(S_Terms), colorText3);
            SetCode("", 0f);
            HideLink();
            RefreshChannels();

            SendCustomEventDelayedSeconds("_Poll", 3f);
            SendCustomEventDelayedSeconds("_Tick", 1f);
            SendCustomEventDelayedSeconds("_SeekTick", 0.5f);
            if (seekBar != null) seekBar.SetActive(false);
            if (!IsEmpty(_autoPlayUrl)) SendCustomEventDelayedSeconds("_AutoPlay", 2f);
            RefreshIdle();
        }

        /// <summary>
        /// 言語で変わる、置いたままの文字 (見出し・選択肢・ボタン・入力欄の案内) を入れ直す。
        /// 実行時は Start が、エディタではルートの設定を変えたときと Build Panel が呼ぶ (SetLanguage で言語を決めてから)。
        /// </summary>
        public void ApplyStaticTexts()
        {
            int l = _lang == 1 ? 1 : 0;
            if (brandText != null) brandText.text = L(S_Brand);
            if (asideText != null) asideText.text = L(S_Aside);
            if (brandLayoutX != null && brandLayoutX.Length >= 4)
            {
                if (brandDot != null) brandDot.anchoredPosition = new Vector2(brandLayoutX[l], brandDot.anchoredPosition.y);
                if (asideRect != null) asideRect.anchoredPosition = new Vector2(brandLayoutX[2 + l], asideRect.anchoredPosition.y);
            }
            if (optionTitleTexts != null && optionTitleTexts.Length >= 2)
            {
                if (optionTitleTexts[0] != null) optionTitleTexts[0].text = L(S_Opt0Title);
                if (optionTitleTexts[1] != null) optionTitleTexts[1].text = L(S_Opt1Title);
            }
            if (optionSubTexts != null && optionSubTexts.Length >= 2)
            {
                if (optionSubTexts[0] != null) optionSubTexts[0].text = L(S_Opt0Sub);
                if (optionSubTexts[1] != null) optionSubTexts[1].text = L(S_Opt1Sub);
            }
            if (backLabelText != null) backLabelText.text = L(S_Back);
            if (backUnderline != null && backUnderlineWidths != null && backUnderlineWidths.Length >= 2)
            {
                // 下線は右寄せ: 幅を変えたら左端もずらす
                float w = backUnderlineWidths[l];
                float right = backUnderline.anchoredPosition.x + backUnderline.sizeDelta.x;
                backUnderline.sizeDelta = new Vector2(w, backUnderline.sizeDelta.y);
                backUnderline.anchoredPosition = new Vector2(right - w, backUnderline.anchoredPosition.y);
            }
            if (playLabelText != null) playLabelText.text = L(S_Play);
            if (urlPlaceholderText != null) urlPlaceholderText.text = L(S_Placeholder);
            if (claimButtonLabel != null) claimButtonLabel.text = L(S_ClaimIdle);
            if (claimStatusText != null) claimStatusText.text = L(S_Terms);
            if (chooseTitle != null) chooseTitle.text = L(S_ChooseTitle);
            if (statusText != null) statusText.text = channelButtons != null && channelButtons.Length > 1 ? L(S_SelectChannel) : L(S_SelectMethod);
            if (channelTitleTexts != null)
                for (int i = 0; i < channelTitleTexts.Length; i++)
                    if (channelTitleTexts[i] != null) channelTitleTexts[i].text = channelTitleTexts.Length == 1 ? L(S_Stage) : L(S_ChannelN).Replace("{N}", (i + 1).ToString());
            if (channelStateTexts != null)
                for (int i = 0; i < channelStateTexts.Length; i++)
                    if (channelStateTexts[i] != null) channelStateTexts[i].text = L(S_StateEmpty);
        }

        /// <summary>エディタ用: 言語を決める (実行時は Start がルートの設定から読む)。</summary>
        public void SetLanguage(int lang) { _lang = lang == 1 ? 1 : 0; }

        /// <summary>
        /// 最初に自動で再生する動画: インスタンスを最初に開いた人 (マスター兼オーナー) だけが、チャンネル 1 に入れて同期する。
        /// 後から来た人は同期で受け取るので、ここでは何もしない。誰かがもう何か入れていたら入れない。
        /// </summary>
        public void _AutoPlay()
        {
            if (IsEmpty(_autoPlayUrl) || _channelCount < 1) return;
            if (!Networking.IsMaster || !Networking.IsOwner(gameObject)) return;
            if (_syncedCurrent >= 0) return;
            for (int i = 0; i < _channelCount; i++) if (IsFilled(i)) return;

            _slotIndex[0] = -1;
            _slotGen[0] = 0;
            _slotLive[0] = false;
            _channelUrls[0] = _autoPlayUrl;
            _slotStartAt[0] = Networking.GetServerTimeInSeconds();
            StartChannel(0);
            RequestSerialization();
            ApplyState();
        }

        private bool IsAutoPlayUrl(string url)
        {
            return !IsEmpty(_autoPlayUrl) && url == _autoPlayUrl.Get();
        }

        /// <summary>映像が出ていない間 (停止中・接続中・範囲の外・終わった後) だけ待機画像を出す。</summary>
        private void RefreshIdle()
        {
            if (idleScreen == null) return;
            bool show = !IsFilled(_current) || _farAway || !IsVideoActive();
            if (idleScreen.activeSelf != show) idleScreen.SetActive(show);
        }

        /// <summary>配列の長さを UI に合わせる。Start と OnDeserialization のどちらが先でも動くようにする。</summary>
        private void EnsureState()
        {
            _channelCount = channelButtons != null ? channelButtons.Length : 0;

            if (_slotIndex == null || _slotIndex.Length != _channelCount)
            {
                _slotIndex = new int[_channelCount];
                for (int i = 0; i < _channelCount; i++) _slotIndex[i] = -1;
            }
            if (_slotGen == null || _slotGen.Length != _channelCount) _slotGen = new int[_channelCount];
            if (_slotStartAt == null || _slotStartAt.Length != _channelCount) _slotStartAt = new double[_channelCount];
            if (_slotLive == null || _slotLive.Length != _channelCount) _slotLive = new bool[_channelCount];
            if (_channelUrls == null || _channelUrls.Length != _channelCount)
            {
                _channelUrls = new VRCUrl[_channelCount];
                for (int i = 0; i < _channelCount; i++) _channelUrls[i] = VRCUrl.Empty;
            }
        }

        // ---- ボタン (Button.onClick → UdonBehaviour.SendCustomEvent) ----

        public void Channel0() { SelectChannel(0); }
        public void Channel1() { SelectChannel(1); }
        public void Channel2() { SelectChannel(2); }
        public void Channel3() { SelectChannel(3); }

        /// <summary>「URL を入力」を選ぶ (空きチャンネルを選んでいるときだけ押せる)。</summary>
        public void ModeUrl() { _mode = 0; RefreshSource(); }

        /// <summary>「live.buicha.jp で直接配信」を選ぶ。</summary>
        public void ModeLive() { _mode = 1; RefreshSource(); }

        /// <summary>「選び直す」: 2 枚の選択肢に戻る。</summary>
        public void ModeBack() { _mode = -1; RefreshSource(); }

        /// <summary>「同意して発行」: サーバーにプールから 1 本予約させ、選んだ空きチャンネルに入れる。</summary>
        public void Claim()
        {
            if (_claimPending) return;
            if (IsEmpty(claimUrl) || poolUrls == null || poolUrls.Length == 0)
            {
                SetClaimStatus(L(S_NoClaimHere), colorDanger);
                return;
            }

            int slot = TargetOrFirstEmpty();
            if (slot < 0)
            {
                SetClaimStatus(L(S_NoEmptyChannel), colorDanger);
                return;
            }

            _claimPending = true;
            _claimSlot = slot;
            _claimStartedAt = Time.time;
            SetClaimButton(false);
            SetClaimStatus(L(S_Claiming), colorText2);
            VRCStringDownloader.LoadUrl(claimUrl, (IUdonEventReceiver)this);
            SendCustomEventDelayedSeconds("_ClaimTimeout", claimTimeoutSeconds);
        }

        public void _ClaimTimeout()
        {
            if (!_claimPending) return;
            if (Time.time - _claimStartedAt < claimTimeoutSeconds - 0.5f) return;
            _claimPending = false;
            SetClaimButton(true);
            SetClaimStatus(L(S_ConnectError), colorDanger);
        }

        /// <summary>▶: 入力欄の URL を、選んだ空きチャンネル (なければ最初の空き) に入れて再生する。</summary>
        public void AddAndPlay()
        {
            if (urlInput == null) return;
            VRCUrl url = urlInput.GetUrl();
            if (IsEmpty(url))
            {
                SetStatus(L(S_PasteUrl));
                return;
            }
            if (!IsPlayableUrl(url))
            {
                SetStatus(L(S_BadUrl));
                return;
            }

            int slot = TargetOrFirstEmpty();
            if (slot < 0)
            {
                SetStatus(L(S_NoEmptyChannel));
                return;
            }

            TakeOwnership();
            _slotIndex[slot] = -1;
            _slotGen[slot] = 0;
            _slotLive[slot] = false;
            _channelUrls[slot] = url;
            _slotStartAt[slot] = Networking.GetServerTimeInSeconds();
            StartChannel(slot);
            _focus = slot;
            _mode = -1;
            RequestSerialization();
            ApplyState();

            urlInput.SetUrl(VRCUrl.Empty);
        }

        /// <summary>✕: 選んでいるチャンネル (空きなら再生中のチャンネル) を空にする。</summary>
        public void RemoveChannel()
        {
            int index = IsFilled(_focus) ? _focus : _current;
            if (!IsFilled(index))
            {
                SetStatus(L(S_NothingToRemove));
                return;
            }
            TakeOwnership();
            ClearChannel(index);
            RequestSerialization();
            ApplyState();
        }

        /// <summary>■: 再生を止める (チャンネルは残す)。</summary>
        public void StopStream()
        {
            if (_syncSwitch)
            {
                TakeOwnership();
                _syncedCurrent = -1;
                RequestSerialization();
            }
            else _userChose = true; // 自分だけ止める
            _current = -1;
            ApplyState();
        }

        /// <summary>
        /// 入れたチャンネルを始める (URL を入れた・発行した・自動再生)。自分はそのチャンネルを見る。
        /// 「全員」なら全員が、「自分だけ」なら、まだ自分で選んでいない人がついて来る。呼んだ側が RequestSerialization する。
        /// </summary>
        private void StartChannel(int index)
        {
            _syncedCurrent = index;
            _current = index;
        }

        /// <summary>↻: 再接続。</summary>
        public void Resync()
        {
            if (videoPlayer == null || _farAway) return;
            VRCUrl url = ChannelUrl(_current);
            if (IsEmpty(url)) return;
            SetStatus(L(S_Reconnecting));
            _lastPlayAt = Time.time;
            videoPlayer.Loop = IsAutoPlayUrl(url.Get()) && _autoPlayLoop;
            videoPlayer.PlayURL(url);
        }

        public void OnVolumeChanged()
        {
            if (speaker != null && volumeSlider != null) speaker.volume = volumeSlider.value;
            RefreshVolumeText();
        }

        private void RefreshVolumeText()
        {
            if (volumeValueText != null && volumeSlider != null) volumeValueText.text = Mathf.RoundToInt(volumeSlider.value * 100f).ToString();
        }

        // ---- チャンネル ----

        private void SelectChannel(int index)
        {
            if (index < 0 || index >= _channelCount) return;

            if (index != _focus) _mode = -1; // 別のチャンネルでは、入れ方の 2 択から始める
            _focus = index;
            if (!IsFilled(index))
            {
                // 空き: 下のカードで「URL を入力」か「live.buicha.jp で直接配信」を選んで入れる
                RefreshChannels();
                return;
            }

            if (_syncSwitch)
            {
                TakeOwnership();
                _syncedCurrent = index;
                RequestSerialization();
            }
            else _userChose = true; // 自分の画面だけ切り替える
            _current = index;
            ApplyState();
        }

        private bool IsFilled(int index)
        {
            return IsPool(index) || HasUrl(index);
        }

        /// <summary>発行で入れたチャンネルか (状態 API で確認する対象)。</summary>
        private bool IsPool(int index)
        {
            return index >= 0 && index < _channelCount && PoolIndexValid(_slotIndex[index]);
        }

        /// <summary>貼った URL が入っているか (同期で届いた値も、形が URL のものだけを「入っている」とみなす)。</summary>
        private bool HasUrl(int index)
        {
            return index >= 0 && index < _channelCount && IsPlayableUrl(_channelUrls[index]);
        }

        /// <summary>
        /// 再生してよい URL か。サイトでは止めない (YouTube なども貼れる)。形だけ確かめる: 空でない、長すぎない、「://」がある。
        /// どのサイトに接続してよいかは、見る人の VRChat の設定 (信頼されていない URL を許可) が決める。
        /// </summary>
        private bool IsPlayableUrl(VRCUrl url)
        {
            if (IsEmpty(url)) return false;
            string value = url.Get();
            return value.Length <= MaxUrlLength && value.IndexOf("://") > 0;
        }

        /// <summary>配信 (RTSP / RTMP) の URL か。LIVE の表示はこれと発行したチャンネルだけに出す (動画には出さない)。</summary>
        private bool IsStreamUrl(string url)
        {
            string lower = url.ToLower();
            return lower.StartsWith("rtsp://") || lower.StartsWith("rtspt://") || lower.StartsWith("rtmp://") || lower.StartsWith("rtmps://");
        }

        private bool PoolIndexValid(int poolIndex)
        {
            return poolUrls != null && poolIndex >= 0 && poolIndex < poolUrls.Length && !IsEmpty(poolUrls[poolIndex]);
        }

        private VRCUrl ChannelUrl(int index)
        {
            if (IsPool(index)) return poolUrls[_slotIndex[index]];
            if (HasUrl(index)) return _channelUrls[index];
            return VRCUrl.Empty;
        }

        /// <summary>選んでいる空きチャンネル。選んでいなければ最初の空き。空きが無ければ -1。</summary>
        private int TargetOrFirstEmpty()
        {
            if (_focus >= 0 && _focus < _channelCount && !IsFilled(_focus)) return _focus;
            for (int i = 0; i < _channelCount; i++) if (!IsFilled(i)) return i;
            return -1;
        }

        private void ClearChannel(int index)
        {
            if (index < 0 || index >= _channelCount) return;
            _slotIndex[index] = -1;
            _slotGen[index] = 0;
            _slotLive[index] = false;
            _channelUrls[index] = VRCUrl.Empty;
            _slotStartAt[index] = 0;
            if (_current == index) _current = -1;
            if (_syncedCurrent == index) _syncedCurrent = -1;
        }

        private bool IsEmpty(VRCUrl url)
        {
            return url == null || string.IsNullOrEmpty(url.Get());
        }

        /// <summary>「チャンネル 1」のような呼び名。ステージモード (チャンネル 1 本) は「ステージ」。</summary>
        private string ChannelTitle(int index)
        {
            if (_channelCount == 1) return L(S_Stage);
            return L(S_ChannelN).Replace("{N}", (index + 1).ToString());
        }

        private string ChannelName(int index)
        {
            VRCUrl url = ChannelUrl(index);
            if (IsEmpty(url)) return "";
            if (IsAutoPlayUrl(url.Get())) return L(S_AutoPlay);
            return NameFromUrl(url.Get());
        }

        private string NameFromUrl(string url)
        {
            if (!IsStreamUrl(url))
            {
                // 動画サイトなど: サイトの名前 (www. は省く)
                int scheme = url.IndexOf("://");
                string host = scheme >= 0 ? url.Substring(scheme + 3) : url;
                int cut = host.IndexOf('/');
                if (cut >= 0) host = host.Substring(0, cut);
                cut = host.IndexOf('?');
                if (cut >= 0) host = host.Substring(0, cut);
                if (host.ToLower().StartsWith("www.")) host = host.Substring(4);
                return host;
            }
            int q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            while (url.Length > 0 && url[url.Length - 1] == '/') url = url.Substring(0, url.Length - 1);
            int slash = url.LastIndexOf('/');
            if (slash >= 0 && slash < url.Length - 1) return url.Substring(slash + 1);
            return url;
        }

        private void RefreshChannels()
        {
            if (channelButtons == null) return;
            for (int i = 0; i < _channelCount; i++)
            {
                bool isPool = IsPool(i);
                bool filled = IsFilled(i);
                bool active = filled && i == _current;
                bool targeted = i == FocusChannel();
                bool live = isPool ? _slotLive[i] : (active && _videoPlaying && IsStreamUrl(ChannelUrl(i).Get()));

                // live.buicha.jp の method-card: 面 #f3f3f1 + 線。再生中は黒の面 (アイコンの丸はオレンジ)。選んでいるチャンネルは黒の枠
                string state;
                Color stateColor;
                Color pill = new Color(0f, 0f, 0f, 0f);
                bool dot = false;
                if (!filled) { state = L(S_StateEmpty); stateColor = active ? colorOnInk2 : colorText3; }
                else if (live) { state = L(S_StateLive); stateColor = colorInk; pill = colorAccent; dot = true; }
                else if (isPool) { state = L(S_StateWaiting); stateColor = colorAccentInk; pill = colorAccentSoft; }
                else
                {
                    state = ChannelName(i);
                    if (state.Length > 12) state = state.Substring(0, 11) + "…";
                    stateColor = active ? colorOnInk2 : colorText2;
                }

                SetImageColor(channelBackgrounds, i, active ? colorInk : colorSurface);
                SetImageColor(channelFrames, i, active || targeted ? colorInk : colorLine);
                SetImageColor(channelStatePills, i, pill);
                SetImageColor(channelDots, i, dot ? colorInk : new Color(0f, 0f, 0f, 0f));
                SetText(channelTitleTexts, i, ChannelTitle(i), active ? colorOnInk : colorInk);
                SetText(channelStateTexts, i, state, stateColor);
                SetImageColor(channelIconCircles, i, active ? colorAccent : new Color(0f, 0f, 0f, 0f));
                SetImageColor(channelIconRings, i, active ? colorAccent : colorLineStrong);
                SetIcon(i, isPool ? iconWorld : (filled ? iconUrl : iconEmpty), active ? colorInk : (filled ? colorAccentInk : colorText3));
            }
            RefreshSource();
        }

        private void SetIcon(int i, Sprite sprite, Color color)
        {
            if (channelIcons == null || i >= channelIcons.Length || channelIcons[i] == null) return;
            channelIcons[i].sprite = sprite;
            channelIcons[i].color = color;
        }

        /// <summary>下のカードの対象。パネルで選んだチャンネル、なければ再生中のチャンネル。</summary>
        /// <summary>
        /// 下のカードが対象にするチャンネル。選んでいればそれ。選んでいなければ最初の空き (2 択から始める)。
        /// 空きが無ければ再生中のチャンネル。
        /// </summary>
        private int FocusChannel()
        {
            if (_focus >= 0 && _focus < _channelCount) return _focus;
            int empty = TargetOrFirstEmpty();
            return empty >= 0 ? empty : _current;
        }

        /// <summary>
        /// 下のカードを対象チャンネルに合わせる。
        /// 空き (または未選択) で入れ方をまだ選んでいない: 2 枚の大きな選択肢だけを出す。
        /// 選んだあと: 見出し行 (どのチャンネルに、どちらで入れるか) + その入力だけを出す。
        /// 入っているチャンネル: その入れ方に固定し、「選び直す」は出さない。
        /// </summary>
        private void RefreshSource()
        {
            int f = FocusChannel();
            bool filled = IsFilled(f);
            bool pool = IsPool(f);
            int mode = filled ? (pool ? 1 : 0) : _mode;
            int target = filled ? f : TargetOrFirstEmpty();

            if (choosePanel != null) choosePanel.SetActive(mode < 0);
            if (formHeader != null) formHeader.SetActive(mode >= 0);
            if (urlPanel != null) urlPanel.SetActive(mode == 0);
            if (livePanel != null) livePanel.SetActive(mode == 1);
            if (backButton != null) backButton.SetActive(!filled);
            if (formIcon != null) formIcon.sprite = mode == 1 ? iconWorld : iconUrl;
            if (formTitle != null)
            {
                string modeName = mode == 1 ? L(S_ModeLive) : L(S_ModeUrl);
                formTitle.text = target >= 0 && _channelCount > 1 ? ChannelTitle(target) + L(S_HeaderSep) + modeName : modeName;
            }

            // URL を入力: URL を貼ったチャンネルならその URL を見せる
            bool showUrl = filled && !pool;
            if (urlEntry != null) urlEntry.SetActive(!showUrl);
            if (urlShowBar != null) urlShowBar.SetActive(showUrl);
            if (urlShowText != null) urlShowText.text = showUrl ? ChannelUrl(f).Get() : "";

            // live.buicha.jp で直接配信
            // 発行前: [同意して発行] [規約 / エラー]。発行後 (押した本人): [案内] [リンク] [コード]。それ以外: [案内] だけ
            bool mine = pool && f == _claimSlot && _code != "";
            if (claimButton != null) claimButton.gameObject.SetActive(!filled);
            if (claimStatusText != null)
            {
                claimStatusText.gameObject.SetActive(!filled);
                claimStatusText.text = _claimMsg;
                claimStatusText.color = _claimColor;
            }
            if (claimGuideText != null)
            {
                claimGuideText.gameObject.SetActive(filled);
                claimGuideText.text = mine ? L(S_OpenLink) : L(S_OtherChannel).Replace("{CH}", ChannelTitle(f));
            }
            if (linkField != null) linkField.SetActive(mine);
            if (codeText != null) codeText.gameObject.SetActive(mine);
            if (codeTimerPill != null) codeTimerPill.SetActive(mine);
            if (chooseTitle != null) chooseTitle.text = target >= 0 && _channelCount > 1 ? L(S_ChooseTitleN).Replace("{CH}", ChannelTitle(target)) : L(S_ChooseTitle);
        }

        private void SetImageColor(Image[] images, int i, Color color)
        {
            if (images != null && i < images.Length && images[i] != null) images[i].color = color;
        }

        private void SetText(Text[] texts, int i, string value, Color color)
        {
            if (texts == null || i >= texts.Length || texts[i] == null) return;
            texts[i].text = value;
            texts[i].color = color;
        }

        // ---- 同期 ----

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public override void OnDeserialization()
        {
            EnsureState();
            if (_syncSwitch) _current = _syncedCurrent; // 全員で同じチャンネルを見る
            else
            {
                // 自分だけ: 見ているチャンネルはほかの人が何をしても変えない。何も見ていない人 (入ってきたばかり、
                // 見ていたチャンネルが消された) だけ、誰かが始めたチャンネルを見る。自分で止めた人はついて行かない
                if (_current >= 0 && !IsFilled(_current)) _current = -1;
                if (_current < 0 && !_userChose) _current = _syncedCurrent;
            }
            ApplyState();
        }

        private void ApplyState()
        {
            ApplyStateCore();
            RefreshIdle();
        }

        private void ApplyStateCore()
        {
            EnsureState();
            RefreshChannels();

            if (!IsFilled(_current))
            {
                if (_playingUrl != "")
                {
                    _playingUrl = "";
                    _videoPlaying = false;
                    StopPlayer();
                }
                _hasStatusApi = false;
                SetStreamName(EmptyName, "");
                SetStatus(L(S_Stopped));
                SetLive(false);
                return;
            }

            _hasStatusApi = IsPool(_current);
            SetStreamName(ChannelTitle(_current), ChannelName(_current));

            if (_farAway)
            {
                // 離れている間は再生しない。戻ってきたら _Tick が ApplyState を呼び直す
                StopLocal();
                SetStatus(HasViewAreas() ? L(S_OutOfArea) : L(S_FarAway));
                if (_hasStatusApi) SetLive(_slotLive[_current]); else SetLive(false);
                return;
            }

            VRCUrl url = ChannelUrl(_current);
            if (url.Get() != _playingUrl)
            {
                // 前の動画を止めてから読み込む。止めないと、読み込みの間は前の動画が流れ続け、
                // 再生位置の同期がそれを新しい動画の位置 (頭) に戻してしまう。読み込み中は待機画像を出す
                if (_playingUrl != "") StopPlayer();
                _playingUrl = url.Get();
                _videoPlaying = false;
                SetStatus(L(S_Connecting));
                if (_hasStatusApi)
                {
                    SetLive(_slotLive[_current]);
                    if (PoolIndexValid(_slotIndex[_current]) && poolStatusUrls != null && _slotIndex[_current] < poolStatusUrls.Length)
                        VRCStringDownloader.LoadUrl(poolStatusUrls[_slotIndex[_current]], (IUdonEventReceiver)this);
                }
                else SetLive(false);
                _lastPlayAt = Time.time;
                if (videoPlayer != null)
                {
                    // 自動再生の動画だけ繰り返せる (配信は繰り返さない)
                    videoPlayer.Loop = IsAutoPlayUrl(url.Get()) && _autoPlayLoop;
                    videoPlayer.PlayURL(url);
                }
            }
            else if (_hasStatusApi) SetLive(_slotLive[_current]);
        }

        // ---- ビデオプレイヤーのイベント ----

        public override void OnVideoReady()
        {
            if (StopIfUnwanted()) return;
            // RTSP の配信では OnVideoStart が来ないことがある。再生が始まっていればここで扱う
            if (videoPlayer != null && videoPlayer.IsPlaying) MarkPlaying();
            else SetStatus(L(S_Ready));
        }

        public override void OnVideoStart()
        {
            if (StopIfUnwanted()) return;
            MarkPlaying();
        }

        private void MarkPlaying()
        {
            _videoPlaying = true;
            _retryUrl = "";
            SetStatus(L(S_Playing));
            if (!_hasStatusApi) SetLive(IsStreamUrl(_playingUrl)); // 動画 (YouTube や自動再生) は配信ではないので LIVE を出さない
            RefreshChannels();
            RefreshIdle();
        }

        /// <summary>
        /// 映っているか。イベントのフラグに加えてプレイヤーの IsPlaying も見る
        /// (フラグだけだと OnVideoStart が来ない配信で「映っていない」と判断し、20 秒ごとに再接続して黒くなっていた)。
        /// </summary>
        private bool IsVideoActive()
        {
            return _videoPlaying || (videoPlayer != null && videoPlayer.IsPlaying);
        }

        private void StopLocal()
        {
            _retryUrl = "";
            if (_playingUrl == "") return;
            _playingUrl = "";
            _videoPlaying = false;
            StopPlayer();
        }

        // ---- 再生バー ----

        /// <summary>
        /// 位置を動かせる動画か。配信 (発行したチャンネル・RTSP / RTMP) と、長さが分からないもの (ライブなど) は動かせない。
        /// </summary>
        private bool IsSeekable()
        {
            if (videoPlayer == null || _playingUrl == "" || _hasStatusApi || IsStreamUrl(_playingUrl)) return false;
            float d = videoPlayer.GetDuration();
            return d > 1f && d < 604800f; // 1 秒〜7 日。無限大や NaN はここで外れる
        }

        /// <summary>0.5 秒ごと: 再生バーの表示 + 全員の再生位置をそろえる。</summary>
        public void _SeekTick()
        {
            SendCustomEventDelayedSeconds("_SeekTick", 0.5f);
            bool show = !_farAway && IsVideoActive() && IsSeekable();
            if (show != _seekShown)
            {
                _seekShown = show;
                if (seekBar != null) seekBar.SetActive(show);
                // 再生バーは状態の文字と同じ場所に出す
                if (statusText != null && seekBar != null) statusText.gameObject.SetActive(!show);
            }
            if (!show) return;

            float d = videoPlayer.GetDuration();
            float t = videoPlayer.GetTime();
            SyncToTimeline(t, d);

            bool dragging = Time.time - _seekUserAt < 1f;
            float shownTime = dragging ? _seekTarget * d : t;
            if (!dragging && seekSlider != null)
            {
                _seekUpdating = true;
                seekSlider.value = Mathf.Clamp01(t / d);
                _seekUpdating = false;
            }
            if (seekTimeText != null) seekTimeText.text = FormatClock(shownTime);
            if (seekDurationText != null) seekDurationText.text = FormatClock(d);
        }

        /// <summary>
        /// サーバー時刻から「いま何秒のところのはずか」を出し、2.5 秒以上ずれていたら合わせる。
        /// 後から来た人・遅れて読み込んだ人・ほかの人が位置を動かしたときも、これで同じ位置になる。
        /// </summary>
        private void SyncToTimeline(float t, float d)
        {
            double startAt = _current >= 0 && _current < _channelCount ? _slotStartAt[_current] : 0;
            if (startAt <= 0 || !_videoPlaying) return; // 新しい動画が実際に始まるまで合わせない
            if (Time.time - _lastPlayAt < 1.5f || Time.time - _lastSyncSeekAt < 3f || Time.time - _seekUserAt < 1.5f) return;
            float expected = (float)(Networking.GetServerTimeInSeconds() - startAt);
            if (expected < 0f) return;
            if (expected >= d)
            {
                if (!videoPlayer.Loop) return; // 終わっている
                expected = expected % d;
            }
            if (Mathf.Abs(t - expected) > 2.5f)
            {
                _lastSyncSeekAt = Time.time;
                videoPlayer.SetTime(expected);
            }
        }

        /// <summary>再生バーのつまみが動いた (人が動かしたときだけ、止まってから 0.35 秒後に位置を変える)。</summary>
        public void OnSeekChanged()
        {
            if (_seekUpdating || seekSlider == null) return;
            _seekUserAt = Time.time;
            _seekTarget = seekSlider.value;
            SendCustomEventDelayedSeconds("_ApplySeek", 0.35f);
        }

        public void _ApplySeek()
        {
            if (Time.time - _seekUserAt < 0.3f) return; // まだ動かしている。最後の呼び出しが反映する
            if (!IsSeekable()) return;
            float d = videoPlayer.GetDuration();
            float target = Mathf.Clamp(_seekTarget * d, 0f, Mathf.Max(0f, d - 0.5f));
            videoPlayer.SetTime(target);
            _lastSyncSeekAt = Time.time;
            // このチャンネルを見ている全員の位置をそろえる
            if (_current < 0 || _current >= _channelCount) return;
            TakeOwnership();
            _slotStartAt[_current] = Networking.GetServerTimeInSeconds() - target;
            RequestSerialization();
        }

        private string FormatClock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600, m = (total / 60) % 60, sec = total % 60;
            string mm = (m < 10 ? "0" : "") + m.ToString();
            string ss = (sec < 10 ? "0" : "") + sec.ToString();
            return h > 0 ? h.ToString() + ":" + mm + ":" + ss : mm + ":" + ss;
        }

        /// <summary>
        /// プレイヤーを止める。繰り返し (Loop) をオフにしてから止める (オンのまま止めると頭から流れ直すことがある)。
        /// </summary>
        private void StopPlayer()
        {
            if (videoPlayer == null) return;
            videoPlayer.Loop = false;
            videoPlayer.Stop();
        }

        /// <summary>
        /// 止めたのに映像が出てきたか。読み込み中に止めると、読み込みが終わった時点で再生が始まってしまうことがある。
        /// 何も再生しないはずのとき (_playingUrl が空) に始まった映像は止め直す。
        /// </summary>
        private bool StopIfUnwanted()
        {
            if (_playingUrl != "") return false;
            StopPlayer();
            _videoPlaying = false;
            RefreshIdle();
            return true;
        }

        /// <summary>
        /// 1 秒ごと: 再生の開始を拾う + 範囲 / 距離で映像を止める・戻す。
        /// 表示範囲 (viewAreas) があれば頭がその中にいるか、無ければスクリーンからの距離で決める。
        /// </summary>
        public void _Tick()
        {
            SendCustomEventDelayedSeconds("_Tick", 1f);

            if (!_videoPlaying && _playingUrl != "" && videoPlayer != null && videoPlayer.IsPlaying) MarkPlaying();
            // 取りこぼしの保険: 止めているはずなのに再生中なら止める
            if (_playingUrl == "" && videoPlayer != null && videoPlayer.IsPlaying) StopIfUnwanted();
            RefreshIdle();

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            bool far;
            if (HasViewAreas())
            {
                far = !InViewArea(local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position);
            }
            else if (cutoffDistance > 0f)
            {
                Vector3 anchor = distanceAnchor != null ? distanceAnchor.position : transform.position;
                float d = Vector3.Distance(local.GetPosition(), anchor);
                // 境目で止める / 戻すを繰り返さないよう 3m の幅を持たせる
                far = _farAway ? d > cutoffDistance - 3f : d > cutoffDistance;
            }
            else far = false;

            if (far != _farAway)
            {
                _farAway = far;
                ApplyState();
            }
        }

        private bool HasViewAreas()
        {
            if (_viewAreas == null) return false;
            for (int i = 0; i < _viewAreas.Length; i++)
                if (_viewAreas[i] != null && _viewAreas[i].enabled && _viewAreas[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        /// <summary>点がどれかの範囲の中か (回転したキューブでも正しく判定できるよう ClosestPoint を使う)。</summary>
        private bool InViewArea(Vector3 point)
        {
            for (int i = 0; i < _viewAreas.Length; i++)
            {
                Collider area = _viewAreas[i];
                if (area == null || !area.enabled || !area.gameObject.activeInHierarchy) continue;
                if ((area.ClosestPoint(point) - point).sqrMagnitude < 0.0001f) return true;
            }
            return false;
        }

        /// <summary>URL チャンネルの再試行。予約後に再生が始まった / 別の URL に変わったときは何もしない。</summary>
        public void _RetryTick()
        {
            if (_retryUrl == "" || _retryUrl != _playingUrl) return;
            if (Time.time < _retryAt - 0.5f) return;
            _retryUrl = "";
            if (IsVideoActive()) return;
            Resync();
        }

        public override void OnVideoEnd()
        {
            _videoPlaying = false;
            SetStatus(IsAutoPlayUrl(_playingUrl) ? L(S_VideoEnded) : L(S_StreamEnded));
            if (!_hasStatusApi) SetLive(false);
            RefreshChannels();
            RefreshIdle();
        }

        public override void OnVideoError(VideoError videoError)
        {
            _videoPlaying = false;
            RefreshChannels();
            RefreshIdle();
            if (_hasStatusApi && IsPool(_current))
            {
                // 発行で入れたチャンネルは状態 API が live になったら再接続する (配信前のチャンネルに繰り返しつながない)
                SetStatus(_slotLive[_current] ? L(S_ErrPool).Replace("{E}", videoError.ToString()) : L(S_WaitingStart));
                return;
            }
            if (!IsFilled(_current)) return;
            SetStatus(L(S_ErrUrl).Replace("{E}", videoError.ToString()).Replace("{S}", retrySeconds.ToString()));
            SetLive(false);
            _retryUrl = _playingUrl;
            _retryAt = Time.time + retrySeconds;
            SendCustomEventDelayedSeconds("_RetryTick", retrySeconds);
        }

        // ---- 状態の確認 (GET /api/vrc/channel/<name>) ----

        public void _Poll()
        {
            int used = 0;
            for (int i = 0; i < _channelCount; i++) if (IsPool(i)) used++;

            // 1 チャンネルあたり statusPollSeconds 秒になるよう、発行で入れたチャンネルを順番に 1 つずつ確認する
            float interval = statusPollSeconds / (used > 0 ? used : 1);
            if (interval < 1f) interval = 1f;
            SendCustomEventDelayedSeconds("_Poll", interval);

            if (used == 0 || poolStatusUrls == null) return;
            for (int k = 0; k < _channelCount; k++)
            {
                int s = (_pollCursor + k) % _channelCount;
                if (!IsPool(s)) continue;
                int poolIndex = _slotIndex[s];
                if (poolIndex >= poolStatusUrls.Length || IsEmpty(poolStatusUrls[poolIndex])) continue;
                _pollCursor = s + 1;
                VRCStringDownloader.LoadUrl(poolStatusUrls[poolIndex], (IUdonEventReceiver)this);
                return;
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            string url = result.Url != null ? result.Url.Get() : "";
            if (_claimPending && !IsEmpty(claimUrl) && url == claimUrl.Get())
            {
                HandleClaimResponse(result.Result);
                return;
            }
            HandleStatusResponse(url, result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            string url = result.Url != null ? result.Url.Get() : "";
            if (_claimPending && !IsEmpty(claimUrl) && url == claimUrl.Get())
            {
                _claimPending = false;
                SetClaimButton(true);
                SetClaimStatus(L(S_ConnectError), colorDanger);
            }
            // 状態確認の失敗は直前の状態を保つ
        }

        private void HandleClaimResponse(string body)
        {
            _claimPending = false;
            SetClaimButton(true);

            DataToken token;
            if (!VRCJson.TryDeserializeFromJson(body, out token) || token.TokenType != TokenType.DataDictionary)
            {
                SetClaimStatus(L(S_ConnectError), colorDanger);
                return;
            }
            DataDictionary d = token.DataDictionary;

            if (!GetBool(d, "ok"))
            {
                SetClaimStatus(ClaimErrorMessage(GetString(d, "error")), colorDanger);
                return;
            }

            int index = GetInt(d, "index", -1);
            int gen = GetInt(d, "gen", 0);
            string name = GetString(d, "name");
            string code = GetString(d, "code");
            float expiresIn = GetFloat(d, "expiresIn", 600f);

            // サーバーとワールドのプールが食い違っていないか
            if (!PoolIndexValid(index) || gen <= 0 || string.IsNullOrEmpty(name) || !poolUrls[index].Get().EndsWith("/" + name))
            {
                SetClaimStatus(L(S_WorldUpdate), colorDanger);
                return;
            }
            // コードは画面とリンクに出すので、英数字だけ・短いものだけを受け付ける
            if (!IsSafeCode(code))
            {
                SetClaimStatus(L(S_ClaimFailed), colorDanger);
                return;
            }
            if (expiresIn < 1f) expiresIn = 1f;
            if (expiresIn > 3600f) expiresIn = 3600f;

            int slot = _claimSlot;
            if (slot < 0 || slot >= _channelCount || IsFilled(slot)) slot = TargetOrFirstEmpty();
            if (slot < 0)
            {
                SetClaimStatus(L(S_ChannelTaken), colorDanger);
                return;
            }
            _claimSlot = slot;

            TakeOwnership();
            _slotIndex[slot] = index;
            _slotGen[slot] = gen;
            _slotLive[slot] = false;
            _channelUrls[slot] = VRCUrl.Empty;
            bool wasPlaying = _current >= 0;
            if (_current < 0) _current = slot;
            if (_syncedCurrent < 0 || _syncSwitch) _syncedCurrent = _current;
            _focus = slot;
            RequestSerialization();
            ApplyState();
            if (wasPlaying) SetStatus(_videoPlaying ? L(S_Playing) : L(S_Connecting)); // 選んだときの案内を消す

            ShowCode(code, expiresIn, slot);
        }

        private bool IsSafeCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }

        private string ClaimErrorMessage(string error)
        {
            if (error == "FULL") return L(S_Full);
            if (error == "RATE_LIMITED") return L(S_RateLimited);
            if (error == "UNAVAILABLE") return L(S_ConnectError);
            return L(S_ClaimFailed);
        }

        private void HandleStatusResponse(string url, string body)
        {
            int slot = SlotForStatusUrl(url);
            if (slot < 0) return;

            DataToken token;
            if (!VRCJson.TryDeserializeFromJson(body, out token) || token.TokenType != TokenType.DataDictionary) return;
            DataDictionary d = token.DataDictionary;
            if (!GetBool(d, "ok")) return;

            int gen = GetInt(d, "gen", -1);
            bool live = GetBool(d, "live");

            if (gen >= 0 && gen != _slotGen[slot])
            {
                // 割り当てが変わった (コード失効 / キー期限切れ) → 空きに戻す。同期はオーナーだけが行う
                if (Networking.IsOwner(gameObject))
                {
                    ClearChannel(slot);
                    RequestSerialization();
                    ApplyState();
                }
                return;
            }

            _slotLive[slot] = live;
            RefreshChannels();
            if (live && slot == _claimSlot && _code != "") ClearCode();
            if (slot == _current && _hasStatusApi)
            {
                SetLive(live);
                // 配信中なのに映っていなければ再接続する (直前の PlayURL から 10 秒は待つ)
                if (live && !IsVideoActive() && Time.time - _lastPlayAt > 10f) Resync();
            }
        }

        private int SlotForStatusUrl(string url)
        {
            if (poolStatusUrls == null || string.IsNullOrEmpty(url)) return -1;
            for (int s = 0; s < _channelCount; s++)
            {
                int poolIndex = _slotIndex[s];
                if (poolIndex < 0 || poolIndex >= poolStatusUrls.Length || IsEmpty(poolStatusUrls[poolIndex])) continue;
                if (poolStatusUrls[poolIndex].Get() == url) return s;
            }
            return -1;
        }

        // ---- 受け取りコード (本人のパネルにだけ表示。同期しない) ----

        private void ShowCode(string code, float expiresIn, int slot)
        {
            _code = code;
            _codeExpireAt = Time.time + expiresIn;
            SetCode(FormatCode(code), expiresIn);
            SetClaimStatus(L(S_Terms), colorText3); // 空きチャンネルに戻ったときの表示。発行したチャンネルでは RefreshSource が案内を出す
            ShowLink(claimPageUrl + "?code=" + code);
            if (!_codeTicking)
            {
                _codeTicking = true;
                SendCustomEventDelayedSeconds("_CodeTick", 1f);
            }
        }

        public void _CodeTick()
        {
            if (_code == "")
            {
                _codeTicking = false;
                return;
            }
            float remain = _codeExpireAt - Time.time;
            if (remain <= 0f)
            {
                ClearCode();
                SetClaimStatus(L(S_CodeExpired), colorDanger);
                return;
            }
            SetCode(FormatCode(_code), remain);
            SendCustomEventDelayedSeconds("_CodeTick", 1f);
        }

        /// <summary>コードを消してリンク欄を閉じ、発行カードを初期表示に戻す (失効、または配信が始まったとき)。</summary>
        private void ClearCode()
        {
            _code = "";
            _codeTicking = false;
            SetCode("", 0f);
            HideLink();
            SetClaimStatus(L(S_Terms), colorText3);
        }

        /// <summary>表示用のコード。サーバーの桁数に合わせる (4 桁はそのまま、6 桁なら 3 文字ずつ区切る)。</summary>
        private string FormatCode(string code)
        {
            if (code == null) return "";
            if (code.Length == 6) return code.Substring(0, 3) + " " + code.Substring(3);
            return code;
        }

        private string FormatRemain(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            if (total < 0) total = 0;
            int m = total / 60;
            int s = total % 60;
            return (m < 10 ? "0" + m.ToString() : m.ToString()) + ":" + (s < 10 ? "0" + s.ToString() : s.ToString());
        }

        // ---- リンク欄 (発行後、押した本人にだけ表示。VRChat のコピー機能で PC に持っていく) ----

        // 表示 / 非表示は RefreshSource が決める
        private void ShowLink(string text)
        {
            if (copyField != null) copyField.text = text;
            RefreshSource();
        }

        private void HideLink()
        {
            if (copyField != null) copyField.text = "";
            RefreshSource();
        }

        // ---- JSON ----

        private bool GetBool(DataDictionary d, string key)
        {
            DataToken v;
            if (!d.TryGetValue(key, out v)) return false;
            return v.TokenType == TokenType.Boolean && v.Boolean;
        }

        private string GetString(DataDictionary d, string key)
        {
            DataToken v;
            if (!d.TryGetValue(key, out v)) return "";
            return v.TokenType == TokenType.String ? v.String : "";
        }

        private int GetInt(DataDictionary d, string key, int fallback)
        {
            DataToken v;
            if (!d.TryGetValue(key, out v)) return fallback;
            if (v.TokenType == TokenType.Double) return (int)v.Double;
            if (v.TokenType == TokenType.Int) return v.Int;
            return fallback;
        }

        private float GetFloat(DataDictionary d, string key, float fallback)
        {
            DataToken v;
            if (!d.TryGetValue(key, out v)) return fallback;
            if (v.TokenType == TokenType.Double) return (float)v.Double;
            if (v.TokenType == TokenType.Int) return (float)v.Int;
            return fallback;
        }

        // ---- 表示 ----

        private void SetLive(bool live)
        {
            if (liveBadge != null) liveBadge.SetActive(live);
        }

        private void SetStreamName(string name, string sub)
        {
            if (streamNameText != null) { streamNameText.text = name; streamNameText.color = name == EmptyName ? colorText3 : colorText; }
            if (streamNameSubText != null) streamNameSubText.text = sub;
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        private void SetClaimStatus(string message, Color color)
        {
            _claimMsg = message;
            _claimColor = color;
            RefreshSource();
        }

        private void SetCode(string text, float remain)
        {
            bool has = !string.IsNullOrEmpty(text);
            if (codeText != null) codeText.text = has ? text : "";
            if (codeTimerText != null) codeTimerText.text = has ? L(S_Remain).Replace("{T}", FormatRemain(remain)) : "";
        }

        private void SetClaimButton(bool enabled)
        {
            if (claimButton != null) claimButton.interactable = enabled;
            if (claimButtonLabel != null) claimButtonLabel.text = enabled ? L(S_ClaimIdle) : L(S_ClaimBusy);
        }
    }
}
