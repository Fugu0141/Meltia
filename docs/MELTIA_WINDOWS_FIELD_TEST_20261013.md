# Windows実機フィードバックに基づく境界修正・評価計画（2026-10-13）

## 1. 位置付け

PR #18/#20統合後main（`99075ee8b5c87b86f79d9cdac645fb1caad5027a`）をビルドしたWindows Keyboard Previewについて、手入力の報告を受けた**バグ修正**。次期版の未知英語モデルや学習方式を無検証で既定ONにはしない。正式TSF/IMEの入力E2Eは別ゲート。

## 2. 報告された事実と区別する仮説

| キー列 / 観測 | 実機結果（実験境界OFF） | 現時点の原因仮説・扱い |
| --- | --- | --- |
| `commitha` | 「こっみてゃ」 | English `commit` の最後の `t` と助詞 `ha` が `tha` のローマ字かな単位に結合され、英語境界が消える可能性。単位分離を修正し回帰追加 |
| `Adoさんの`（入力例 `Adosannno`） | `Adosannno` のまま | アーティスト名 `Ado` が同梱固有名詞辞書にない。人手で用途が明確な固有名詞だけ候補辞書に追加 |
| `API` | テスト報告はNG（具体的表示不明） | 自動テストでRaw/表示を監視。異なる再現環境での確定結果はまだ不明とする |
| `meltiaga` | 入力途中に一時的な表示不良 | 各打鍵のRaw・区間完全被覆を検査。ただしGUIの描画フリッカーやキーイベント順序は自動セルフテストでは再現できない |
| `seeyouagain`, `IME`, `meltiawotamesu`, `Meltiawotamesu`, `kyouhagoogledekensaku`, URL | 報告ではOK | 修正で壊さない回帰対象。文字列が曖昧なら文脈/設定の異なる解釈を独立評価する |

## 3. 最小変更・受け入れ条件

1. 英語の最後の `t` に `ha` が続く場合、既知英単語として独立していた `t` を `tha`（てゃ）に再結合させない。無関係な日本語 `tha`、未知英字、単語内の綴りを無条件に英語固定しない。
2. 独立な原著データの学習ではなく、既存の `propernouns.txt` に人手の正解例として `Ado` を1語追加。**大量の機械生成例は正答学習データではない**。後続の固有名詞追加は独立したコーパスと負例で検証する。
3. `CapitalizedWordEnd` の全後続文字列の走査を、入力冒頭が大文字か小文字→大文字（例: iPhone）の時だけ行うようにする。大文字固有名詞の既存規則、UTF-16、手動補正、Raw保存を維持。
4. Preview self-testで `commitha`、`Adosannno`、`API`、`meltiaga` を追加し、OS横断CoreテストとWindows Preview publish + smokeを走らせる。**自動テストの成功だけでは実入力E2E合格にしない**。

## 4. IncrementalBoundaryLabから適用した設計判断

参照: [Phonetic-first](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/phonetic-first.md)、[OOV研究（2026-10-10）](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/research/2026-10-10-oov-character-candidates.md)、[CI](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/ci-validation.md)。

- 音の読みを作る**Stage 1**と英語/日本語の区間根拠**Stage 2**を混ぜない。今回は先に単位境界を保証し、その後既存英語辞書/固有名詞の根拠で区間を選ぶ。
- 研究の54未知英語語テストでは文字モデル候補が固定語尾候補より多く発見されたが、**候補recallはWindows IMEの正答率ではない**。日本語だけの負例でもseedによって誤候補が生じるため、学習器を直接確定処理に接続しない。
- `tokyo`/`ime` のように意図不明な同字列は原文だけでは決定できない。大文字、利用者の明示修正、既存固有名詞、前後文脈を追加の証拠にする。誤確定は増やさない。
- 未知英語の学習データについては著作権・ライセンス/holdout/ラベル品質の監査が必要。今回の変更は研究データのモデル重みや外部辞書を転載しない。

## 5. 性能測定・比較条件

今回の早期終了は、普通の小文字ローマ字を入力する度に `CapitalizedWordEnd` が全残り区間を読み直す仕事を省くもの。**速くなる量はまだWindows実機で計測していない**。旧mainと修正ブランチを同じPC/SDK/Release構成で順番を入れ替えて比較すること。

```powershell
dotnet run --project tools/performance/Benchmark.csproj -c Release -- 15 32,128,256
```

このベンチマークは `tools/performance/README.md` に記載されたセッションのp50/p95/max、割り当てbytesと入力全体の経過時間の検証用。ウォームアップ・ランナー負荷・辞書版・文字数・SHAを揃え、両方の出力ハッシュも一致させる。*手動の1打鍵遅延・描画/UI更新・キー再送の遅延はここでは測定できない*。

## 6. Windows手動再検証

1. **最新版ブランチからローカルで再ビルド**し、実験境界OFFでメモ帳に `commit`, `commitha`, `API`, `Adosannno`, `meltiaga`, `meltiawotamesu` を入力。途中表示とEnter確定後の結果を別々に記録。
2. 実験境界ONで同じ入力と `kyouhacommitsimasita` / `githubdeissue` / `kyouhagoogledekensaku` を比較。これらはIncrementalBoundaryLabの研究例に含まれるが、Windowsでの正答を保証しない。
3. Backspace・前後の入力欄フォーカス移動、連続入力、長文の末尾編集を同条件で比較。パスワード欄・個人情報・重要書類は使用しない。
4. 精度は原文保持率/区間完全一致/日本語・英語誤判定/誤確定、性能は短文32字・中間128字・長文256字の平均/p95/p99/allocを**別々に**報告する。

## 7. 未実施・合格の宣言範囲

この文書作成時点では、修正後のWindows GUI実キーE2E、パフォーマンスのbefore/after実測、ラベル付き自然文holdoutの比較は未実施。追加セルフテスト/CIが通ってもリリース不可という従来のゲートを維持する。
