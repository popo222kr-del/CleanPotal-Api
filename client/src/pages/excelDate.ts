/**
 * ExcelJS 는 Date 를 UTC 기준으로 셀 값에 바꿔 넣는다. 그대로 넣으면 한국 시각 14:26 이 05:26 으로 찍힌다.
 * 벽시계 시각이 셀에 그대로 보이도록 시간대 차이만큼 밀어서 넘긴다.
 */
export function excelDate(value: string | number | Date): Date {
  const d = new Date(value);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000);
}
