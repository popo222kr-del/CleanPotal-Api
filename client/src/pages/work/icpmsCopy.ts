import type { IcpmsMeasurement } from '../../api/types';

// ICP-MS 보고서 블록 — 엑셀 "AETS QA ICP-MS Data(설비별 시트)" 의 한 칸:
//   5. ICP-MS 측정 결과 (26/09/02)
//   Chemical Name | SPEC | Li Na Mg Al K Ca Cr Mn Fe Ni Cu Zn Pb | 총 합 | 비 고
//   DI / HF / S2  | <1ppb | 값(소수 3자리, 없으면 -)           | 합계  | 각 원소별 SPEC 적용(세 줄 병합)
// 블록 사이 빈 줄 하나 — 엑셀 시트가 날짜마다 이 6줄을 반복한다.
// 복사는 HTML 표로 넣어 엑셀에 붙여넣으면 병합·테두리·색·숫자 형식이 그대로 들어간다.

export const REPORT_ELS = ['Li', 'Na', 'Mg', 'Al', 'K', 'Ca', 'Cr', 'Mn', 'Fe', 'Ni', 'Cu', 'Zn', 'Pb'] as const;
export const REPORT_CHEMS = ['DI', 'HF', 'S2'] as const;
export type Line = 'METAL' | 'N-METAL';

/** 설비 라인 — N 으로 시작하거나 SPC(외주세정 전용, N-METAL 파일에 있음)면 N-METAL. */
export const lineOf = (eqId: string): Line => (/^(N|SPC)/i.test(eqId.trim()) ? 'N-METAL' : 'METAL');
/** 엑셀 시트 순서 — SPC 먼저, 나머지는 이름 순(MDC01 < MDC10 < MSC02-1). */
export const eqCmp = (a: string, b: string) =>
  Number(!/^SPC/i.test(a)) - Number(!/^SPC/i.test(b)) || a.localeCompare(b, 'en', { numeric: true });

export interface ReportRow { chem: string; values: number[] | null; total: number | null }
export interface ReportBlock { eqId: string; date: string; rows: ReportRow[]; measured: boolean }

/** 한 설비·한 날짜의 블록. DI·HF·S2 세 줄은 늘 두고, 그 밖의 약액 측정이 있으면 아래에 붙인다. */
export function buildBlock(eqId: string, date: string, ms: IcpmsMeasurement[]): ReportBlock {
  const byChem = new Map<string, IcpmsMeasurement>();
  for (const m of ms) {
    const k = m.bathGb.trim().toUpperCase();
    const cur = byChem.get(k);
    if (!cur || m.id > cur.id) byChem.set(k, m);   // 같은 약액이 여럿이면 나중에 넣은 것
  }
  const chems = [...REPORT_CHEMS, ...[...byChem.keys()].filter(k => k && !(REPORT_CHEMS as readonly string[]).includes(k)).sort()];
  const rows = chems.map(chem => {
    const m = byChem.get(chem);
    if (!m) return { chem, values: null, total: null };
    const values = REPORT_ELS.map(el => m.values[el] ?? 0);
    return { chem, values, total: values.reduce((s, v) => s + v, 0) };
  });
  return { eqId, date, rows, measured: rows.some(r => r.values) };
}

/** 26/09/02 */
export const shortDate = (d: string) => `${d.slice(2, 4)}/${d.slice(5, 7)}/${d.slice(8, 10)}`;
export const f3 = (v: number) => v.toFixed(3);

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const B = 'border:.5pt solid #000;';
const TD = `${B}text-align:center;vertical-align:middle;font-family:'맑은 고딕';font-size:9pt;height:18pt;`;
const HEAD = `${TD}background:#D9E1F2;font-weight:bold;`;
const NUM = `${TD}mso-number-format:'0\\.000';`;

function blockHtml(b: ReportBlock): string {
  const cols = 2 + REPORT_ELS.length + 2;
  const head = ['Chemical Name', 'SPEC', ...REPORT_ELS, '총 합', '비 고'].map(h => `<td style="${HEAD}">${h}</td>`).join('');
  const body = b.rows.map((r, i) => {
    const cells = r.values
      // 칸에는 원래 값 전체를 넣고 표시만 소수 3자리(엑셀 원본과 같게) — 반올림한 값이 들어가지 않게
      ? r.values.map(v => `<td style="${NUM}">${v}</td>`).join('') + `<td style="${NUM}font-weight:bold;">${r.total}</td>`
      : Array.from({ length: REPORT_ELS.length + 1 }, () => `<td style="${TD}">-</td>`).join('');
    const note = i === 0 ? `<td rowspan="${b.rows.length}" style="${TD}font-size:7pt;">각 원소별 SPEC 적용</td>` : '';
    return `<tr><td style="${TD}font-weight:bold;">${esc(r.chem)}</td><td style="${TD}font-weight:bold;">&lt;1ppb</td>${cells}${note}</tr>`;
  }).join('');
  const title = `<tr><td colspan="${cols}" style="font-family:'맑은 고딕';font-size:11pt;font-weight:bold;color:#1F3864;text-align:left;border-bottom:1.5pt solid #1F3864;height:20pt;">5. ICP-MS 측정 결과 (${shortDate(b.date)})</td></tr>`;
  return title + `<tr>${head}</tr>` + body;
}

function blockText(b: ReportBlock): string {
  const lines = [
    `5. ICP-MS 측정 결과 (${shortDate(b.date)})`,
    ['Chemical Name', 'SPEC', ...REPORT_ELS, '총 합', '비 고'].join('\t'),
    ...b.rows.map((r, i) => [r.chem, '<1ppb', ...(r.values ? [...r.values.map(f3), f3(r.total!)] : Array(REPORT_ELS.length + 1).fill('-')),
      i === 0 ? '각 원소별 SPEC 적용' : ''].join('\t')),
  ];
  return lines.join('\n');
}

/** 블록 여러 개를 엑셀 시트처럼 빈 줄 하나씩 띄워 한 표로. */
export function blocksToClipboard(blocks: ReportBlock[]): { html: string; text: string } {
  const gap = '<tr><td style="height:18pt;"></td></tr>';
  const html = `<table style="border-collapse:collapse;">${blocks.map(blockHtml).join(gap)}</table>`;
  const text = blocks.map(blockText).join('\n\n');
  return { html, text };
}

/**
 * 서식 있는 복사. https(보안 연결)면 Clipboard API, 사내 http 주소면 copy 이벤트로 넣는다
 * (http 에서는 navigator.clipboard 가 없다).
 */
export async function copyRich(html: string, text: string): Promise<boolean> {
  try {
    if (window.isSecureContext && navigator.clipboard && 'ClipboardItem' in window) {
      await navigator.clipboard.write([new ClipboardItem({
        'text/html': new Blob([html], { type: 'text/html' }),
        'text/plain': new Blob([text], { type: 'text/plain' }),
      })]);
      return true;
    }
  } catch { /* 아래 방법으로 */ }
  let ok = false;
  const onCopy = (e: ClipboardEvent) => {
    e.clipboardData?.setData('text/html', html);
    e.clipboardData?.setData('text/plain', text);
    e.preventDefault();
    ok = true;
  };
  document.addEventListener('copy', onCopy);
  try { document.execCommand('copy'); } finally { document.removeEventListener('copy', onCopy); }
  return ok;
}
