import ExcelJS from 'exceljs';
import type { CheckReport } from '../../api/types';

// 월간 리포트를 엑셀로. 표 모양은 화면과 같게(항목 × 일/교대), 요약·NG 목록은 시트를 나눈다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const thin = { style: 'thin' as const, color: { argb: 'FFBFBFBF' } };
const box = { top: thin, left: thin, bottom: thin, right: thin };

function mark(v: string): string {
  if (v === 'O') return '○';
  if (v === 'X') return '✕';
  if (v.startsWith('!')) return v.slice(1);
  return v;
}

export async function exportCheckReport(r: CheckReport): Promise<void> {
  const wb = new ExcelJS.Workbook();
  const title = `${r.year}년 ${r.month}월 ${r.line} ${r.formName}`;

  // ── 점검표 ──
  const ws = wb.addWorksheet('점검표', { pageSetup: { orientation: 'landscape', paperSize: 8 as unknown as ExcelJS.PaperSize, fitToPage: true, fitToWidth: 1, fitToHeight: 0 } });
  ws.addRow([title]).font = { bold: true, size: 14 };
  ws.addRow([`${r.revision}${r.effectiveDate ? ` · 시행일 ${r.effectiveDate}` : ''} · ○ 양호 / ✕ NG / 숫자 측정값 / N/A / 미 = 점검 안 함`]);
  ws.addRow([]);
  const head1 = ['구역', '항목ID', '점검 항목', '시점'];
  const head2 = ['', '', '', ''];
  for (let d = 1; d <= r.days; d++) {
    const w = DOW[new Date(r.year, r.month - 1, d).getDay()];
    head1.push(`${d}(${w})`, '');
    head2.push('주', '야');
  }
  const h1 = ws.addRow(head1);
  const h2 = ws.addRow(head2);
  for (let d = 0; d < r.days; d++) ws.mergeCells(h1.number, 5 + d * 2, h1.number, 6 + d * 2);
  for (let c = 1; c <= 4; c++) ws.mergeCells(h1.number, c, h2.number, c);
  [h1, h2].forEach(row => row.eachCell(c => {
    c.font = { bold: true, size: 9 };
    c.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
    c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE2E8F0' } };
    c.border = box;
  }));

  for (const row of r.rows) {
    const x = ws.addRow([row.zoneName, row.itemCode, row.detail ? `${row.text}\n(${row.detail})` : row.text, row.timing, ...row.cells.map(mark)]);
    x.eachCell({ includeEmpty: true }, (c, col) => {
      c.border = box;
      c.font = { size: 9 };
      c.alignment = col <= 4 ? { vertical: 'middle', wrapText: true } : { horizontal: 'center', vertical: 'middle' };
      if (col > 4) {
        const v = row.cells[col - 5] ?? '';
        if (v === 'X' || v.startsWith('!')) c.font = { size: 9, bold: true, color: { argb: 'FFC0392B' } };
        else if (v === '미') c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFFDECEA' } };
      }
    });
  }
  ws.getColumn(1).width = 11;
  ws.getColumn(2).width = 7;
  ws.getColumn(3).width = 42;
  ws.getColumn(4).width = 10;
  for (let c = 5; c <= 4 + r.days * 2; c++) ws.getColumn(c).width = 3.6;
  ws.views = [{ state: 'frozen', xSplit: 4, ySplit: h2.number }];

  // ── 구역별 요약 ──
  const sum = wb.addWorksheet('구역별 요약');
  sum.addRow([title]).font = { bold: true, size: 13 };
  sum.addRow([]);
  sum.addRow(['구역', '대상', '완료', '미점검', 'NG', '이행률']).font = { bold: true };
  for (const z of r.zones) sum.addRow([z.zoneName, z.due, z.done, z.missing, z.ng, z.due ? z.done / z.due : null]);
  sum.getColumn(6).numFmt = '0.0%';
  sum.getColumn(1).width = 14;

  // ── NG 목록 ──
  const ng = wb.addWorksheet('NG 목록');
  ng.addRow([title]).font = { bold: true, size: 13 };
  ng.addRow([]);
  ng.addRow(['근무일', '교대', '구역', '항목ID', '항목', '측정값', '기준', 'NG 사유', '점검자', '조치 상태', '조치 내용', '조치자']).font = { bold: true };
  for (const n of r.ngs) {
    ng.addRow([n.workDate, n.shift, n.zoneName, n.itemCode, n.itemText, n.numValue, n.specText, n.memo, n.checkedByName,
      n.ngStatus === 'DONE' ? '조치 완료' : '미조치', n.ngCloseNote, n.ngClosedBy]);
  }
  [12, 6, 12, 8, 40, 8, 12, 30, 10, 10, 30, 10].forEach((w, i) => { ng.getColumn(i + 1).width = w; });

  const buf = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = `체크시트_${r.line}_${r.year}${String(r.month).padStart(2, '0')}.xlsx`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
