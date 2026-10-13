# Issue #22 — 分割・移動した原著コードの著作権表示を保持する

対象: [Issue #22](https://github.com/Fugu0141/Meltia/issues/22) / [PR #20](https://github.com/Fugu0141/Meltia/pull/20)

## 背景と再現

PR #20 で `CompositionController.cs` から `CompositionController.Conversion.cs` と `CompositionController.Learning.cs` を `partial` 分割した際、新規ファイルに引き継がれた `Copyright (C) 2026 Yukishiro` を `.github/bot/notices.mjs` が誤って新規の著作権主張とみなし、PR 作者名に置き換えるよう警告した。実際は既存コードを移動したものであり、元著作権の表示を保持する必要がある。

## 設計・受け入れ条件

1. PR の `pulls/{number}/files` の削除行で、追加先と最低8行の連続した実装行の一致を確認し、変更された移動元ファイルの候補を選ぶ。GitHub が `renamed` として報告した場合は `previous_filename` も候補にする。
2. 候補の**PR base SHA** にあるファイルを GitHub API の `contents` から読み、先頭8行に追加先と**同一の Copyright 表示**があることを確認する。PR head のファイルやコードは実行しない。
3. 追加先の実装行（空行、コメント、using/namespace/partial class の外枠、単独の波括弧は除く）が、base のコード内の連続した8行以上のブロックで**すべて**カバーされる場合のみ、原著名義の表示を認める。新規の処理を混在させた場合は認めない。SPDX/Copyright 欠落は引き続き検出する。
4. 小さい移動、整形差、GitHub patch 欠落、巨大ファイル、base 取得不可などで移動を証明できない場合は保守的に警告を維持する。特に追加ソースの `patch` が欠落・空文字の場合は**検証不能の警告**を出し、SPDX/Copyright/出自が適切だと誤って判定しない。既存ソースの変更で `patch` が無い場合は元から追加行を判定できない制約がある。著作権を自動で上書きしたり削除したりはしない。候補の base 読み取りは最大24ファイル、1ファイル1MBまで。
5. `pull_request_target` は**引き続き default branch の bot のみをチェックアウト**し、PR コードを実行しない。別の `pull_request` ワークフローでコードを実行する自己テストは read-only、永続的な資格情報なし。

この照合は自動的な出自の証拠検査であり、著作権の法的判断そのものではない。

## 検証

- PR #20 の移動元: `src/Meltype.Core/Composition/CompositionController.cs`（PR base）。移動先の実装行は大部分が一致し、ファイル外枠を除けば実装の移動を確認できる。
- `node --test .github/bot/notices-provenance.test.mjs` — **11/11 成功（GitHub Actions）**。部分分割の再現、新規実装・混在実装・出自不一致・削除差分なし・短い一致・別種ファイルに加え、文字列リテラルの誤検出と、rename された移動元の照合を検証。
- `node .github/bot/notices.mjs --self-test` — 既存の検査に加え、新規ファイルの SPDX 欠落・Copyright 欠落・双方欠落・著作権名義の混在・部分分割の検査を追加。PR CI で確認する。
- `.github/workflows/notices-test.yml` — PR HEAD 上の両テストを read-only で実行する。PR 作成後はその HEAD の Actions 結果を確認する。

## PR #31 レビュー対応（2026-10-14）

[PR #31 のレビュー](https://github.com/Fugu0141/Meltia/pull/31)において、追加した self-test の文字列リテラルを通知 bot が著作権行として誤検出している点が指摘された。

- `copyrightOf()` を共通の `notices-provenance.mjs` にまとめ、**行頭のコメント記号の直後**に著作権行がある場合だけ認識する。引用符の中や通常のコード行に含まれる文字列は著作権ヘッダーとみなさない。移動元 base ファイルの著作権照合にも同じ関数を用いる。
- テストデータは実行時に連結し、**まだ main に存在する旧版 bot** からも誤認識されない形にする。元著作者の表示そのものは変更・削除しない。
- `status=renamed` の移動元について、GitHub の `previous_filename` を base パスとして照合する。変更差分に移動したコードが含まれている場合だけ候補とし、base のコード照合を省略しない。
- 追加の回帰テストは「単一行・複数行の著作権コメントを認識」「コード文字列は無視」「rename したファイルを移動元とする」「base の文字列リテラルを原著ヘッダーと誤認しない」。既存の著作権の新規偽装・混在検出は維持。

コード修正時点の同一 HEAD `2e9a3098` では以下がすべて success：

- [notices-test（11件の出自テスト＋ bot の自己テスト）](https://github.com/Fugu0141/Meltia/actions/runs/38425408580)
- [pr-checks](https://github.com/Fugu0141/Meltia/actions/runs/38425408445)
- [notices](https://github.com/Fugu0141/Meltia/actions/runs/38425404460)

なお、以前の PR #31 の bot 誤警告は、今回のコミットに伴う `pull_request_target` の再実行で `✅ ライセンス・著作権の表記を確認しました` へ更新された。これは既存 default-branch bot での誤警告解消を確認したものだが、改良済み bot の動作確認は上記 read-only CI の自己テストを用いる。

## 追加確認：SPDX ヘッダー判定の整合性

レビューの直接指摘（Copyright の文字列リテラル誤検出）は前回のコミットで解消・スレッド解決済み。今回の再確認では同じヘッダー処理に、SPDX の有無だけが `includes('SPDX-License-Identifier')` による任意の文字列検索のまま残っていた。これは新規ファイルの実際の SPDX ヘッダー欠落を見逃す可能性があり、Issue #22 の欠落検出の受け入れ条件に関わる。

- Copyright と共通のコメント行抽出処理 `commentText()` を使い、`spdxOf()` で有効な `SPDX-License-Identifier:` コメント行だけ認識する。実装ロジックやライセンス種別は変更しない。
- コード中のリテラル、文字列の部分一致、識別子名だけでの偽装、値のない SPDX コメントを拒否する回帰ケースを追加。
- 新規ファイルにある shebang 後の正しい SPDX/Copyright ヘッダーは従来通り許可。
- 変更範囲は通知 bot とテストに限定する。GitHub 上の追加レビューコメントはなく、修正は関連領域の自主監査として実施した。
- 検証：出自ユニットテスト **12件**および notice 本体の自己テストが [notices-test](https://github.com/Fugu0141/Meltia/actions/runs/38427137754) で成功。[pr-checks](https://github.com/Fugu0141/Meltia/actions/runs/38427137750) と [notices](https://github.com/Fugu0141/Meltia/actions/runs/38427131761) も同じ HEAD `5fed1c1` で成功。

## 非対象・注意

IME の変換、境界判定、学習、辞書、Windows アプリ等には一切変更しない。ライセンス方針自体も変更しない。誤警告の対処として、原著者の名義を削除・置換してはならない。

## PR #31 再レビュー対応：patch 不在時の保守的判定（2026-10-14）

- レビュー指摘のとおり、従来の `if (!file.patch) continue` では追加されたソースファイルが未検証のまま警告ゼロ件になる可能性があった。`status=added` のソース拡張子を対象に、`patch=null`・プロパティ無し・空文字のケースで**検証不能警告**を返す。
- 対象外拡張子の文書等は従来通り警告しない。出自の確認に失敗した場合も、原著作権の表示を自動変更させることはない。読み取れた場合の既存の SPDX/Copyright・部分移動照合は変更しない。
- `node .github/bot/notices.mjs --self-test` に4件の境界回帰を追加。PR HEAD の read-only Actions による再実行結果を確認し、最終判断をする。
