import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import type { EqCheckItem, EqCheckPeriod, EqCheckResult, EqCheckSheet, EqCycle } from '../../api/types';
import QrScanButton, { QrIcon } from '../../components/QrScan';
import { timeLabel } from '../checklist/common';
import { CYCLES, fieldsOf, optionsOf, rangeText, STATE_LABEL, STATE_TONE, todayYmd } from './eqCommon';
import '../checklist/Checklist.css';
import './EqCheck.css';

// 설비 호기 QR 을 찍으면 열리는 화면 — 휴대폰에서 쓴다(설비 점검표 AQ-C-13 Rev.7).
// 일상(하루 1회)·주간(금요일)은 생산팀, 월간(첫째 주 금요일)은 설비팀. 항목마다 누르는 즉시 저장한다.

type SaveBody = { value?: string; nums?: Record<string, number | null>; notRunning?: boolean; memo?: string };

export default function EqUnit() {
  const { code = '' } = useParams();
  const [params] = useSearchParams();
  const { user } = useAuth();
  const [sheet, setSheet] = useState<EqCheckSheet | null>(null);
  const [error, setError] = useState('');
  const [tab, setTab] = useState<EqCycle>(() => (CYCLES.includes(params.get('tab') as EqCycle) ? params.get('tab') as EqCycle : '일상'));
  const [busy, setBusy] = useState<Record<number, boolean>>({});
  const date = params.get('date');

  const load = useCallback(async () => {
    try {
      setSheet(await api.get<EqCheckSheet>(`/api/eqcheck/sheet/${encodeURIComponent(code)}${date ? `?date=${date}` : ''}`));
      setError('');
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) setError('403');
      else if (e instanceof ApiError && e.status === 401) setError('');
      else setError(e instanceof Error ? e.message : '점검표를 불러오지 못했습니다.');
    }
  }, [code, date]);
  useEffect(() => { load(); }, [load, user]);

  const period = sheet?.periods.find(p => p.cycle === tab) ?? null;

  function patchResult(p: EqCheckPeriod, itemId: number, r: EqCheckResult | null) {
    setSheet(s => s && {
      ...s,
      periods: s.periods.map(x => x.cycle !== p.cycle ? x
        : { ...x, results: [...x.results.filter(y => y.itemId !== itemId), ...(r ? [r] : [])] }),
    });
  }

  async function save(p: EqCheckPeriod, item: EqCheckItem, body: SaveBody) {
    if (!sheet) return;
    const prev = p.results.find(r => r.itemId === item.id);
    setBusy(b => ({ ...b, [item.id]: true }));
    try {
      const saved = await api.put<EqCheckResult | null>(`/api/eqcheck/sheet/${encodeURIComponent(sheet.unit.code)}/items/${item.id}`, {
        cycle: p.cycle, periodKey: p.periodKey,
        value: body.value ?? prev?.value ?? '', nums: body.nums ?? prev?.nums ?? {},
        notRunning: body.notRunning ?? prev?.notRunning ?? false, memo: body.memo ?? prev?.memo ?? '',
      });
      patchResult(p, item.id, saved);
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
      load();
    } finally {
      setBusy(b => ({ ...b, [item.id]: false }));
    }
  }

  async function allOk(p: EqCheckPeriod) {
    const left = p.items.filter(i => i.isActive && i.inputType === 'OXA' && !p.results.some(r => r.itemId === i.id));
    if (left.length === 0 || !confirm(`아직 적지 않은 ${left.length}개 항목을 모두 O(양호)로 적을까요?`)) return;
    for (const i of left) await save(p, i, { value: 'O' });
  }

  async function saveNote(p: EqCheckPeriod, note: string) {
    if (!sheet || note === p.note) return;
    try {
      await api.put(`/api/eqcheck/sheet/${encodeURIComponent(sheet.unit.code)}/note`, { cycle: p.cycle, periodKey: p.periodKey, note });
      setSheet(s => s && { ...s, periods: s.periods.map(x => x.cycle === p.cycle ? { ...x, note } : x) });
    } catch (e) { alert(e instanceof Error ? e.message : '특이사항을 저장하지 못했습니다.'); }
  }

  async function reportFault() {
    if (!sheet) return;
    const text = prompt(`${sheet.unit.code} 고장·부적합 내용을 적어 주세요.\n(미조치 NG 로 올라가 설비팀이 조치합니다)`)?.trim();
    if (!text) return;
    try {
      await api.post('/api/eqcheck/fault', { unitCode: sheet.unit.code, date: sheet.today, text, memo: '' });
      alert('등록했습니다. 체크시트(설비) → NG·고장 에서 조치 상황을 볼 수 있습니다.');
    } catch (e) { alert(e instanceof Error ? e.message : '등록하지 못했습니다.'); }
  }

  if (error === '403') {
    return (
      <div className="ck-zone">
        <div className="ck-error">
          <b>{user?.realName ?? '이 계정'}</b> 계정에는 체크시트(설비) 권한이 없습니다.<br />
          관리자에게 <b>사용자 계정 관리 → 권한</b>에서 <b>'체크시트 (설비)'를 '조회' 이상</b>으로 받은 뒤 QR 을 다시 찍어 주세요.
        </div>
      </div>
    );
  }
  if (error) {
    return (
      <div className="ck-zone">
        <div className="ck-error">{error}</div>
        <Link className="btn btn-ghost" to="/eq-check">체크시트(설비) 현황으로</Link>
      </div>
    );
  }
  if (!sheet || !period) return <div className="ck-zone"><div className="ck-empty">불러오는 중…</div></div>;

  const u = sheet.unit;
  return (
    <div className="ck-zone ec-unit">
      <header className="ck-zhead">
        <div className="ck-zline">
          <span>{u.line} · {u.templateName}</span>
          <QrScanButton className="ck-zscan"><span className="ck-scan-ico">{QrIcon}</span>다른 설비 QR</QrScanButton>
        </div>
        <h2>{u.code}{u.process && <small className="ec-proc">{u.process}</small>}</h2>
        <div className="ck-zmeta">
          <span>{period.label}</span>
          {date && date !== todayYmd() && <span className="ck-tag warn">지난 날짜 보기</span>}
        </div>
      </header>

      <div className="ck-tabs ec-tabs" role="tablist">
        {sheet.periods.map(p => {
          const done = countDone(p);
          const total = p.items.filter(i => i.isActive).length;
          const st = done >= total && total > 0 ? 'done' : p.state;
          return (
            <button key={p.cycle} role="tab" aria-selected={tab === p.cycle} className={`${tab === p.cycle ? 'on' : ''} c-${cycleClass(p.cycle)}`}
              onClick={() => setTab(p.cycle)}>
              {p.cycle}
              <span className={`ck-tabn ${STATE_TONE[st]}`}>{st === 'done' ? '✓' : `${done}/${total}`}</span>
            </button>
          );
        })}
      </div>

      <PeriodView key={period.cycle + period.periodKey} p={period} busy={busy} monthlyTeam={sheet.isMonthlyTeam}
        onSave={(i, b) => save(period, i, b)} onAllOk={() => allOk(period)} onNote={n => saveNote(period, n)} />

      <div className="ec-foot">
        <button className="btn btn-ghost" onClick={reportFault}>고장·부적합 알리기</button>
        <Link className="btn btn-ghost" to="/eq-check">현황</Link>
      </div>
    </div>
  );
}

