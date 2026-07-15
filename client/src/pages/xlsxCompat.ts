import ExcelJS from 'exceljs';
import JSZip from 'jszip';

const MAIN_NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';

/**
 * WPF(OpenXML SDK/ClosedXML)가 생성한 xlsx 호환 로더.
 * 그런 파일은 XML이 네임스페이스 접두사(<x:workbook>)와 절대경로 rel(Target="/xl/…")로 쓰여
 * ExcelJS가 파싱하지 못한다 → 표준 형식으로 정규화 후 재시도.
 */
export async function loadWorkbookCompat(file: File): Promise<ExcelJS.Workbook> {
  if (!file.name.toLowerCase().endsWith('.xlsx'))
    throw new Error('.xlsx 파일만 지원합니다. 엑셀에서 "Excel 통합 문서(.xlsx)"로 저장해 주세요.');
  const buf = await file.arrayBuffer();
  try {
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(buf);
    if (wb.worksheets.length > 0) return wb;
  } catch { /* 비표준 파일 → 정규화 후 재시도 */ }
  try {
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(await normalizeXlsx(buf));
    return wb;
  } catch {
    throw new Error('엑셀을 읽지 못했습니다. 엑셀에서 파일을 열어 "다른 이름으로 저장(.xlsx)" 후 다시 시도해 주세요.');
  }
}

async function normalizeXlsx(buf: ArrayBuffer): Promise<ArrayBuffer> {
  const zip = await JSZip.loadAsync(buf);
  zip.remove('docProps/app.xml');   // 비표준 메타는 ExcelJS 파서를 깨뜨림 — 데이터와 무관하므로 제거
  zip.remove('docProps/core.xml');
  for (const name of Object.keys(zip.files)) {
    if (zip.files[name].dir) continue;
    if (!name.endsWith('.xml') && !name.endsWith('.rels')) continue;
    let xml = await zip.file(name)!.async('string');
    // 1) spreadsheetml 네임스페이스 접두사 제거 (<x:row> → <row>)
    const m = xml.match(/xmlns:([A-Za-z][\w.-]*)="http:\/\/schemas\.openxmlformats\.org\/spreadsheetml\/2006\/main"/);
    if (m) {
      const p = m[1];
      xml = xml.split(`<${p}:`).join('<').split(`</${p}:`).join('</').replace(m[0], `xmlns="${MAIN_NS}"`);
    }
    // 2) 절대경로 rel Target → 해당 rels 기준 상대경로
    if (name.endsWith('.rels')) {
      const base = name.substring(0, name.indexOf('_rels/'));
      xml = xml.replace(/Target="\/([^"]+)"/g, (_s, t: string) =>
        `Target="${base && t.startsWith(base) ? t.slice(base.length) : t}"`);
    }
    zip.file(name, xml);
  }
  return zip.generateAsync({ type: 'arraybuffer' });
}
