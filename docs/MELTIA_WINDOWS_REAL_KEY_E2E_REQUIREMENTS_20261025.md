# Windows実打鍵 E2E テスト基盤：要件・設計・実行手順（2026-10-25）

対象: Draft PR #102。現段階では**Windows実機の結果は未取得**。既存の Core Input Matrix（合成テスト）とは別の検証ゲート。

## 目的と件数

- 1ケース＝1単語または1文。JSONLに1ケース1行を記録。
- デフォルト10,000件（日本語3,334件、英語3,333件、日英混合3,333件）。任意JSONLと最大100,000件に対応。
- Win32 SendInputによる仮想キーボード → 別プロセスのWindows Keyboard Previewの実フック → CompositionService → WinForms入力欄への確定、という実経路を検査する。正式TSF登録のテストではない。
- 実際の確定テキスト、未確定表示、UTF-16区間start/end、日英区別、原文の完全被覆、ケース時間、SendInput API時間、キー送信から変換表示までの観測可能な遅延をJSONLへ出力。

## 初期化・汚染防止

**利用中の辞書や履歴は削除しない。** 毎runでGUID付きの新しいテスト専用フォルダーを作成し、子プロセスの MELTIA_DATA_DIR と MELTYPE_DATA_DIR を固定する。configだけ書き込み、ユーザー学習モデル、言語記憶、変換履歴、フレーズ履歴、ユーザー辞書は空の状態から開始する。学習、自動補正、ライブ漢字変換、予測、翻訳候補、自動更新、入力本文の通常ログ出力を無効化し、変換エンジンとその他の設定を固定する。子プロセスの学習メモリ・未確定入力・文脈を**各ケース前にリセットし、ACK待ち**してから送信する。

**完全なOS初期化ではない**：固定の同梱辞書、Windows日本語IMEの外部学習、システム辞書、別アプリのプロファイルは操作しない。必要なら隔離Windows VMの同一スナップショットを各run前に復元する。OSユーザーデータ/レジストリの一括消去は行わない。比較結果を「全学習状態完全初期化」と偽らない。

既存のMeltypeやMeltiaが起動中で二重フックが成立しなければ新しい子プロセスは起動を拒否し、ハーネスはタイムアウト/失敗として記録する。入力ターゲットのフォーカスを失ったら直ちに送信停止。

## 入力JSONLフォーマット

各行は例えば次のように記述する：

~~~json
{"id":"mixed-00001","category":"mixed","raw":"commitha","expected":"commitは","spans":[{"start":0,"end":6,"language":"en","raw":"commit","text":"commit"},{"start":6,"end":8,"language":"ja","raw":"ha","text":"は"}]}
~~~

- id: 一意な文字列。category: ja/en/mixedまたは英数字・ハイフン・アンダースコアからなる任意の追加カテゴリ。
- raw: ASCII印字可能キー1～512字、制御文字やキーボードショートカットは禁止。
- expected: 任意の観測用期待結果。nullなら「未ラベル」で正解率分母に含めない。
- spans: 任意、raw上の半開区間 [start,end)、language=ja/en。**同言語内の単語境界も記録するが、言語判定の比較には各UTF-16位置に展開したラベルを使う。**
- 合成された期待値は厳密な自然言語holdoutや漢字変換の正解保証とは区別する。

## Windows実行方法

Windows 11の対話デスクトップを使用。先に他のMeltia/Meltypeを終了させ、実行中はマウス・キーボードを使わない。管理者権限アプリ、ロック画面、ヘッドレスCIへのSendInputは対象外。

~~~powershell
# リポジトリのルートから。Python 3、.NET SDK 10 が必要。
py -3 tests/test_meltia_e2e_corpus.py
py -3 tools/generate_meltia_e2e_corpus.py --count 60 --seed 20261025 --output artifacts/meltia-e2e/smoke.jsonl
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1 -FrameworkDependent

$exe = (Resolve-Path ".\artifacts\meltia-windows-preview-win-x64-framework-dependent\Meltia.exe").Path
$corpus = (Resolve-Path ".\artifacts\meltia-e2e\smoke.jsonl").Path
$reports = Join-Path (Get-Location) "artifacts\meltia-e2e\reports"
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList @("--meltia-e2e", ('"' + $corpus + '"'), ('"' + $reports + '"')) -Wait -PassThru
Write-Host "Exit code: $($p.ExitCode)"
~~~

60件のスモーク後に、合計1万件を実行する：

~~~powershell
py -3 tools/generate_meltia_e2e_corpus.py --count 10000 --seed 20261025 --output artifacts/meltia-e2e/input-10000.jsonl
$corpus = (Resolve-Path ".\artifacts\meltia-e2e\input-10000.jsonl").Path
$p = Start-Process -FilePath $exe -ArgumentList @("--meltia-e2e", ('"' + $corpus + '"'), ('"' + $reports + '"'), "12", "140") -Wait -PassThru
Write-Host "Exit code: $($p.ExitCode)"
~~~

実験的境界判定をONにする場合は同じPowerShellのプロセス環境変数 MELTIA_BOUNDARY_PREVIEW=1 を設定してから起動する。OFF/ONを同一コーパス/seed/OS/キーレイアウトで別々の新規プロファイルから測定し、SHA256と観測時間を比較する。

## 出力ファイルと判定

毎runの新規「run-GUID」ディレクトリに出力する。

| ファイル | 説明 |
| --- | --- |
| cases.jsonl | 1件1行。入力・期待・確定された実際のホスト文字列・期待/実境界・境界比較・ケース経過ms・送信API ms・キー単位の観測可能なview遅延p50/p95/p99・欠測数 |
| composition.jsonl | 子Previewが本当に表示/確定した値、raw、spans、タイムスタンプ、qpc、ケース識別子 |
| summary.json | OS/.NET・レイアウト・実験フラグ・プロファイル状態・入力コーパス/EXEのSHA256・カテゴリ別件数・p50/p95/p99・失敗・未完了 |

**計測上の区別**：Win32 API時間とホストTextBoxへの確定遅延は別物。view遅延は「入力イベント→プレビューのview更新」であり、画面へのpaint完了時間ではない。観測不能な区間を0ms扱いしない。ケース時間には入力遅延と待機時間が含まれる。ケース途中失敗・二重フック・タイムアウトを成功とみなさない。

## 新しいテスト項目の追加

既存のJSONLに行を追加するか、tools/generate_meltia_e2e_corpus.py の語彙リスト・分類別生成関数を追加する。カテゴリ名とspansの検証はハーネス側が行う。既存コーパスを変更した場合はseed/生成器版/出力SHA256を保存すること。

## 受け入れと現時点の未実施

1. Pythonコーパスの固定seed、件数・重複・構文・境界のオフライン検査。
2. Windows CIでPreviewビルド成功。
3. ローカルWindows実機で60件スモーク（フォーカス保持、フック実通過、trace・確定テキスト一致、ケースのリセット）。
4. 続いて10,000件実測、カテゴリ別正答/欠測・実時間・境界誤りを記録し、修正前後比較。
5. メモ帳・ブラウザ・正式TSFの異種ホストE2E、外部IMEの学習完全初期化は**将来の別ゲート**。

**現在はコードを作成した段階であり、3/4の実機合格を主張しない。** 生成合成語句は独立ブラインド正解コーパスではない。入力・出力はテスト専用の無害な文字列に限定し、個人情報を混ぜない。
