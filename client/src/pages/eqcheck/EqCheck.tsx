import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import type { EqCheckCell, EqCheckNg, EqCheckStatus, EqCheckUnit } from '../../api/types';
import QrScanButton, { QrIcon } from '../../components/QrScan';
import { isTouchDevice } from '../../hooks/useIsMobile';
import { dayLabel, timeLabel, ymdOf } from '../checklist/common';
import { cl, md, STATE_LABEL, todayYmd } from './eqCommon';
import EqMonth from './EqMonth';
import EqAdmin from './EqAdmin';
import '../checklist/Checklist.css';
import './EqCheck.css';

// 체크시트 (설비) — 사무실에서 보는 곳. 현장은 설비 호기 QR 로 /e/호기 화면에 바로 들어온다.
// 설비 점검표 AQ-C-13 Rev.7: 매일(하루 1회)·주간(금요일)은 생산팀, 월간(그 달 안에)은 설비팀.
type Tab = 'status' | 'ng' | 'month' | 'admin';

export default function EqCheck() {
  const acc = useAccess();
  const [params] = useSearchParams();
  const [tab, setTab] = useState<Tab>(() => (['ng', 'month'].includes(params.get('tab') ?? '') ? params.get('tab') as Tab : 'status'));
  const tabs: [Tab, string][] = [['status', '점검 현황'], ['ng', 'NG·고장'], ['month', '월간 점검표']];
  if (acc.isAdmin) tabs.push(['admin', '양식 관리']);

  return (
    <div className="ck-page">
      <header className="ck-head ck-noprint">
        <div className="ck-head-title">
          <h2>체크시트 (설비)</h2>
          <span>설비 점검표 AQ-C-13 · 매일·주간 생산팀 / 월간 설비팀</span>
          <QrScanButton className="ck-qr" title="QR 스캔">{QrIcon}</QrScanButton>
        </div>
        <nav className="ck-nav">
          {tabs.map(([t, l]) => <button key={t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{l}</button>)}
        </nav>
      </header>
      <div className="ck-body">
        {tab === 'status' && <StatusTab onOpenNg={() => setTab('ng')} />}
        {tab === 'ng' && <NgTab />}
        {tab === 'month' && <EqMonth initialUnit={params.get('unit') ?? ''} />}
        {tab === 'admin' && acc.isAdmin && <EqAdmin />}
      </div>
    </div>
  );
}

// 점검 현황 — 체크시트(현장) 현황과 같은 모양: 날짜 넘기기 · 요약 칸 · 라인별 표 · 칸(상태 점·막대·n/전체·NG).
const SC_CLASS: Record<string, string> = { done: 'submitted', partial: 'progress', due: 'progress', late: 'late', todo: 'none', none: 'none' };

function shiftDate(ymd: string, days: number) {
  const d = new Date(`${ymd}T00:00:00`);
  d.setDate(d.getDate() + days);
  return ymdOf(d);
}

function Cell({ c, onClick, locked }: { c: EqCheckCell; onClick: () => void; locked: boolean }) {
  if (c.state === 'none') return <span className="ck-muted">—</span>;
  const pct = c.total ? Math.round((c.done / c.total) * 100) : 0;
  return (
    // 현장에서 설비 QR 을 찍어야만 점검한다 — 자리에서 목록을 눌러 체크하지 못하게(locked)
    <button className={`ck-sc ${SC_CLASS[c.state]}`} onClick={onClick} disabled={locked}
      title={locked ? '현장에서 설비 QR 을 찍어 점검합니다' : '눌러서 점검 화면 열기'}>
      <span className="ck-sc-state"><i />{STATE_LABEL[c.state]}</span>
      <span className="ck-sc-bar"><i style={{ width: `${pct}%` }} /></span>
      <span className="ck-sc-num">{c.done}/{c.total}</span>
      {c.ng > 0 && <span className="ck-pill bad">NG {c.ng}</span>}
      <span className="ck-sc-who">{c.by}</span>
    </button>
  );
}

function StatusTab({ onOpenNg }: { onOpenNg: () => void }) {
  const nav = useNavigate();
  // 처음에는 서버의 근무일(07시 전은 전날)로 — 새벽에 브라우저 날짜와 하루 어긋나지 않게
  const [date, setDate] = useState('');
  const [workDay, setWorkDay] = useState('');
  const [data, setData] = useState<EqCheckStatus | null>(null);

  const load = useCallback(async () => {
    try {
      const d = await api.get<EqCheckStatus>(`/api/eqcheck/status${date ? `?date=${date}` : ''}`);
      setData(d);
      if (!date) setWorkDay(d.date);
    } catch (e) { alert(e instanceof Error ? e.message : '현황을 불러오지 못했습니다.'); }
  }, [date]);
  useEffect(() => { load(); }, [load]);

  if (!data) return <div className="ck-empty">불러오는 중…</div>;
  const s = data;
  const today = workDay || s.date;
  const isToday = s.date === today;
  const lines = [...new Set(s.rows.map(r => r.line))];
  const count = (k: 'daily' | 'weekly' | 'monthly') => {
    const t = s.rows.filter(r => r[k].state !== 'none');
    return { done: t.filter(r => r[k].state === 'done').length, total: t.length, late: t.filter(r => r[k].state === 'late').length };
  };
  const d = count('daily'), w = count('weekly'), m = count('monthly');
  const openNg = s.rows.reduce((n, r) => n + r.openNg, 0);
  // 폰·태블릿은 누구나, PC 는 조회 등급(생산 작업자)이 칸을 눌러 들어가지 못한다. 편집 등급·설비팀·관리자는 된다.
  const locked = isTouchDevice() || !s.canOpenOffQr;
  const open = (code: string, tab: string) =>
    nav(`/e/${encodeURIComponent(code)}?from=hub&tab=${encodeURIComponent(tab)}${isToday ? '' : `&date=${s.date}`}`);

  return (
    <div>
      <div className="ck-toolbar">
        <div className="ck-datenav">
          <button onClick={() => setDate(shiftDate(s.date, -1))} aria-label="전날">‹</button>
          <input type="date" value={s.date} onChange={e => e.target.value && setDate(e.target.value)} />
          <button onClick={() => setDate(shiftDate(s.date, 1))} aria-label="다음날">›</button>
        </div>
        {!isToday && <button className="ck-link" onClick={() => setDate('')}>오늘로</button>}
        <span className="ck-now">지금 <b>{dayLabel(today)}</b></span>
        <button className="ck-iconbtn" onClick={load} title="새로고침">↻</button>
      </div>

      <div className="ck-kpis">
        <div className={`ck-kpi ${d.late ? 'warn' : ''}`}>
          <span>매일 완료 ({dayLabel(s.date)})</span>
          <b>{d.done}<small>/{d.total} 설비</small></b>
        </div>
        <div className={`ck-kpi ${w.late ? 'warn' : ''}`}>
          <span>주간 완료 (기한 {md(s.weekDue)} 금 09시)</span>
          <b>{w.done}<small>/{w.total} 설비{w.late ? ` · 지연 ${w.late}` : ''}</small></b>
        </div>
        <div className={`ck-kpi ${m.late ? 'warn' : ''}`}>
          <span>월간 완료 ({Number(s.monthKey.slice(5))}월 안에)</span>
          <b>{m.done}<small>/{m.total} 설비{m.late ? ` · 지연 ${m.late}` : ''}</small></b>
        </div>
        <div className="ck-kpi">
          <span>이번 달 매일 점검</span>
          <b>{s.rows.length ? Math.round(s.rows.reduce((n, r) => n + r.dailyDoneDays, 0) / (s.rows.length * Math.max(1, s.daysElapsed)) * 100) : 0}<small>% · {s.daysElapsed}일 기준</small></b>
        </div>
        <button className={`ck-kpi link ${openNg ? 'bad' : ''}`} onClick={onOpenNg}>
          <span>미조치 NG</span>
          <b>{openNg}<small>건</small></b>
        </button>
      </div>

      {lines.length === 0 && <div className="ck-empty">등록된 설비가 없습니다. 양식 관리에서 호기를 넣어 주세요.</div>}
      {lines.map(l => {
        const rows = s.rows.filter(r => r.line === l);
        return (
          <section key={l} className="ck-panel">
            <div className="ck-panel-head">
              <b>{l}</b>
              <span className="ck-muted">{rows.length}대</span>
            </div>
            <table className="ck-table ck-status ec-status">
              <colgroup><col className="c-zone" /><col /><col /><col /><col className="c-month" /></colgroup>
              <thead>
                <tr>
                  <th>설비</th>
                  <th className={isToday ? 'now' : ''}>매일{isToday && <em>오늘</em>}</th>
                  <th>주간<em className="due">~{md(s.weekDue)} 금</em></th>
                  <th>월간<em className="due">~{md(s.monthDue)}</em></th>
                  <th>이번 달 매일</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.unitCode}>
                    <td><span className="ck-zname">{r.unitCode}</span><span className="ck-zcode">{r.process || r.templateName}</span></td>
                    <td><Cell c={r.daily} locked={locked} onClick={() => open(r.unitCode, '일상')} /></td>
                    <td><Cell c={r.weekly} locked={locked} onClick={() => open(r.unitCode, '주간')} /></td>
                    <td><Cell c={r.monthly} locked={locked} onClick={() => open(r.unitCode, '월간')} /></td>
                    <td className="ec-days">
                      <span className={r.dailyDoneDays < s.daysElapsed ? 'short' : ''}>{r.dailyDoneDays}</span>/{s.daysElapsed}일
                      {r.openNg > 0 && <span className="ck-pill bad">미조치 {r.openNg}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        );
      })}
    </div>
  );
}

function NgTab() {
  const [openOnly, setOpenOnly] = useState(true);
  const [line, setLine] = useState('');
  const [rows, setRows] = useState<EqCheckNg[] | null>(null);
  const [units, setUnits] = useState<EqCheckUnit[]>([]);
  const [err, setErr] = useState('');

  const load = useCallback(async () => {
    try {
      setRows(await api.get<EqCheckNg[]>(`/api/eqcheck/ng?open=${openOnly}${line ? `&line=${encodeURIComponent(line)}` : ''}`));
      setErr('');
    } catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
  }, [openOnly, line]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { api.get<EqCheckUnit[]>('/api/eqcheck/units').then(setUnits).catch(() => {}); }, []);

  async function close(r: EqCheckNg) {
    const note = prompt(`${r.unitCode} · ${r.name}\n조치 내용 및 결과를 적어 주세요.`)?.trim();
    if (!note) return;
    try { await api.put(`/api/eqcheck/ng/${r.id}/close`, { note }); load(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }

  async function addFault() {
    const code = prompt(`설비 호기 코드 (예: NDC01)\n${units.filter(u => u.isActive).map(u => u.code).join(', ')}`)?.trim().toUpperCase();
    if (!code) return;
    const text = prompt(`${code} 고장·부적합 내용`)?.trim();
    if (!text) return;
    try { await api.post('/api/eqcheck/fault', { unitCode: code, date: todayYmd(), text, memo: '' }); load(); }
    catch (e) { alert(e instanceof Error ? e.message : '등록하지 못했습니다.'); }
  }

  const lines = [...new Set(units.map(u => u.line))];
  const openCount = rows?.filter(n => n.ngStatus === 'OPEN').length ?? 0;
  return (
    <div>
      <div className="ck-toolbar">
        <div className="ck-seg">
          <button className={openOnly ? 'on' : ''} onClick={() => setOpenOnly(true)}>미조치</button>
          <button className={!openOnly ? 'on' : ''} onClick={() => setOpenOnly(false)}>전체</button>
        </div>
        {lines.length > 1 && (
          <div className="ck-seg">
            <button className={!line ? 'on' : ''} onClick={() => setLine('')}>전체 라인</button>
            {lines.map(l => <button key={l} className={line === l ? 'on' : ''} onClick={() => setLine(l)}>{l}</button>)}
          </div>
        )}
        {rows && <span className="ck-muted">{openOnly ? `${rows.length}건` : `${rows.length}건 (미조치 ${openCount})`}</span>}
        <button className="ck-iconbtn" onClick={load} title="새로고침">↻</button>
        <div className="ck-toolbar-right">
          <button className="ck-btn-sm" onClick={addFault}>+ 고장·부적합 등록</button>
        </div>
      </div>
      {err && <div className="ck-error">{err}</div>}

      <section className="ck-panel">
        {!rows ? <div className="ck-empty">불러오는 중…</div>
          : rows.length === 0 ? <div className="ck-empty">{openOnly ? '미조치 NG 가 없습니다.' : 'NG 기록이 없습니다.'}</div>
          : (
            <table className="ck-table ck-ngtable">
              <thead>
                <tr><th>상태</th><th>발생</th><th>설비</th><th>항목</th><th>값 · 메모</th><th>점검자</th><th>조치</th></tr>
              </thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.id} className={r.ngStatus === 'DONE' ? 'done' : ''}>
                    <td>{r.ngStatus === 'DONE' ? <span className="ck-pill ok">조치 완료</span> : <span className="ck-pill bad">미조치</span>}</td>
                    <td className="nowrap">{dayLabel(r.checkedAt.slice(0, 10))} <span className="ck-muted">{cl(r.cycle)}</span></td>
                    <td className="nowrap"><span className="ck-zname">{r.unitCode}</span><span className="ck-zcode">{r.line}</span></td>
                    <td>{r.category && <span className="ck-code">{r.category}</span>} {r.name}{r.point && <span className="ck-muted"> {r.point}</span>}
                      {r.spec && <div className="ck-sub">기준 {r.spec}</div>}</td>
                    <td>{r.valueText || (r.memo ? '' : <span className="ck-muted">—</span>)}
                      {r.memo && <div className="ck-sub">{r.memo}</div>}</td>
                    <td className="nowrap">{r.checkedByName}<div className="ck-sub">{timeLabel(r.checkedAt)}</div></td>
                    <td>
                      {r.ngStatus === 'DONE'
                        ? <>{r.ngCloseNote}<div className="ck-sub">{r.ngClosedBy} · {timeLabel(r.ngClosedAt)}</div></>
                        /* 조치 완료는 편집 등급·설비팀만 — 서버가 확인하고 안 되면 이유를 알려 준다 */
                        : <button className="ck-btn-sm primary" onClick={() => close(r)}>조치 완료</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>
    </div>
  );
}
