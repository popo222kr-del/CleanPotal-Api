import ExcelJS from 'exceljs';
import { statusLabel } from './lot';
import { excelDate } from '../excelDate';

// 입·출고 현황 조회 목록을 엑셀로 내보낸다. MES 화면의 [Excel] 과 같은 열·순서다.
//
// 서버를 거치지 않고 화면이 만든다 — 지금 화면에 보이는(드릴다운으로 좁혀진) 그 목록을 그대로
// 내보내야 하는데, 그 목록은 화면이 들고 있기 때문이다. 서버에 다시 물으면 조건을 또 맞춰야 한다.

export type LotInOutRow = {
  line: string | null; pmEquipmentName: string | null; exportNumber: string | null;
  cleaningCode: string | null; productName: string; serialNumber: string; itemCode: string;
  isBatch: boolean; currentStatus: number; recipeCode: string | null; equipmentId: string | null;
  receivedDate: string; stageArrivedAt: string; tatHours: number | null;
  processLabel: string | null; worker: string | null; comment: string | null;
};

const HEADERS = [
  'LINE', '업체명', '반출번호', '세정코드', '제품명', 'S/N', '품목코드', 'BATCH',
  'STATUS', 'RECIPE', '설비명', 'AETS 입고', '공정 입고', 'TAT(시간)', 'PROCESS', '작업자', 'Comment',
];

const stamp = () => {
  const d = new Date(); const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}`;
};

export async function exportLotInOut(rows: LotInOutRow[]): Promise<void> {
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet('입출고 현황');

  ws.addRow(HEADERS);
  const head = ws.getRow(1);
  head.font = { bold: true };
  head.eachCell(c => {
    c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE2E8F0' } };
  });

  for (const r of rows) {
    ws.addRow([
      r.line ?? '', r.pmEquipmentName ?? '', r.exportNumber ?? '', r.cleaningCode ?? '',
      r.productName, r.serialNumber, r.itemCode, r.isBatch ? 'Y' : '',
      statusLabel(r.currentStatus), r.recipeCode ?? '', r.equipmentId ?? '',
      // 날짜는 문자열이 아니라 날짜로 넣어야 엑셀에서 정렬·필터가 제대로 먹는다.
      excelDate(r.receivedDate), excelDate(r.stageArrivedAt),
      r.tatHours ?? '', r.processLabel ?? '', r.worker ?? '', r.comment ?? '',
    ]);
  }

  ws.getColumn(12).numFmt = 'yyyy-mm-dd hh:mm';
  ws.getColumn(13).numFmt = 'yyyy-mm-dd hh:mm';
  ws.columns.forEach((col, i) => { col.width = [10, 16, 14, 12, 30, 18, 14, 8, 10, 12, 12, 18, 18, 10, 12, 10, 30][i] ?? 12; });
  ws.views = [{ state: 'frozen', ySplit: 1 }];
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: HEADERS.length } };

  const buffer = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  }));
  const a = document.createElement('a');
  a.href = url;
  a.download = `입출고현황_${stamp()}.xlsx`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
