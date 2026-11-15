Buicha Live Panel (for VRChat worlds)  version 1.0.0
====================================================

A screen and control panel that shows Buicha Live (https://live.buicha.jp) streams in a VRChat world.
It also plays video links such as YouTube.
日本語: README.txt


Installation
------------
Install it with the VRChat Creator Companion (VCC).
1. In VCC, open Settings -> Packages -> Add Repository and enter https://packages.buicha.jp/vpm.json
   (or use the "Add to VCC" button on https://packages.buicha.jp).
2. In your project's Manage Project, add "Buicha Live Player".
   It needs VRChat SDK - Worlds 3.10 or later (VCC installs it for you).
The files go into Packages/jp.buicha.live.player/ (Project window: Packages -> Buicha Live Player).


Contents
--------
BuichaLive.prefab               4-channel mode. Switch between 4 channels (panel 3.2 m x 0.69 m)
BuichaLiveStage.prefab          Stage mode. A smaller panel with a single channel (2.0 m x 0.6 m)
Scripts/                        Scripts that run the panel (UdonSharp)
Shaders/ Materials/ Textures/ Fonts/   Files used for the look


Setup
-----
1. Put one of the prefabs from Packages -> Buicha Live Player in your scene (you can use both).
   Don't edit the files inside the package; adjust the copies in your scene (updates overwrite the package).
2. You can move the screen (Screen) and the panel (Panel) anywhere inside the prefab.
3. The cubes (Area) inside ViewAreas set where the video plays. Only people whose head is inside one of them
   see the video. Move, resize, rotate or duplicate them as you like. If you delete all of them, the panel
   uses the distance from the screen instead (Panel > Cutoff Distance).
4. If needed, set "Buicha Live Settings" on the prefab root (BuichaLive / BuichaLiveStage).
   - Language: Shows the panel in Japanese or English.
   - Sync Channel Switch: When on, switching or stopping a channel changes the screen for everyone.
     When off (default), switching and stopping only affect the person who does it. Stage mode always works as if it is on.
   - Auto Play Url: A video URL that plays automatically. The first person to open the instance puts it on
     channel 1 (the stage in stage mode), and people who join later see the same video. Leave it empty to turn it off.
     It works like any other channel, so anyone can remove it with the X button in the world.
   - Auto Play Loop: Repeats the auto-play video (has no effect on live streams).
   - Idle Texture: The image shown on the screen when nothing is playing (stopped, connecting, or outside the
     viewing area). It is stretched to 16:9. If empty, the included image (Textures/BuichaLiveIdleImage.jpg) is used.
5. Upload your world.


Using it in the world
---------------------
- Select a channel, then choose "Play a URL" or "Go live with BuichaLive".
- Play a URL: Paste a video URL such as YouTube, or a rtspt://live.buicha.jp/... URL issued on dash.live.buicha.jp,
  then press "Play".
- Go live with BuichaLive: Press "Agree and get a code". Only the person who pressed it sees the code and a link.
  Copy the link with VRChat's copy feature and open it in a browser on your PC; the code is already filled in.
  (You can also open https://dash.live.buicha.jp/claim and type the code.) After you agree to the terms,
  the stream settings appear. The code expires in 10 minutes.
- The round buttons are, from left: Remove / Stop / Reconnect.
- What is on each channel, and removing a channel (X), are shared by everyone. By default, switching channels and
  stopping only change your own screen. When someone starts a channel, people who are already watching something
  stay where they are. People who aren't watching anything (for example, people who just joined) watch the channel
  someone started.
- While a video is playing, a seek bar appears at the top of the panel. Moving it changes the position for everyone
  in the instance, and people who join later start from the same position (live streams have no seek bar).


Good to know
------------
- Streams started with "Agree and get a code" on this panel are public and can be watched from outside the instance.
  To show a stream only to certain people, paste a URL issued on dash.live.buicha.jp into the panel instead.
- To watch Buicha Live streams, viewers must turn on "Allow Untrusted URLs" in VRChat's settings.
  Viewers who keep it off only see videos from sites VRChat allows (such as YouTube).
- The panel connects to the servers below and to the site of any URL someone pastes. Pasted URLs are not filtered
  by site, so everyone in the instance connects to that site (like any video player). The site you connect to can
  see your IP address.
    live.buicha.jp        Video (RTSP)
    dash.live.buicha.jp   Issuing codes and checking whether a channel is live (HTTPS)
- Anyone can operate the panel (including removing and stopping channels).
- Each stream can have up to 30 viewers connected at the same time.
- The channels used by "Agree and get a code" are shared by every world that uses this panel.
  When they are busy, you may not be able to get a code.
- Browser streaming is limited to 2 streams at a time across the whole server (OBS streaming is not limited).
- Tested on Windows (PC). Playback on Quest standalone is not guaranteed.
- Do not change the URLs in the panel's channel pool. Getting codes will stop working.
- The code page and the Buicha Live website are in Japanese.
- Buicha Live Terms of Service: https://dash.live.buicha.jp/terms


Terms of use
------------
This package (Buicha Live Panel) is distributed by Buicha.jp. You may use it for free under the terms below.
By using it, you agree to these terms. If this English text and the Japanese README.txt differ,
the Japanese version applies.

You may
- Use it in your VRChat worlds (personal or business, commercial or non-commercial, Public or Private).
- Modify it to fit your world (position, size, look, scripts).
- Publish worlds that include this panel. You don't need to contact us or give credit.

You may not
- Redistribute or sell the package itself (including modified versions). To recommend it, link to the distribution page.
- Change the channel pool URLs or the code URL, or use them for anything other than this panel.
- Modify it in ways that add load to the Buicha Live servers (for example, checking channel status more often).
- Use it in ways that break the Buicha Live Terms of Service (https://dash.live.buicha.jp/terms).
- Make your world look like an official or endorsed Buicha.jp world.
  You may use the logo and the "Buicha Live" name only as shown on this panel, unchanged.

Disclaimer
- Provided as is, with no warranty. The distributor is not responsible for any damage caused by using this package.
- The Buicha Live service may change or stop without notice. The panel may stop working when that happens.
- The person who streams is responsible for the content of their stream.
- These terms may change. Each version is covered by the terms in that version's README.

Copyright
- (c) 2026 Buicha.jp (except the fonts)
- The fonts Zen Kaku Gothic New and JetBrains Mono are not covered by these terms. They are licensed under the
  SIL Open Font License 1.1 (OFL.txt in the Fonts folder). To keep the size small, both fonts contain only the
  characters the panel uses. If you change the panel text, missing characters are shown in another font.
