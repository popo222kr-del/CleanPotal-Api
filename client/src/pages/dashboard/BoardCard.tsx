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

const hm = (m: number) => { const a = START_HOUR * 60 + m; return `${String(Math.floor(a / 60) % 24).padStart(2, '0')}:${String(a % 60).padStart(2, '0')}`; };

/**
 * 대시보드용 — 지금 시각에 돌고 있는 설비만. 설비마다 레시피·지금 단계(S2/HF/DI)·진행 막대·남은 시간·끝나는 시각.
 * 돌고 있는 설비가 없으면 다음에 시작할 작업 몇 건을 보인다. 전체 타임라인은 [보드 열기].
 */
export default function BoardBody({ state }: { state: BoardState }) {
  const [now, setNow] = useState(boardNow());
  useEffect(() => { const t = window.setInterval(() => setNow(boardNow()), 60_000); return () => window.clearInterval(t); }, []);
  const { equips, blocks, loaded, failed } = state;
  if (!loaded) return <p className="db-empty">불러오는 중…</p>;
  if (failed) return <p className="db-empty">스케줄 보드를 불러오지 못했습니다.</p>;
  const isNow = now.date === state.date;
  const eqOf = new Map(equips.map(e => [e.index, e]));
  const len = (b: ScheduleBlock) => b.s2Minutes + b.hfMinutes + b.diMinutes;
  const rel = (b: ScheduleBlock) => (now.min - b.startMinute + TOTAL) % TOTAL;   // 시작 뒤 지난 분
  const running = isNow ? blocks.filter(b => eqOf.has(b.equipmentIndex) && rel(b) < len(b))
    .sort((a, b) => (eqOf.get(a.equipmentIndex)!.orderIndex - eqOf.get(b.equipmentIndex)!.orderIndex) || a.equipmentIndex - b.equipmentIndex) : [];
  const upcoming = isNow ? blocks.filter(b => eqOf.has(b.equipmentIndex) && b.startMinute > now.min)
    .sort((a, b) => a.startMinute - b.startMinute).slice(0, 5) : [];
  const busy = new Set(running.map(b => b.equipmentIndex)).size;
  const name = (b: ScheduleBlock) => eqOf.get(b.equipmentIndex)?.displayName ?? `#${b.equipmentIndex}`;
  const recipe = (b: ScheduleBlock) => b.recipeText.split('@')[0];
  const dur = (m: number) => (m >= 60 ? `${Math.floor(m / 60)}시간${m % 60 ? ` ${m % 60}분` : ''}` : `${m}분`);
  return (
    <div className="db-sb">
      <div className="db-sb-sum">
        <span>지금 <b>{hm(now.min)}</b> · 가동 <b className="run">{busy}</b>/{equips.length}대 · 오늘 작업 <b>{blocks.length}</b>건</span>
        <span className="db-sb-key"><i style={{ background: COL.s2 }} />S2<i style={{ background: COL.hf }} />HF<i style={{ background: COL.di }} />DI</span>
      </div>
      {running.length === 0 && <p className="db-empty">지금 진행 중인 설비가 없습니다.</p>}
      {running.map(b => {
        const r = rel(b), L = len(b);
        const step = r < b.s2Minutes ? 'S2' : r < b.s2Minutes + b.hfMinutes ? 'HF' : 'DI';
        const stepCol = step === 'S2' ? COL.s2 : step === 'HF' ? COL.hf : COL.di;
        return (
          <div key={b.id} className="db-sb-run">
            <div className="db-sb-run-h">
              <b className="eq">{name(b)}</b>
              <span className="rc" title={b.recipeText}>{recipe(b)}</span>
              <span className="st" style={{ background: stepCol }}>{step}</span>
              <span className="left">남은 {dur(L - r)} · {hm((b.startMinute + L) % TOTAL)} 끝</span>
            </div>
            <div className="db-sb-bar" title={`${hm(b.startMinute)} ~ ${hm((b.startMinute + L) % TOTAL)}`}>
              <i style={{ width: `${(b.s2Minutes / L) * 100}%`, background: COL.s2 }} />
              <i style={{ width: `${(b.hfMinutes / L) * 100}%`, background: COL.hf }} />
              <i style={{ width: `${(b.diMinutes / L) * 100}%`, background: COL.di }} />
              {/* 지난 부분은 진하게 — 같은 막대를 지난 만큼만 잘라 위에 겹친다 */}
              {r > 0 && (
                <span className="done" style={{ width: `${(r / L) * 100}%` }}>
                  <span style={{ width: `${(L / r) * 100}%` }}>
                    <i style={{ width: `${(b.s2Minutes / L) * 100}%`, background: COL.s2 }} />
                    <i style={{ width: `${(b.hfMinutes / L) * 100}%`, background: COL.hf }} />
                    <i style={{ width: `${(b.diMinutes / L) * 100}%`, background: COL.di }} />
                  </span>
                </span>
              )}
              <span className="pin" style={{ left: `${(r / L) * 100}%` }} />
            </div>
          </div>
        );
      })}
      {running.length === 0 && upcoming.length > 0 && (
        <div className="db-sb-next">
          <div className="t">다음 작업</div>
          {upcoming.map(b => (
            <div key={b.id} className="db-sb-next-r"><b>{hm(b.startMinute)}</b><span className="eq">{name(b)}</span><span className="rc">{recipe(b)}</span><span className="d">{dur(len(b))}</span></div>
          ))}
        </div>
      )}
    </div>
  );
}