const cycleClass = (c: string) => (c === '일상' ? 'd' : c === '주간' ? 'w' : 'm');
const countDone = (p: EqCheckPeriod) => {
  const active = new Set(p.items.filter(i => i.isActive).map(i => i.id));
  return p.results.filter(r => active.has(r.itemId) && r.judge).length;
};

function PeriodView({ p, busy, monthlyTeam, onSave, onAllOk, onNote }: {
  p: EqCheckPeriod; busy: Record<number, boolean>; monthlyTeam: boolean;
  onSave: (i: EqCheckItem, b: SaveBody) => void; onAllOk: () => void; onNote: (n: string) => void;
}) {
  const [note, setNote] = useState(p.note);
  const groups = useMemo(() => {
    const m = new Map<string, EqCheckItem[]>();
    for (const i of p.items) m.set(i.category || '기타', [...(m.get(i.category || '기타') ?? []), i]);
    return [...m.entries()];
  }, [p.items]);
  const readOnly = !p.canEdit;
  const hasOxaLeft = !readOnly && p.items.some(i => i.isActive && i.inputType === 'OXA' && !p.results.some(r => r.itemId === i.id));
  const who = p.cycle === '월간' ? '설비팀' : '생산팀';

  return (
    <div className={`ec-period c-${cycleClass(p.cycle)}`}>
      <div className="ec-phead">
        <span className={`ck-pill ${STATE_TONE[p.state]}`}>{STATE_LABEL[p.state]}</span>
        <span className="ec-who">{who} 점검</span>
        {p.cycle === '월간' && monthlyTeam && <span className="ck-tag okc">설비팀</span>}
        {hasOxaLeft && <button className="btn btn-ghost ec-allok" onClick={onAllOk}>남은 항목 모두 O</button>}
      </div>
      {readOnly && <div className="ck-banner">{p.reason || '입력할 수 없습니다.'}</div>}
      {p.items.length === 0 && <div className="ck-empty">이 설비는 {p.cycle} 점검 항목이 없습니다.</div>}
      {groups.map(([cat, items]) => (
        <section key={cat} className="ec-group">
          <h3>{cat}</h3>
          {items.map(i => (
            <ItemRow key={i.id} item={i} result={p.results.find(r => r.itemId === i.id) ?? null}
              readOnly={readOnly || !i.isActive} busy={!!busy[i.id]} onSave={b => onSave(i, b)} />
          ))}
        </section>
      ))}
      <div className="ec-note">
        <label>특이사항{p.cycle === '월간' ? ' (월간 점검표 특이사항 칸)' : ''}</label>
        <textarea className="input" rows={2} value={note} disabled={readOnly} placeholder={readOnly ? '' : '있을 때만 적습니다'}
          onChange={e => setNote(e.target.value)} onBlur={() => onNote(note.trim())} />
      </div>
    </div>
  );
}

