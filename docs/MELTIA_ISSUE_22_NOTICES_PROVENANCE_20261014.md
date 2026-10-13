# Issue #22 — 分割・移動した原著コードの著作権表示を保持する

対象: [Issue #22](https://github.com/Fugu0141/Meltia/issues/22) / [PR #20](https://github.com/Fugu0141/Meltia/pull/20)

## 背景と再現

PR #20 で `CompositionController.cs` から `CompositionController.Conversion.cs` と `CompositionController.Learning.cs` を `partial` 分割した際、新規ファイルに引き継がれた `Copyright (C) 2026 Yukishiro` を `.github/bot/notices.mjs` が誤って新規の著作権主張とみなし、PR 作者名に置き換えるよう警告した。実際は既存コードを移動したものであり、元著作権の表示を保持する必要がある。

## 設計・受け入れ条件

1. PR の `pulls/{number}/files` の削除行で、追加先と最低8行の連続した実装行の一致を確認し、変更された移動元ファイルの候補を選ぶ。GitHub が `renamed` として報告した場合は `previous_filename` も候補にする。
2. 候補の**PR base SHA** にあるファイルを GitHub API の `contents` から読み、先頭8行に追加先と**同一の Copyright 表示**があることを確認する。PR head のファイルやコードは実行しない。
3. 追加先の実装行（空行、コメント、using/namespace/partial class の外枠、単独の波括弧は除く）が、base のコード内の連続した8行以上のブロックで**すべて**カバーされる場合のみ、原著名義の表示を認める。新規の処理を混在させた場合は認めない。SPDX/Copyright 欠落は引き続き検出する。
4. 小さい移動、整形差、GitHub patch 欠落、巨大ファイル、base 取得不可などで移動を証明できない場合は保守的に警告を維持する。著作権を自動で上書きしたり削除したりはしない。候補の base 読み取りは最大24ファイル、1ファイル1MBまで。
5. `pull_request_target` は**引き続き default branch の bot のみをチェックアウト**し、PR コードを実行しない。別の `pull_request` ワークフローでコードを実行する自己テストは read-only、永続的な資格情報なし。

この照合は自動的な出自の証拠検査であり、著作権の法的判断そのものではない。

## 検証

- PR #20 の移動元: `src/Meltype.Core/Composition/CompositionController.cs`（PR base）。移動先の実装行は大部分が一致し、ファイル外枠を除けば実装の移動を確認できる。
- `node --test .github/bot/notices-provenance.test.mjs` — ローカル Node.js v22 で **8/8 成功**（部分分割の再現、新規実装・混在実装・出自不一致・削除差分なし・短い一致・別種ファイル等の回帰）。
- `node .github/bot/notices.mjs --self-test` — 既存の検査に加え、新規ファイルの SPDX 欠落・Copyright 欠落・双方欠落・著作権名義の混在・部分分割の検査を追加。PR CI で確認する。
- `.github/workflows/notices-test.yml` — PR HEAD 上の両テストを read-only で実行する。PR 作成後はその HEAD の Actions 結果を確認する。

## 非対象・注意

IME の変換、境界判定、学習、辞書、Windows アプリ等には一切変更しない。ライセンス方針自体も変更しない。誤警告の対処として、原著者の名義を削除・置換してはならない。
