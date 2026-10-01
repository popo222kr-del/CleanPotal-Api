import ExcelJS from 'exceljs';

// 업체 관리 — 관리자용 전체 엑셀 내려받기. 부서·분류 탭이나 검색과 상관없이 등록된 업체를 모두 담는다.
// 시트 셋: 업체 목록(한 업체 한 줄, 주소·담당자는 칸 안 줄바꿈) / 담당자(한 사람 한 줄) / 주소(한 곳 한 줄).
// 담당자·주소 시트는 따로 걸러 보거나 정렬하기 쉽게 풀어 둔 것이다.

export interface VendorExcelRow {
  dept: string; category: string; name: string; weekly: boolean;
  addrs: { isMain: boolean; locationName: string; fullAddress: string }[];
  mgrs: { managerName: string; contactNumber: string }[];
  basePath: string; linkUrl: string;
  mes: { code: string; prefix: string; line: string; active: boolean } | null;
}

const HEAD_FILL: ExcelJS.Fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFEEF2F8' } };
const BORDER: Partial<ExcelJS.Borders> = {
  top: { style: 'thin', color: { argb: 'FFD5DCE5' } }, bottom: { style: 'thin', color: { argb: 'FFD5DCE5' } },
  left: { style: 'thin', color: { argb: 'FFD5DCE5' } }, right: { style: 'thin', color: { argb: 'FFD5DCE5' } },
};

function sheet(wb: ExcelJS.Workbook, name: string, head: string[], widths: number[], rows: (string | number)[][]) {
  const ws = wb.addWorksheet(name, { views: [{ state: 'frozen', ySplit: 1 }] });
  ws.addRow(head);
  for (const r of rows) ws.addRow(r);
  ws.getRow(1).eachCell(c => { c.font = { bold: true }; c.fill = HEAD_FILL; c.alignment = { vertical: 'middle', horizontal: 'center' }; });
  ws.eachRow(row => row.eachCell(c => {
    c.border = BORDER;
    if (row.number > 1) c.alignment = { vertical: 'top', wrapText: true };
  }));
  widths.forEach((w, i) => { ws.getColumn(i + 1).width = w; });
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: head.length } };
}

export async function downloadVendorExcel(rows: VendorExcelRow[], withMes: boolean) {
  const wb = new ExcelJS.Workbook();
  const sorted = [...rows].sort((a, b) => a.dept.localeCompare(b.dept, 'ko') || a.category.localeCompare(b.category, 'ko') || a.name.localeCompare(b.name, 'ko'));

  const addrText = (r: VendorExcelRow) => r.addrs.map(a => `${a.isMain ? '[주] ' : ''}${a.locationName ? `${a.locationName}: ` : ''}${a.fullAddress}`).join('\n');
  const mgrText = (r: VendorExcelRow) => r.mgrs.map(m => [m.managerName, m.contactNumber].filter(Boolean).join(' ')).join('\n');

  const head = ['부서', '분류', '업체명', '주간세정', '주소', '담당자', '자료 폴더', '링크',
    ...(withMes ? ['MES 업체코드', '반출번호 약어', 'LINE', 'MES 사용'] : [])];
  sheet(wb, '업체 목록', head, [12, 12, 24, 9, 48, 30, 30, 30, ...(withMes ? [14, 12, 12, 10] : [])],
    sorted.map(r => [r.dept, r.category, r.name, r.weekly ? 'O' : '', addrText(r), mgrText(r), r.basePath, r.linkUrl,
      ...(withMes ? (r.mes ? [r.mes.code, r.mes.prefix, r.mes.line, r.mes.active ? '사용' : '안 씀'] : ['', '', '', '']) : [])]));

  sheet(wb, '담당자', ['부서', '분류', '업체명', '담당자', '연락처'], [12, 12, 24, 16, 18],
    sorted.flatMap(r => r.mgrs.map(m => [r.dept, r.category, r.name, m.managerName, m.contactNumber])));

  sheet(wb, '주소', ['부서', '분류', '업체명', '주 사업장', '장소', '주소'], [12, 12, 24, 10, 16, 60],
    sorted.flatMap(r => r.addrs.map(a => [r.dept, r.category, r.name, a.isMain ? 'O' : '', a.locationName, a.fullAddress])));

  const buf = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  const a = document.createElement('a');
  a.href = url;
  a.download = `업체목록_전체_${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}.xlsx`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
