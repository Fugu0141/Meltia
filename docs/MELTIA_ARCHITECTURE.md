# Meltia アーキテクチャと整理方針

この文書は Meltype から継承した構造の**現状把握と段階的な整理**を記録するものです。構造上の整理だけで安定化が完了したわけではありません。

## 既存の責務

| モジュール | 主な役割 | 整理時の注意 |
| --- | --- | --- |
| `src/Meltype.Core/Detection` | ローマ字・日本語・英語の判定とスコア | 日英混在時の誤判定と辞書依存を回帰テストする |
| `src/Meltype.Core/Composition/CompositionText.cs` | 未確定文字列・表示用の分割 | IME の逐次入力と確定前後の差を確認する |
| `src/Meltype.Core/Composition/CompositionDetector.cs` | 英語として保持する区間の検出 | URL・コード・略語・固有名詞も検証する |
| `src/Meltype.Core/Composition/CompositionController.cs` | 入力イベント、確定、描画更新などの制御 | キーを横取り／再送する契約を維持する |
| `CompositionController.Conversion.cs` | 文節変換、候補の構築・選択、ライブ変換 | 候補順・変換キャッシュ・文節境界を維持する |
| `CompositionController.Learning.cs` | 確定後の補正、学習・再変換状態 | 誤補正、入力消失、キャレット移動での誤削除に注意する |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | OS 非依存な入力セッションの公開 API | Windows / macOS / Linux 側との互換性に注意する |
| `src/Meltype/MeltypeEngine.cs` | Windows キー入力、IME、スレッド間の制御 | フックと UI スレッド、IME 登録の境界を維持する |
| `src/Meltype/Composition/CompositionService.cs` | Windows 上の表示・入力確定 | OS 側の副作用、クリップボード処理を隔離する |

## 今回の変更（第 1 段階）

- 巨大な `CompositionController` を C# の **`partial`** で 3 つのファイルに整理する。
- 変換・候補選択と学習・事後補正を物理的に分けるが、**クラスの状態と実装は現状維持**する。
- 元のメソッド本体・宣言を**完全に移動**する。パラメータや既存 API、判定・辞書・学習の条件分岐は変更しない。
- パブリックの `Meltype.Core` アセンブリ名、IME ID、インストール／更新機構は今回触らない。

**注意**: これは見通しを良くして次の抽出をしやすくするための低リスクな準備です。単に `partial` に分けても依存度は下がりません。次の段階ではテストを増やしながら、共有状態や副作用を持たないロジックから独立サービスへ抽出します。

## 推奨する次の順序

1. `CompositionController` の入力と表示に関する回帰テストを増やし、現行の失敗例を「既知の不具合」として記録する。
2. `CompositionDetector` / `CompositionText` の境界判定を、IME 操作から切り離したテスト可能なインターフェイスにまとめる。
3. 候補生成の「辞書・変換器問い合わせ」「候補の統合・順位付け」を別責務として抽出する。
4. 学習・事後補正の副作用をまとめ、確定取消・フォーカス移動時の安全性を確認する。
5. 安定した変更だけを取り込み、実験的な境界判定方式は独立した機能フラグとテスト群で管理する。

## 検証ゲート

- `dotnet build Meltype.sln -c Release`（Windows の場合。ほかの OS は Core テストを中心に実施）
- `dotnet run --project src/Meltype.Core.Tests -c Release`
- `dotnet run --project src/Meltype.Core.Tests -c Release -- --eval`
- Windows IME: 変換・確定・取り消し・フォーカス移動・日英混在入力と本家共存を手動で確認（最終リリース前）

今回の PR では**機能の仕様を変更していない**ので、パスするテスト数だけでなく PR 上の品質比較と OS 別 CI の結果を確認してからマージ判断する。
