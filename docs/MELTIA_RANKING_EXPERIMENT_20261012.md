# Meltia v0.5 — 経路順位・日本語誤切断のオフライン実験（2026-10-12）

## 結論

Meltia の境界診断モジュールに **明示的にオプトインする2つの研究用オプション**を追加した。
**本番IMEの既定挙動は変えていない。** 使用していない時は従来と同じ得点・候補・経路を返す。

1. `ExperimentalFragmentationPenalty`: 既定の英語区間1個につき0.3点の負担を、実験時だけ4.5点にする。2文字・3文字の辞書語を連鎖させて日本語音節を切る失敗への最小構成長（MDL）型の対策。
2. `ExperimentalStrongHostAdvisory`: Meltypeが提示した英語区間のうち、ローマ字として成立しないASCII語、または既知の技術略語だけを相対的に優先する。基本は仮説であり、強制保護・自動確定にはしない。小文字で始まり途中に大文字が混ざる長い全体判定、ローマ字として成立する短い語は強制しない。

`KURU` のような短い大文字日本語にも誤って強い英字点を与えないため、**大文字なら何でも英語**という処理は撤回した。明示的英字/日本語指定が引き続き最優先。辞書で意図が決められない`tokyo` /`ime`は本質的に曖昧で、入力者意図なしの完全判別は保証できない。

### オフライン初回比較

[GitHub Actions 3e2333c の実行](https://github.com/Fugu0141/Meltia/actions/runs/38182530776)
で観測した値。これは技術略語の安全なホワイトリスト制限を加える**前**のコードなので、最終コミットの値とは区別する。両セットとも **合成ゴールド**であり、人手自然文の独立holdoutではない。

| 方式 | 47件 Top1完全一致 | 47件 言語切替F1 | 530件 Top1完全一致 | 530件 言語切替F1 |
| --- | ---: | ---: | ---: | ---: |
| Meltype（オフライン） | 26/47 | 0.5979 | 271/530 | 0.6161 |
| Meltiaシャドー既定 | 12/47 | 0.3438 | 177/530 | 0.1190 |
| シャドー・ペナルティのみ | 12/47 | 0.3438 | 177/530 | 0.1190 |
| シャドー・Host強根拠のみ | **29/47** | **0.7253** | **269/530** | 0.5808 |
| シャドー・両オプション | **29/47** | **0.7253** | **269/530** | 0.5808 |
| Meltia単独・辞書一致あり・既定 | 16/47 | 0.3896 | 168/530 | 0.2955 |
| Meltia単独・辞書一致あり・両オプション | **25/47** | **0.6939** | **251/530** | **0.6011** |

**解釈の注意：**

- シャドーでの改善は、アブレーション上、**Meltypeがもともと予測した英字区間をより上位に選ぶこと**が主因である。これはMeltia単独の新しい英語検出性能が29/47や269/530へ向上した証拠ではない。
- 辞書あり・Meltype候補なしでの比較では、余計な英語区間を増やすペナルティが効いた。47件 **16→25**、530件 **168→251**。こちらはMeltypeの分割結果には依存しないが、**Meltype同梱辞書**を利用している。
- 英語辞書共有では、原文に出現した語だけを渡している。評価ゴールドから推定語を注入していない。語彙量・スコアの設計とMeltype側の分析エンジンはなお異なるため、同一条件のアルゴリズム比較という意味には限界がある。
- 530件は [H1研究の54英語語種/19日本語音韻語種](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/research/oov_character_model_experiment.py) を以前と別の文脈に埋め込んだケース（378英語混在+152日本語のみ）。H9由来と語種が重なる**合成robustness試験**で、完全な盲検holdoutではない。ゴールドは断片連結からUTF-16区間を導出し、JSONで保存。
- 152件の日本語のみrobustness入力は、Meltype・旧Meltia・実験Meltiaのすべてで誤英語Top1なし。ただし、この結果で任意の日本語文章の誤検出率0%を主張してはいけない。
- 530件の**2英語島のTop1は実験シャドー6/54、3英語島は2/54**であり、まだ弱い。Meltypeのみでもそれぞれ9/54と2/54。現時点の実用優位は未証明。

## APIとテスト

```csharp
// 既定版: Meltype Core/TSFには接続しない
var normal = new MeltiaBoundaryEngine();

// 研究用のみ（AutoCommitAllowed は常に false）
var research = new MeltiaBoundaryEngine(
    new MeltiaBoundaryOptions(
        ExperimentalStrongHostAdvisory: true,
        ExperimentalFragmentationPenalty: true));
```

- 基本の非侵襲性（`AutoCommitAllowed=false`）、UTF-16整合性、複数必須span、編集のプロパティテストは変更せず実行。
- `NIHONGO` /`KURU` と`IME` /`commit`の相反する入力、単純辞書部分一致`commitsimasita`、Meltypeが混在文全体を英語にしたケースを追加契約化。
- [評価エントリ](../tests/Meltia.BoundaryEvaluation/Program.cs) は `--suite core` (47件) と `--suite robustness` (530件) の2モード。各方式のケース、候補、Top-K、区間F1を別JSON artifactに保存。
- `Meltia-penalty-only-shadow` と`Meltia-host-only-shadow` のアブレーションを両セットで測定。各方式で例外が出たらCI失敗。**ただし精度合格閾値の固定はまだしていない。**

```bash
dotnet run --project tests/Meltia.BoundaryEvaluation/Meltia.BoundaryEvaluation.csproj -c Release -- --output artifacts/meltia-boundary-evaluation.json --suite core
dotnet run --project tests/Meltia.BoundaryEvaluation/Meltia.BoundaryEvaluation.csproj -c Release -- --output artifacts/meltia-boundary-robustness.json --suite robustness
```

## 分かっていないこと・次の改善箇所

- **候補生成が足りない。** 530件の英語正例は540 span。Meltiaの現行単独・辞書なし候補は約4.6%、辞書を揃えた単独でも約39.4%のGold spanしか候補に存在しない。正しい候補がない場合、経路順位だけ変更しても回復できない。
- 複数英語島・短い略語・ローマ字として成立する英語・英字と日本語の意図が衝突する入力は別の根拠が必要。英語の途中で`no`や`de`等が現れても、辞書マッチだけで単語を切らないこと。
- H8/H9のPythonモデルとの**同一コーパス・同一辞書・同一候補生成でのパリティ比較**はまだ済んでいない。この結果はH9の性能比較値ではない。
- **人手で入力意図を確認した自然文holdout**、5 seedの安定性、入力長ごとのp50/p95/p99、Windows TSF/Mozcでの実測が必要。
- 実験スイッチを本番の`CompositionController`・TSF・Mozc・自動確定へ直接接続していない。独立したMeltiaアプリIDの監査完了前には接続禁止。

**次の研究優先順位：** 生成された候補の見逃しを減らす（H1文字モデルやフォネティック/構造候補のC#独立移植）→日本語のみの誤英字検出予算を守りつつ、複数英語島のGold経路を維持→人手holdout評価→実IME統合。
