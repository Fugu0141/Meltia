# Meltia v1.0-dev 品質強化ゲート（2026-10-12）

> 状態：**研究用 / Draft / リリース不可**。
> [品質強化PR #10](https://github.com/Fugu0141/Meltia/pull/10) は
> [v1.0-dev Core PR #9](https://github.com/Fugu0141/Meltia/pull/9) をbaseとする。
> 版番号は引き続き `1.0.0-dev`。**IME・アプリ・Windows TSFには接続しない。**

## 今回実装した検証

### 1. 固定56件の逆境（adversarial）合成コーパス

`tests/Meltia.BoundaryEvaluation/AdversarialCorpus.cs`：
- A01 12件：日本語のみ（日本語ローマ字の負例）
- A02 12件：未知の英語が1区間
- A03 8件：英語2〜3区間
- A04 12件：**同一の6つの文字列に日英両方の対立する正解ラベル**
  （`tokyo`、`ime`、`sake`、`no`、`take`、`kaki`）
- A05 8件：URL、メール、略語、絵文字、句読点
- A06 4件：`si`/`shi`、`tyo`/`cho`などローマ字打鍵法の差

ゴールドラベルは人手によって**作成した意図を指定する断片**に基づき生成。
第三者による自然文の盲検収集・ラベル検証ではない。
**A04は同一の文字列に相反するゴールドがあるので、外部証拠がなければ
原理的に両方の正解を同時に満たせない。** この分類のTop1率を他分類と
無批判に混ぜて汎化精度を語らない。

`tests/Meltia.Core.Tests/Program.cs` では同じ6語それぞれについて
明示的な英語ロック、明示的な日本語ロック、語彙証拠だけの場合の
意図未確定性、原文保持、非自動確定を検査。
**評価CLIでもA04の6つの対立ラベル付きペアを個別に検証**する。
語彙証拠のみなら意図不明、明示英語ロックなら英語、明示日本語ロックなら日本語となることをチェックし、検証JSONの`AmbiguityPairs`に結果を出力する。
文字列から意図を推測できない問題を、「自然にどちらも正解」という誤った精度評価に混ぜない。

UTF-16絵文字の後にあるURLの必須保護も検査。

実行：

```sh
dotnet run --project tests/Meltia.BoundaryEvaluation/Meltia.BoundaryEvaluation.csproj -c Release -- --output artifacts/meltia-boundary-adversarial.json --suite adversarial
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
```

### 2. Coreベンチマーク

`tools/Meltia.BoundaryBenchmark`：
- v0.9の境界探索と、v1.0の不確実性レビュー付き境界探索を比較。
- 日本語のみ、英語1区間・3区間、URL、絵文字、96文字の英語風負例、
  最大1024 UTF-16単位の日本語入力。
- 同一プロセスでウォームアップ後、各入力を8〜28回独立に実行。
  **p50・p95・p99** の実行時間、呼び出しごとの
  `GC.GetAllocatedBytesForCurrentThread()` をJSONに記録。
- 各入力について**v0.9とv1.0の候補・英語区間・得点が同一**かを事前に検証し、
  p50・p95の経過時間比、p50割当量比もJSONに出力する。

- 一貫して`AutoCommitAllowed=false`、候補128・経路8制約、
  原文保持、レビュー有無を検査する。

```sh
dotnet run --project tools/Meltia.BoundaryBenchmark/Meltia.BoundaryBenchmark.csproj -c Release -- --output artifacts/meltia-boundary-performance.json
```

注意：CIランナーの処理時間は負荷・環境で揺れる。
最小8回でのp99は最大近くの観測であり統計的に頑健ではない。
**CIのp95/p99をWindows上の逐次入力遅延として扱ってはいけない。**
本機能の実IME統合は引き続き行わない。

### 3. 以前からの検証と合わせた運用

従来の47件・530件・290件＝**867件**についてv0.9とv1.0の
候補とTop-K経路の一致、辞書条件とMeltypeシャドー条件、
同一コーパス内でのTop1/Top-K非退行を確認するCI契約は維持。
新しい56件を追加した結果、実験用合成評価は**合計923件**。
ただしコーパスごとに目的が異なり、同一母集団として統合した
「製品精度」ではない。

**現時点の正式版ゲート：**
- [ ] v1.0-devの全CI（Core、Shadow、Windows build、56件、867件、性能）に成功
- [ ] GitHub Actionsがステップ開始前に失敗する障害を解消
- [ ] 人手ラベル付きの独立自然文holdoutを凍結し、採用基準を事前決定
- [ ] 日本語ローマ字負例・英語複数区間・曖昧語について別々に精度目標を達成
- [ ] 実機で逐次打鍵・変換・編集・長時間稼働のp95/p99とメモリを測定
- [ ] GPLと依存物の配布条件、Meltypeとは別の名前・IME識別子・更新先を検証

一つでも未完了なら **`1.0.0-dev`（未リリース）**のまま維持する。

## CIブロッカーの原因究明

- [Issue #11](https://github.com/Fugu0141/Meltia/issues/11)
- [品質強化PR #10](https://github.com/Fugu0141/Meltia/pull/10) の再実行でも開始前失敗が継続。単一ジョブを再実行しても改善せず、ジョブログなし。
- [全体buildの例](https://github.com/Fugu0141/Meltia/actions/runs/38223660134)
- [新しいShadow失敗の例](https://github.com/Fugu0141/Meltia/actions/runs/38226762557)

2026-10-12の該当runではGitHub Actions jobsに実行ステップがなく、
ランナー名も記録されず、ログ取得が `404 BlobNotFound`。
単一ジョブを再実行してもステップ0のまま終了した。
現象はテスト例外・C#コンパイルエラーと**別扱い**とする。
詳細なCheck annotationsはAPIから本文を回収できていないため、
**Billing・権限・GitHub側ランナー問題のいずれかをまだ確定できていない**。
ユーザーにGitHub UIで該当jobのannotationと
Settings → Billing & plans / Settings → Actionsの確認を依頼する。

この障害のため、**新規56件および性能測定のCI成功・測定値は未確認**。
ソースへの追加と、実際の.NET環境でビルド・テストに成功したことは区別する。

## 4. 無料枠を使い切った状態での検証・CI節約（2026-10-12）

GitHub Actionsの**月間無料枠100%到達**が報告されたため、
プライベートリポジトリのGitHub-hosted Runnerがジョブ初期化前に
失敗する可能性が高い。これはユーザーのGitHub画面に基づく
原因推定で、GitHub Check annotationsの本文による確定診断ではない。

### ローカル実行（Windows/macOS/Linux）

.NET SDK **10**、Python **3.10以降**、依存パッケージの復元用ネットワーク
（初回のみ必要な場合あり）がある環境で、
**リポジトリのルート**から次を実行：

```sh
python tools/run-meltia-quality.py --tier smoke
python tools/run-meltia-quality.py --tier full
```

WindowsでPython Launcherを使う場合：

```powershell
py -3 tools/run-meltia-quality.py --tier full
```

- `smoke`: Core契約、Bridge契約、47件、56件を実行。
- `full`: 上記に530件、290件、v0.9対v1.0オフライン性能比較を追加。
- 成否・コマンドのログ・各ケースのJSON・性能結果を
  `artifacts/meltia-quality/` に出力し、失敗時は**非ゼロ終了**。
- `.NET` SDKが見つからない場合も`summary.json`で
  `blocked-missing-dotnet`とし、**成功したと偽らない**。
- `full`でもWindows TSFの実機入力・macOS/LinuxネイティブIME・
  インストーラーの検証には**置き換えられない**。

### GitHub Actionsの節約ルール

1. Meltia専用CoreとShadow、元Meltypeの`build`、
   `pr-checks`、`notices`は、
   **Draft PRをスキップ**する設定に変更。
   Ready for reviewに移行した際には自動CIを再開。
2. コード`push`では`main`に限定し、
   作業用ブランチで`push`と`pull_request`の重複実行を防止。
   原則として開発中はローカル`smoke`を使用。
3. 従来の`build.yml`はMeltypeのコード・配布物と関係する
   変更に限定。Meltia専用Coreやベンチマークだけの変更では
   Windows・macOSの全ビルドを自動起動しない。
4. 新しいMeltia品質ワークフローのPRは`smoke`。
   `workflow_dispatch`（手動起動）は`smoke`または`full`を指定。
   ただし**workflow_dispatchは該当ワークフローが既定ブランチに存在することが前提**。
   PRのDraft段階ではローカルスクリプトを使う。
5. **リリース前に必ず**`full`（923件と性能）を成功させ、
   元Meltype`build`の全OS・署名/インストーラーも別途検証。
   Actionsの失敗やskipped状態を成功とみなさない。
6. `pull_request_target`の`notices`は、
   **既定ブランチ上のワークフローで動作する**ため、
   Draftスキップ設定はPR内だけでは即座に有効にならず、
   既定ブランチに適用されて初めて機能する。

自動CIを減らすことは品質ゲートを弱めることではない。
Draftと開発時には自前のテスト結果を収集し、レビュー・正式採用前に
必要なゲートを全て実行して証拠を残す。

### 制約・検証状況

上記設定・スクリプトは**ソース実装**。GitHub-hostedランナーの
利用枠が使い切られている間は、CI成功の実測とみなさない。
ローカル開発環境に.NET SDK 10がない場合、`run-meltia-quality.py`
はブロック理由を出力する。CIと正式配布の判定は保留する。
