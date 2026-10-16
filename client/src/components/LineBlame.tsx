import './LineBlame.css';

// 생산팀 인수인계 — 줄(문단)마다 쓴 사람 이름표. 서버가 저장 때 줄별로 남긴 이름(lineWriters)을 쓰고,
// 아직 저장 안 된 지금 입력은 마지막 저장본과 줄을 맞춰 보고 새로 쓰거나 고친 줄을 '나'로 본다(서버와 같은 방식).

const split = (s: string | null | undefined) => (s ?? '').replace(/\r\n/g, '\n').split('\n');

/** 마지막 저장본(글·줄별 이름) → 지금 글의 줄별 이름 */
export function blameLines(savedText: string, savedBy: string[] | null | undefined, text: string, me: string): string[] {
  const a = split(savedText), b = split(text);
  const res = b.map(l => (l.trim() ? me : ''));
  if (!savedBy || savedBy.length !== a.length) return savedText === text ? b.map(() => '') : res;
  const n = a.length, m = b.length;
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
  for (let i = 0, j = 0; i < n && j < m;) {
    if (a[i] === b[j]) { if (b[j].trim()) res[j] = savedBy[i]; i++; j++; }
    else if (dp[i + 1][j] >= dp[i][j + 1]) i++;
    else j++;
  }
  return res;
}

/** 이름표를 붙일 줄 — 문단(빈 줄로 나뉨)의 첫 줄이거나 앞 줄과 쓴 사람이 다를 때 */
function pillAt(lines: string[], by: string[], i: number) {
  const n = by[i];
  return !!n && !!lines[i].trim() && (i === 0 || !lines[i - 1].trim() || by[i - 1] !== n);
}

/** 입력칸 위에 겹치는 투명 층 — 글자는 안 보이고 줄 오른쪽 여백에 이름표만 보인다(입력칸과 같은 글꼴·줄바꿈) */
export function BlameOverlay({ text, by, className = '' }: { text: string; by: string[]; className?: string }) {
  const lines = split(text);
  return (
    <div className={`bl-ov ${className}`} aria-hidden>
      {lines.map((l, i) => (
        <div key={i} className="bl-line">
          {l || '​'}
          {pillAt(lines, by, i) && <span className="bl-pill">{by[i]}</span>}
        </div>
      ))}
    </div>
  );
}

/** 읽기 전용 — 글과 이름표를 함께 (대시보드) */
export function BlameText({ text, by }: { text: string; by: string[] }) {
  const lines = split(text);
  return (
    <div className="bl-ro">
      {lines.map((l, i) => (
        <div key={i} className="bl-ro-line">
          <span className="bl-ro-t">{l || '​'}</span>
          {pillAt(lines, by, i) && <span className="bl-pill">{by[i]}</span>}
        </div>
      ))}
    </div>
  );
}
