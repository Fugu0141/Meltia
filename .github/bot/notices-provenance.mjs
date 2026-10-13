// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
//
// Copyright attribution is inherited only when actual implementation lines move
// from a file modified/removed in this PR (or a verified GitHub rename).

const MinimumRun = 8;

/**
 * A copyright notice must be a comment-line header, not arbitrary source code
 * containing a string literal with the words "Copyright" and an author.
 * Accept the comment syntaxes used by the extensions monitored by notices.mjs.
 */
export function copyrightOf(line) {
  const comment = /^\s*(?:\/\/+|#+|;+|\/\*+|\*+|<#+)\s*(.+)$/.exec(line)?.[1];
  if (!comment) return null;
  const notice = comment.replace(/\s*(?:\*\/|#>)\s*$/, '').trim();
  return /^copyright\s*(?:\(c\)|©)?\s*\d{4}\b/i.test(notice) ? notice : null;
}

/** Ignore file envelopes and comments, not executable code or preprocessor directives. */
export function implementationLines(text) {
  const lines = Array.isArray(text) ? text : text.split(/\r?\n/);
  return lines.map(line => line.trim()).filter(line =>
    line && line !== '{' && line !== '}' &&
    !line.startsWith('//') && !line.startsWith('/*') &&
    !/^\*(?:\/|\s)/.test(line) &&
    !/^using\s+(?:static\s+)?[\w.]+\s*;/.test(line) && !/^namespace\s/.test(line) &&
    !/^(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*class\s+\w+/.test(line));
}

/** Match whole implementation lines in long, ordered, contiguous blocks. */
function coveredBy(original, moved) {
  if (moved.length < MinimumRun || original.length < MinimumRun) return false;
  const windows = new Set();
  for (let i = 0; i <= original.length - MinimumRun; i++) {
    windows.add(JSON.stringify(original.slice(i, i + MinimumRun)));
  }
  const covered = new Uint8Array(moved.length);
  for (let i = 0; i <= moved.length - MinimumRun; i++) {
    if (!windows.has(JSON.stringify(moved.slice(i, i + MinimumRun)))) continue;
    covered.fill(1, i, i + MinimumRun);
  }
  return covered.every(Boolean);
}

const removedLines = patch => (patch ?? '').split('\n')
  .filter(line => line.startsWith('-') && !line.startsWith('---'))
  .map(line => line.slice(1));

function hasSharedRun(original, moved) {
  if (original.length < MinimumRun || moved.length < MinimumRun) return false;
  const windows = new Set();
  for (let i = 0; i <= original.length - MinimumRun; i++) {
    windows.add(JSON.stringify(original.slice(i, i + MinimumRun)));
  }
  for (let i = 0; i <= moved.length - MinimumRun; i++) {
    if (windows.has(JSON.stringify(moved.slice(i, i + MinimumRun)))) return true;
  }
  return false;
}

/** Only changed source files with matching deleted code can establish movement. */
export function sourcePathsFor(target, files, newLines) {
  const moved = implementationLines(newLines);
  if (moved.length < MinimumRun) return [];
  const extension = /\.[^.\/]+$/.exec(target.filename)?.[0]?.toLowerCase();
  const renamedOrigin = target.status === 'renamed' ? target.previous_filename : null;
  const candidates = files.filter(file => {
    if (file === target || file.filename === target.filename) return false;
    const origin = file.status === 'renamed' ? file.previous_filename : file.filename;
    if (!origin || /\.[^.\/]+$/.exec(origin)?.[0]?.toLowerCase() !== extension) return false;
    if (origin.split('/').some(part => !part || part === '.' || part === '..')) return false;
    if (!['modified', 'removed', 'renamed'].includes(file.status)) return false;
    // This merely selects candidates. The base revision must independently
    // prove that *every* implementation line in the target was inherited.
    return hasSharedRun(implementationLines(removedLines(file.patch)), moved);
  }).map(file => file.status === 'renamed' ? file.previous_filename : file.filename);
  if (renamedOrigin && !renamedOrigin.split('/').some(part => !part || part === '.' || part === '..')) {
    candidates.push(renamedOrigin);
  }
  return [...new Set(candidates)];
}

/** Accept only the exact copyright line held by a proven source at PR base. */
export function movedCopyrightIsProven(target, files, baseContents, newLines, copyright) {
  const moved = implementationLines(newLines);
  if (moved.length < MinimumRun) return false;
  for (const path of sourcePathsFor(target, files, newLines)) {
    const originalText = baseContents.get(path);
    if (typeof originalText !== 'string') continue;
    const copyrightHeaders = originalText.split(/\r?\n/).slice(0, 8).map(copyrightOf);
    if (!copyrightHeaders.includes(copyright)) continue;
    if (coveredBy(implementationLines(originalText), moved)) return true;
  }
  return false;
}
