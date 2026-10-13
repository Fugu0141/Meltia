# Meltia Input Matrix — 10カテゴリの組合せ・逐次入力評価

> **独立した合成試験。** 自然文の正答率を保証せず、Windowsフック、TSF、フォーカス遷移、漢字変換確定のE2Eではありません。
> 既存の固定1,067ケースの品質ゲートを置き換えたり削除したりしません。

## 目的

単一の「日本語＋英語」だけを繰り返すのではなく、**語順・打鍵・言語の意図・短語・長文・UTF-16の境界**を交差させる。
複数のモデルを毎ケース全実行する既存の精密比較とは分け、実験用の最新版境界エンジンに対して安全性を大規模検査する。

| 分類 | 代表的な構成 |
|---|---|
| japanese-only | ローマ字による日本語だけを連結 |
| english-only | 英語複数語、大小文字 |
| japanese-english-japanese | 日本語→英語→日本語 |
| english-japanese-english | 英語→助詞→英語 |
| three-english-islands | 英語が3箇所ある混在 |
| romaji-variants | shi/si、chi/ti、cha/tyaなどの打ち方の差 |
| ambiguous-intent-pairs | **同一の入力文字列**に相反する言語意図を付けたペア |
| urls-emails-and-unicode | URL、メール、半角区切り、絵文字 |
| short-technical-terms | GitHub、Python、Linux、Docker、JSON、GPUなど |
| long-window-and-boundaries | 日本語ローマ字の長い前後文、離れた英語島 |

出力は固定seedを利用して再現できる。生成コーパスはラベルの意図をプログラムで与えるため、**独立した人手のブラインド評価ではない**。語彙プールの変化やseedの追加は新しい評価セットとして扱う。辞書あり比較では全ケースに同じ語彙プールを渡し、個々のケースの正解ラベルから語彙を生成しない。ただし既知語であり、独立holdoutではない。各分類のユニークなraw文字列数も計測し、件数の水増しを把握できるようにする。

## 何を合否判定するか

**厳格な失敗条件**：日本語のみの負例の誤英語化、URL/メール保護区間の誤処理、原文の改変、自動確定許可、候補数・経路数の上限超え、不正なUTF-16境界、ユーザーの明示的な英語・日本語ロックや保護URL/メールの無視、同じ入力と証拠での経路非決定性、CompositionTextの逐次打鍵後のrawや区間の取りこぼし。

**診断指標（現段階では失敗条件にしない）**：証拠なし・固定共通英語語彙付きそれぞれのTop1・Top-K正答率、候補切り詰め件数、分類別のunique raw数、Meltype.Coreの最終区間判定率・逐次打鍵確認回数、経過時間。曖昧語には同じ文字列で二つの対立する正解を含めるため、単純な総合Top1を製品の精度として宣伝してはいけない。

逐次打鍵は *Meltype.Core* の CompositionText.Append() と CompositionDetector.Segment() を段階的に使う。文字列全体だけでなく途中の複数のプレフィックスでも入力原文と区間の一致を確認する。Windowsの物理キー・IME/フックの再現ではない。

本マトリクスのTop1・Top-K評価では、URL/メールとして保護される英字区間も正解spanに含める。誤判定例の入力・期待span・予測spanを限定件数だけJSONに保存する。

## Actionsで実行する

| tier | 件数 | トリガー |
|---|---:|---|
| smoke | 800件（80×10分類） | ローカル・手動の高速確認 |
| full | 10,000件（1,000×10分類） | 対象コードのPR/push、毎週日曜03:35 UTC、手動 |
| stress | 100,000件（10,000×10分類） | 手動のみ・高負荷 |

- .github/workflows/meltia-input-matrix.yml が実行結果JSONとログをArtifactとして14日保存する。
- stressは処理量が多く、90分のtimeoutに達すれば不合格になる可能性がある。利用枠・実行時間を確認しながら手動実行する。
- scheduled workflowはGitHubのデフォルトブランチに存在するときだけ有効。#18への段階的な取り込み中は、PRのfullまたはローカルコマンドで検証する。
- PRで強制するのは安全性契約。正式リリース前の定量的な正答率条件には人手で正解を確認したholdoutデータと、事前に決めたカテゴリ別閾値が必要。

ローカルで実行（.NET SDK 10）：

~~~sh
dotnet run --project tests/Meltia.InputMatrix/Meltia.InputMatrix.csproj -c Release -- --tier smoke --seed 20261013 --output artifacts/meltia-input-matrix/report.json
dotnet run --project tests/Meltia.InputMatrix/Meltia.InputMatrix.csproj -c Release -- --tier full --seed 20261013 --output artifacts/meltia-input-matrix/report-full.json
~~~

結果の Passed は明示ロック・原文保持等の**不変条件**が通った意味。ObservedTop1 / ObservedTopK は別の診断値であり、Passed=true だけでは入力精度の高さを証明しません。
