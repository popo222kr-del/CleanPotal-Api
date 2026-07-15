import ExcelJS from 'exceljs';
import type { InventoryZone, InventoryItem } from '../api/types';

// 실사 스테이징 항목 (업로드 파싱 결과 — DB 미반영)
export interface StagedRow { id: number; orderNo: number; itemName: string; oldStock: string; newStock: string; }

const ZONE_FILL: Record<string, string> = {
  metal: 'FFDBEAFE', nonmetal: 'FFFCE7F3', office: 'FFDCFCE7', cleaning: 'FFEDE9FE',
};
const HEADERS = ['NO', '품목명', '위치', '현재재고', '적정재고', '체크', '발주여부', '발주날짜', '입고예정', '발주회사', '비고'];

function ymd(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}`;
}

/** 현장 실사용 A4 가로 엑셀 내보내기 (수기 "체크" 칸 포함). */
export async function exportInventory(zones: InventoryZone[]): Promise<void> {
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet('재고 리스트', {
    pageSetup: { orientation: 'landscape', paperSize: 9, fitToWidth: 1, fitToHeight: 0, margins: { left: 0.3, right: 0.3, top: 0.4, bottom: 0.4, header: 0.2, footer: 0.2 } },
  });
  ws.columns = [
    { width: 6 }, { width: 30 }, { width: 14 }, { width: 12 }, { width: 12 },
    { width: 10 }, { width: 9 }, { width: 12 }, { width: 12 }, { width: 16 }, { width: 24 },
  ];

  // 1행: 제목
  ws.mergeCells(1, 1, 1, HEADERS.length);
  const title = ws.getCell(1, 1);
  const d = new Date();
  title.value = `현장 재고 리스트 (${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')})`;
  title.font = { bold: true, size: 14 };
  title.alignment = { vertical: 'middle', horizontal: 'center' };
  ws.getRow(1).height = 24;

  // 2행: 머리글
  const head = ws.getRow(2);
  HEADERS.forEach((h, i) => {
    const c = head.getCell(i + 1);
    c.value = h;
    c.font = { bold: true, color: { argb: 'FFFFFFFF' } };
    c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF334155' } };
    c.alignment = { vertical: 'middle', horizontal: 'center' };
    c.border = thin();
  });
  head.height = 22;

  let r = 3;
  for (const z of zones) {
    if (z.items.length === 0) continue;
    // 구역 그룹 헤더 행
    ws.mergeCells(r, 1, r, HEADERS.length);
    const gh = ws.getCell(r, 1);
    gh.value = `◤ ${z.locations || z.zoneName} (${z.items.length}개)`;
    gh.font = { bold: true, color: { argb: 'FF0F172A' } };
    gh.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: ZONE_FILL[z.zoneKey] ?? 'FFF1F5F9' } };
    gh.alignment = { vertical: 'middle', horizontal: 'left' };
    ws.getRow(r).height = 20;
    r++;

    for (const it of z.items) {
      const row = ws.getRow(r);
      const cells = [
        it.orderNo, it.itemName, it.storageLocation, it.currentStock, it.appropriateStock,
        '', it.isOrdered ? '완료' : '', it.orderDate, it.expectedReceipt, it.supplier, it.memo,
      ];
      cells.forEach((v, i) => {
        const c = row.getCell(i + 1);
        c.value = v as ExcelJS.CellValue;
        c.border = thin();
        c.alignment = { vertical: 'middle', horizontal: i === 1 || i === 10 ? 'left' : 'center', wrapText: i === 10 };
        if (it.isLow) { c.font = { color: { argb: 'FFB91C1C' }, bold: i === 1 }; c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFFEF2F2' } }; }
      });
      // 체크 칸(6열) 노랑
      row.getCell(6).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFFDE047' } };
      r++;
    }
  }

  const buf = await wb.xlsx.writeBuffer();
  download(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), `재고리스트_${ymd()}.xlsx`);
}

/** 실사 업로드 파싱: 1열(NO)이 정수인 행만, 6열(체크) 값이 있으면 새 현재고 후보. order_no로 매칭. 변경분만 반환. */
export async function parseInventoryUpload(file: File, items: InventoryItem[]): Promise<StagedRow[]> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const ws = wb.worksheets[0];
  if (!ws) throw new Error('시트를 찾을 수 없습니다.');
  const byOrder = new Map(items.map(i => [i.orderNo, i]));
  const staged: StagedRow[] = [];
  ws.eachRow((row) => {
    const no = row.getCell(1).value;
    const orderNo = typeof no === 'number' ? no : Number.isInteger(Number(no)) && String(no).trim() !== '' ? Number(no) : NaN;
    if (!Number.isInteger(orderNo)) return;            // 제목·머리글·구역행 자동 스킵
    const check = cellText(row.getCell(6).value);
    if (check === '') return;                           // 체크값 없으면 대상 아님
    const it = byOrder.get(orderNo);
    if (!it) return;
    if (check !== (it.currentStock ?? '').trim())       // 기존과 다른 것만
      staged.push({ id: it.id, orderNo, itemName: it.itemName, oldStock: it.currentStock, newStock: check });
  });
  return staged;
}

function cellText(v: ExcelJS.CellValue): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'object' && 'result' in v) return String((v as { result: unknown }).result ?? '').trim();
  if (typeof v === 'object' && 'text' in v) return String((v as { text: unknown }).text ?? '').trim();
  return String(v).trim();
}

function thin(): Partial<ExcelJS.Borders> {
  const s = { style: 'thin' as const, color: { argb: 'FFCBD5E1' } };
  return { top: s, bottom: s, left: s, right: s };
}

function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