function ItemRow({ item, result, readOnly, busy, onSave }: {
  item: EqCheckItem; result: EqCheckResult | null; readOnly: boolean; busy: boolean; onSave: (b: SaveBody) => void;
}) {
  const judge = result?.judge ?? '';
  const range = rangeText(item);
  function memo() {
    const m = prompt('메모(NG 내용·특이사항)', result?.memo ?? '');
    if (m === null) return;
    onSave({ memo: m.trim() });
  }
  return (
    <div className={`ec-item ${judge === 'NG' ? 'ng' : judge === 'OK' ? 'ok' : ''} ${busy ? 'busy' : ''}`}>
      <div className="ec-itext">
        <b>{item.name}{item.point && <em>{item.point}</em>}</b>
        {item.spec && <span className="ec-spec">{item.spec}</span>}
      </div>
      <div className="ec-input">
        {item.inputType === 'OXA' && (
          <div className="ec-seg oxa">
            {(['O', '△', 'X'] as const).map(v => (
              <button key={v} disabled={readOnly} className={`${result?.value === v ? 'on' : ''} v-${v === 'O' ? 'o' : v === 'X' ? 'x' : 't'}`}
                onClick={() => onSave({ value: result?.value === v ? '' : v })}>{v}</button>
            ))}
          </div>
        )}
        {item.inputType === 'CHOICE' && (
          <div className="ec-seg">
            {optionsOf(item).map((o, n) => (
              <button key={o.text} disabled={readOnly}
                className={`${result?.value === o.text ? 'on' : ''} ${n === 0 ? 'v-o' : o.action ? 'v-a' : 'v-x'}`}
                title={o.action ? '조치함 — NG 로 남고 바로 조치 완료로 닫힙니다' : undefined}
                onClick={() => onSave({ value: result?.value === o.text ? '' : o.text })}>{o.text}</button>
            ))}
          </div>
        )}
        {(item.inputType === 'NUM' || item.inputType === 'MULTI') && (
          <NumInputs item={item} result={result} readOnly={readOnly} onSave={onSave} />
        )}
        {range && <span className="ec-range">기준 {range}</span>}
      </div>
      {result && (
        <div className="ec-meta">
          {judge && <span className={`ck-pill ${judge === 'NG' ? 'bad' : 'ok'}`}>{judge}</span>}
          {result.ngStatus === 'OPEN' && <span className="ck-tag bad">미조치</span>}
          {result.ngStatus === 'DONE' && <span className="ck-tag okc" title={result.ngCloseNote}>조치 완료</span>}
          <span className="ck-dim">{result.checkedByName} {timeLabel(result.checkedAt)}</span>
          {result.memo && <span className="ec-memo">메모: {result.memo}</span>}
          {!readOnly && <button className="ec-link" onClick={memo}>{result.memo ? '메모 수정' : '메모'}</button>}
        </div>
      )}
    </div>
  );
}

