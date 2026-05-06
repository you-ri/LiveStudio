# Lilium LiveStudio

VTuber 配信アプリ開発のためのベースパッケージです。アバター表示・カメラ・ライティング・シーン管理・RemoteControl 基盤・ローカライズなど、特定のモーションキャプチャシステムに依存しない汎用機能を提供します。

- **Camera / Lighting / Scene / Screen** — 配信画面構築の基本パーツ。
- **RemoteControl 基盤** — `jp.lilium.remotecontrol` 上に構築された Camera / Light / Manipulator API ハンドラ。
- **InputAction / KeyBinding** — Unity Input System を活用した汎用入力管理。
- **Build パイプライン** — Studio アプリのコマンドラインビルド。
- **Localization** — `LocalizationSystem` への汎用キー登録。

特定のモーションキャプチャシステム (例: VirgoMotion) と組み合わせることで、配信用のスタジオアプリが構築できます。

---

## Requirements

- Unity **2022.3** 以降 (Unity 6.x で動作確認)
- `jp.lilium.remotecontrol`
- `jp.lilium.nativegamepad`
- `com.unity.cinemachine`

---

## Namespace

ランタイム: `Lilium.LiveStudio`
エディタ: `Lilium.LiveStudio.Editor`

---

## License

MIT — see [LICENSE](LICENSE).
