// 업무 파일 통합 관리 화면들이 같이 쓰는 것.

/** 약액 교체 내용의 색 — 엑셀 규칙 그대로(S2 100% 노랑, S2 50% 초록, 그 밖 파랑). */
export function contentTone(content: string): string {
  const c = content.replace(/\s+/g, '').toUpperCase();
  if (/S2100%/.test(c)) return 'full';
  if (/S250%/.test(c)) return 'half';
  return c ? 'other' : '';
}

/** 설비 순서 — 라인(METAL → N-METAL) 먼저, 그 안에서는 설비 목록 순서. 나중에 추가된 설비가 다른 라인 뒤로 가지 않게. */
export function sortByLine<T extends { line: string; sortOrder?: number }>(list: T[], order?: (x: T) => number): T[] {
  const lineRank = (l: string) => (l === 'METAL' ? 0 : l === 'N-METAL' ? 1 : 2);
  return list.map((x, i) => ({ x, i })).sort((a, b) =>
    lineRank(a.x.line) - lineRank(b.x.line) || (order ? order(a.x) - order(b.x) : (a.x.sortOrder ?? a.i) - (b.x.sortOrder ?? b.i)) || a.i - b.i)
    .map(v => v.x);
}