function NumInputs({ item, result, readOnly, onSave }: {
  item: EqCheckItem; result: EqCheckResult | null; readOnly: boolean; onSave: (b: SaveBody) => void;
}) {
  const fields = item.inputType === 'NUM' ? [''] : fieldsOf(item);
  const init = () => Object.fromEntries(fields.map(f => [f, result?.nums?.[f] != null ? String(result.nums[f]) : '']));
  const [vals, setVals] = useState<Record<string, string>>(init);
  // 저장 뒤 서버 값으로 맞춘다
  const key = JSON.stringify(result?.nums ?? {});
  useEffect(() => { setVals(init()); }, [key]); // eslint-disable-line react-hooks/exhaustive-deps

  function commit(next: Record<string, string>) {
    const nums: Record<string, number | null> = {};
    let changed = false;
    for (const f of fields) {
      const s = (next[f] ?? '').trim().replace(',', '.');
      const v = s === '' ? null : Number(s);
      if (v !== null && Number.isNaN(v)) { alert('숫자를 입력하세요.'); return; }
      nums[f] = v;
      if ((result?.nums?.[f] ?? null) !== v) changed = true;
    }
    if (changed || result?.notRunning) onSave({ nums, notRunning: false });
  }
  const notRunning = !!result?.notRunning;
  return (
    <div className="ec-nums">
      {!notRunning && fields.map((f, n) => (
        <label key={f || 'v'} className="ec-num">
          {f && <span>{f}</span>}
          <input className="input" inputMode="decimal" disabled={readOnly} value={vals[f] ?? ''}
            onChange={e => setVals(v => ({ ...v, [f]: e.target.value }))}
            onBlur={() => commit(vals)}
            onKeyDown={e => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
            // 두 번째 칸부터 — 첫 칸 값을 미리 채워 두면 편차만 고쳐 적으면 된다
            onFocus={() => { if (n > 0 && !vals[f] && vals[fields[0]]) setVals(v => ({ ...v, [f]: v[fields[0]] })); }} />
          {item.unit && <i>{item.unit}</i>}
        </label>
      ))}
      {item.runOnly && (
        <button className={`ec-idle ${notRunning ? 'on' : ''}`} disabled={readOnly}
          onClick={() => onSave(notRunning ? { notRunning: false, nums: {} } : { notRunning: true, nums: {} })}>
          {notRunning ? '비가동 ✓' : '비가동'}
        </button>
      )}
    </div>
  );
}
