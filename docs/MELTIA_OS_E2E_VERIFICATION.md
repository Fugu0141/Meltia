# Meltia OS別の実入力E2E確認（PRマージ前チェック）

## 検証対象を混同しない

- **Linux CI**: GitHub Actions `linux.yml` が本物のIBusデーモン、Mozcヘルパー、NativeAOTライブラリ、fcitx5テストフロントエンドを用いて検証する。ネイティブ境界テストとともに全ジョブの結論を確認する。
- **Windows Preview CI**: `Meltia.exe` をビルドし、GUIにフックしないself-testとライセンス監査をする。**実際のアプリ入力欄・フォーカス・TSF動作の検証ではない**。
- **Windows実機E2E**: 実入力の合否はテスト担当者がデスクトップで検証する。Previewの研究用hookは正式TSF実装とみなさない。

## Linux CI（自動）

Ready for review状態で `src/Meltype.Core/**`、`linux/**`、`native/mozc/**` 等を変更したPRでは `.github/workflows/linux.yml` を実行する。Draftはホストランナー費用を節約するためスキップ。対象変更がなくても必要なら `workflow_dispatch` で手動実行する。

成功と判定するには、同じPR HEADの `linux` ワークフローで以下のステップがすべて成功すること：

1. NativeAOT + Mozcのビルド、fcitx5アドオンのテスト。
2. `tools/test-native-boundaries.py` の検証。
3. `linux/tests/test_integration.py` の実IBus/Mozc統合テスト。
4. `xvfb-run -a dbus-run-session -- python3 linux/tests/test_daemon.py` のプライベートIBusデーモンテスト。
5. ZIP作成。CIの `skipped`、`cancelled`、古いHEADの `success` は代替証拠としない。

タグ公開は **PRテストとは独立した権限付きreleaseジョブ**でのみ実行する。

## Windows実機での手動受け入れ検証（自動化未実装）

Windows 11のテスト環境で、Meltia Previewを起動して版/commit SHAと使用した入力方式（Keyboard Previewか、別途検証対象の正式TSF）を記録する。メモ帳とブラウザの入力欄など、少なくとも2種類のホストアプリで試す。テスト中はログにパスワード・メールアドレス・個人情報を記録しない。

| 入力 / 操作 | 期待する安全性・観測内容 |
| --- | --- |
| `hello`、`seeyouagain`、`commit` | 入力キーの欠落・二重挿入がない。意図しない日本語化は具体的に記録 |
| `kyouha`、`meltiawotamesu` | 原文と確定結果、未確定表示を別々に記録。境界誤認識を報告 |
| `IME`、`API`、`OpenAI` | 英大文字の欠落や先頭母音の誤かな化がない |
| `kyouhaIMEwotsukau`、`kyouhagoogledekensaku` | 日英境界の分割、候補と最終確定の内容を記録 |
| `https://example.com`、`alice@example.com` | 保護区間が勝手に変更・消去されない |
| Backspace連打、選択削除、カーソル移動 | 他の文字を誤消去しない。未確定文字とホスト文字列の整合性を確認 |
| Enter、Space、Tab、Esc、変換候補選択 | キーの消費、候補の状態、確定後の文字列を照合。キーを二重処理しない |
| フォーカス移動、IME切替、高速連続入力 | 未確定文字の消失、重複確定、他欄への誤入力がない |
| 設定変更・再起動・アンインストール | 他IMEやシステム入力への影響がない |

上記は **試験手順** であり、未実施の行を「成功」と記録しない。曖昧なローマ字は自動推定だけで期待する言語を一意に定義できないため、判定精度の正解コーパスとは区別する。

## 証跡・結果

日付、OS/アプリ、入力方式、PR HEAD SHA、入力キー列、期待した動作、実際の未確定表示、最終ホスト文書、操作（Backspace/確定等）、PASS/FAIL、問題の再現手順を記録する。機密情報はマスクする。

- Linux自動E2E: 対象SHAとワークフローのrun URLをPRに貼る。
- Windows実機E2E: 人が実測したときに限りチェックを完了にする。
- どちらも未完なら正式IMEの出荷・互換性を保証したと表現しない。
