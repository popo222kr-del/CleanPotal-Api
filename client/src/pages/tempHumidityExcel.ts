import ExcelJS from 'exceljs';
import type { SensorExport } from '../api/types';

// 온·습도 이력을 엑셀로 내보낸다. 품질 기록이나 보고서에 붙이려면 결국 표가 필요하다.
//
// 서버가 조회한 구간의 원본 줄을 그대로 받아 화면에서 파일을 만든다 — 그래프는 보기 좋게
// 묶어서 그리지만, 내보내는 것은 묶지 않은 값이어야 나중에 다시 따져 볼 수 있다.

const HEADERS = ['시간', '사업장', '센서', '온도(℃)', '습도(%)', '배터리(%)', 'LQI', '상태', '구분'];

const stamp = (d: Date) => {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}`;
};

export async function exportTempHumidity(data: SensorExport): Promise<void> {
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet('온습도 이력');

  ws.addRow(HEADERS);
  const head = ws.getRow(1);
  head.font = { bold: true };
  head.eachCell(c => {
    c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE2E8F0' } };
  });

  for (const r of data.rows) {
    ws.addRow([
      // 날짜는 문자열이 아니라 날짜로 넣어야 엑셀에서 정렬·필터가 제대로 먹는다.
      new Date(r.receivedAt),
      r.site, r.deviceName,
      r.temperature ?? '', r.humidity ?? '',
      r.battery ?? '', r.linkQuality ?? '',
      r.statusLabel,
      // 주기 기록은 그 시각에 새로 잰 값이 아니라 마지막 측정값의 반복이다. 구분해 둬야 오해가 없다.
      r.isSnapshot ? '주기 기록' : '수신',
    ]);
  }

  ws.getColumn(1).numFmt = 'yyyy-mm-dd hh:mm:ss';
  ws.columns.forEach((col, i) => { col.width = [20, 10, 16, 10, 10, 11, 8, 10, 10][i] ?? 12; });
  ws.views = [{ state: 'frozen', ySplit: 1 }];
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: HEADERS.length } };

  const buffer = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  }));
  const a = document.createElement('a');
  a.href = url;
  a.download = `온습도이력_${stamp(new Date(data.from))}-${stamp(new Date(data.to))}.xlsx`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
