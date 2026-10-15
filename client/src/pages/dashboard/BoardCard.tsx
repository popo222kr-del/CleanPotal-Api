import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { ScheduleBlock, ScheduleEquipment } from '../../api/types';

// 대시보드 '생산 스케줄 보드' — 스케줄 보드(07:00 ~ 다음날 07:00)를 읽기 전용으로 작게. 설비마다 한 줄,
// 블록은 S2·HF·DI 색으로 나눠 그리고, 지금 시각에 빨간 세로선. 07:00 전이면 어제 날짜 보드가 '지금' 보드다.

const START_HOUR = 7;
const TOTAL = 24 * 60;
const COL = { s2: '#E08B86', hf: '#E3C069', di: '#7FB6D9' };
const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** 지금 보고 있어야 할 보드 날짜와 그 보드 안의 지금 위치(분) */
function boardNow() {
  const n = new Date();
  const d = new Date(n);
  if (n.getHours() < START_HOUR) d.setDate(d.getDate() - 1);
  const min = ((n.getHours() - START_HOUR + 24) % 24) * 60 + n.getMinutes();
  return { date: ymd(d), min };
}

export interface BoardState { date: string; equips: ScheduleEquipment[]; blocks: ScheduleBlock[]; loaded: boolean; failed: boolean }

export function useBoardToday(enabled: boolean): BoardState {
  const [s, setS] = useState<BoardState>({ date: boardNow().date, equips: [], blocks: [], loaded: false, failed: false });
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    const date = boardNow().date;
    Promise.all([
      api.get<ScheduleEquipment[]>('/api/scheduleboard/equipments'),
      api.get<ScheduleBlock[]>(`/api/scheduleboard/day?date=${date}`),
    ]).then(([equips, blocks]) => { if (alive) setS({ date, equips, blocks, loaded: true, failed: false }); })
      .catch(() => { if (alive) setS(x => ({ ...x, loaded: true, failed: true })); });
    return () => { alive = false; };
  }, [enabled]);
  return s;
}

/** 한 블록을 링(1440분) 위의 [시작, 길이, 색] 조각들로 — 다음날 07시를 넘으면 앞으로 돌아간다 */
function pieces(b: ScheduleBlock): { s: number; l: number; c: string }[] {
  const out: { s: number; l: number; c: string }[] = [];
  let at = b.startMinute;
  for (const [len, c] of [[b.s2Minutes, COL.s2], [b.hfMinutes, COL.hf], [b.diMinutes, COL.di]] as [number, string][]) {
    let left = len;
    while (left > 0) {
      const s = at % TOTAL, l = Math.min(left, TOTAL - s);
      out.push({ s, l, c }); at += l; left -= l;
    }
  }
  return out;
}
const hm = (m: number) => { const a = START_HOUR * 60 + m; return `${String(Math.floor(a / 60) % 24).padStart(2, '0')}:${String(a % 60).padStart(2, '0')}`; };

export default function BoardBody({ state }: { state: BoardState }) {
  const [now, setNow] = useState(boardNow());
  useEffect(() => { const t = window.setInterval(() => setNow(boardNow()), 60_000); return () => window.clearInterval(t); }, []);
  const { equips, blocks, loaded, failed } = state;
  if (!loaded) return <p className="db-empty">불러오는 중…</p>;
  if (failed) return <p className="db-empty">스케줄 보드를 불러오지 못했습니다.</p>;
  if (equips.length === 0) return <p className="db-empty">보드에 올린 설비가 없습니다.</p>;
  const byEq = new Map<number, ScheduleBlock[]>();
  blocks.forEach(b => byEq.set(b.equipmentIndex, [...(byEq.get(b.equipmentIndex) ?? []), b]));
  const pct = (m: number) => `${(m / TOTAL) * 100}%`;
  const showNow = now.date === state.date;
  const running = (b: ScheduleBlock) => {
    const len = b.s2Minutes + b.hfMinutes + b.diMinutes;
    const rel = (now.min - b.startMinute + TOTAL) % TOTAL;
    return showNow && rel < len;
  };
  const busy = equips.filter(e => (byEq.get(e.index) ?? []).some(running)).length;
  let lastGroup = '';
  return (
    <div className="db-sb">
      <div className="db-sb-sum">
        작업 <b>{blocks.length}</b>건 · 잡힌 설비 <b>{byEq.size}</b>/{equips.length}
        {showNow && <> · 지금 가동 <b className="run">{busy}</b>대</>}
        <span className="db-sb-key"><i style={{ background: COL.s2 }} />S2<i style={{ background: COL.hf }} />HF<i style={{ background: COL.di }} />DI</span>
      </div>
      <div className="db-sb-grid">
        <div className="db-sb-row head">
          <span className="db-sb-name" />
          <div className="db-sb-track">
            {Array.from({ length: 13 }, (_, i) => i * 120).map(m => (
              <span key={m} className="db-sb-tick" style={{ left: pct(m) }}>{m < TOTAL ? hm(m).slice(0, 2) : ''}</span>
            ))}
          </div>
        </div>
        {equips.map(e => {
          const list = byEq.get(e.index) ?? [];
          const g = e.groupName && e.groupName !== lastGroup ? e.groupName : '';
          if (e.groupName) lastGroup = e.groupName;
          return (
            <div key={e.index} className={`db-sb-row${g ? ' g' : ''}${e.isIdle ? ' idle' : ''}`}>
              <span className="db-sb-name" title={[e.displayName, e.process, e.note].filter(Boolean).join(' · ')}>
                {e.displayName}{e.isIdle && <small> 유휴</small>}
              </span>
              <div className="db-sb-track">
                {Array.from({ length: 11 }, (_, i) => (i + 1) * 120).map(m => <i key={m} className="db-sb-grid-l" style={{ left: pct(m) }} />)}
                {list.map(b => pieces(b).map((p, i) => (
                  <span key={`${b.id}-${i}`} className={`db-sb-blk${running(b) ? ' run' : ''}`} style={{ left: pct(p.s), width: pct(p.l), background: p.c }}
                    title={`${b.recipeText.split('@')[0]} · ${hm(b.startMinute)}~${hm((b.startMinute + b.s2Minutes + b.hfMinutes + b.diMinutes) % TOTAL)}`} />
                )))}
                {showNow && <i className="db-sb-now" style={{ left: pct(now.min) }} />}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
