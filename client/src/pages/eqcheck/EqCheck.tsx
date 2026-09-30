import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import type { EqCheckCell, EqCheckNg, EqCheckStatus, EqCheckStatusRow, EqCheckUnit } from '../../api/types';
import QrScanButton, { QrIcon } from '../../components/QrScan';
import { timeLabel } from '../checklist/common';
import { md, STATE_LABEL, STATE_TONE, todayYmd } from './eqCommon';
import EqMonth from './EqMonth';
import EqAdmin from './EqAdmin';
import '../checklist/Checklist.css';
import './EqCheck.css';

// 체크시트 (설비) — 사무실에서 보는 곳. 현장은 설비 호기 QR 로 /e/호기 화면에 바로 들어온다.
// 설비 점검표 AQ-C-13 Rev.7: 일상(하루 1회)·주간(금요일)은 생산팀, 월간(첫째 주 금요일)은 설비팀.
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
          <span>설비 점검표 AQ-C-13 · 일상·주간 생산팀 / 월간 설비팀</span>
          <QrScanButton className="btn btn-primary ck-scan"><span className="ck-scan-ico">{QrIcon}</span>QR 스캔</QrScanButton>
        </div>
        <nav className="ck-nav">
          {tabs.map(([t, l]) => <button key={t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{l}</button>)}
        </nav>
      </header>
      <div className="ck-body">
        {tab === 'status' && <StatusTab />}
        {tab === 'ng' && <NgTab />}
        {tab === 'month' && <EqMonth initialUnit={params.get('unit') ?? ''} />}
        {tab === 'admin' && acc.isAdmin && <EqAdmin />}
      </div>
    </div>
  );
}

function Cell({ c, onClick, extra }: { c: EqCheckCell; onClick: () => void; extra?: string }) {
  if (c.state === 'none') return <span className="ck-dim">—</span>;
  const text = c.state === 'done' ? '완료' : `${STATE_LABEL[c.state]} ${c.done}/${c.total}`;
  return (
    <button className="ec-cell" onClick={onClick} title={c.by ? `마지막 입력: ${c.by}` : undefined}>
      <span className={`ck-pill ${STATE_TONE[c.state]}`}>{text}</span>
      {c.ng > 0 && <span className="ck-pill bad">NG {c.ng}</span>}
      {extra && <span className="ck-dim">{extra}</span>}
    </button>
  );
}

function StatusTab() {
  const nav = useNavigate();
  // 처음에는 서버의 근무일(07시 전은 전날)로 — 새벽에 브라우저 날짜와 하루 어긋나지 않게
  const [date, setDate] = useState('');
  const [workDay, setWorkDay] = useState('');
  const [data, setData] = useState<EqCheckStatus | null>(null);
  const [err, setErr] = useState('');
  const [line, setLine] = useState('');

  useEffect(() => {
    let alive = true;
    api.get<EqCheckStatus>(`/api/eqcheck/status${date ? `?date=${date}` : ''}`)
      .then(d => { if (alive) { setData(d); setErr(''); if (!date) { setDate(d.date); setWorkDay(d.date); } } })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); });
    return () => { alive = false; };
  }, [date]);

  const lines = useMemo(() => [...new Set((data?.rows ?? []).map(r => r.line))], [data]);
  const rows = (data?.rows ?? []).filter(r => !line || r.line === line);
  const sum = (f: (r: EqCheckStatusRow) => boolean) => rows.filter(f).length;
  const today = workDay || todayYmd();
  const open = (code: string, tab: string) => nav(`/e/${encodeURIComponent(code)}?from=hub&tab=${encodeURIComponent(tab)}${date !== today ? `&date=${date}` : ''}`);

  if (err) return <div className="ck-error">{err}</div>;
  if (!data) return <div className="ck-empty">불러오는 중…</div>;
  return (
    <>
      <div className="ck-bar-row">
        <input className="input ck-date" type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} />
        {date !== today && <button className="btn btn-ghost ck-sm" onClick={() => setDate(today)}>오늘</button>}
        <div className="ec-chips">
          <button className={!line ? 'on' : ''} onClick={() => setLine('')}>전체</button>
          {lines.map(l => <button key={l} className={line === l ? 'on' : ''} onClick={() => setLine(l)}>{l}</button>)}
        </div>
      </div>
      <div className="ec-kpis">
        <div><span>일상 ({md(data.date)})</span><b>{sum(r => r.daily.state === 'done')} / {sum(r => r.daily.state !== 'none')}</b></div>
        <div><span>주간 (기한 {md(data.weekDue)} 금)</span><b>{sum(r => r.weekly.state === 'done')} / {sum(r => r.weekly.state !== 'none')}</b></div>
        <div><span>월간 (기한 {md(data.monthDue)} 첫째 금)</span><b>{sum(r => r.monthly.state === 'done')} / {sum(r => r.monthly.state !== 'none')}</b></div>
        <div className={rows.some(r => r.openNg > 0) ? 'bad' : ''}><span>미조치 NG</span><b>{rows.reduce((a, r) => a + r.openNg, 0)}</b></div>
      </div>
      <div className="ec-tw">
        <table className="ec-table">
          <thead>
            <tr>
              <th>설비</th><th>점검표</th>
              <th>일상 <small>{md(data.date)}</small></th>
              <th>주간 <small>~{md(data.weekDue)} 금</small></th>
              <th>월간 <small>~{md(data.monthDue)}</small></th>
              <th>이번 달 일상</th><th>미조치 NG</th>
            </tr>
          </thead>
          <tbody>
            {lines.filter(l => !line || l === line).map(l => (
              [<tr key={`g-${l}`} className="ec-grp"><td colSpan={7}>{l}</td></tr>,
                ...rows.filter(r => r.line === l).map(r => (
                  <tr key={r.unitCode}>
                    <td className="ec-code"><button className="ec-link b" onClick={() => open(r.unitCode, '일상')}>{r.unitCode}</button>
                      {r.process && <small>{r.process}</small>}</td>
                    <td className="ck-dim">{r.templateName}</td>
                    <td><Cell c={r.daily} onClick={() => open(r.unitCode, '일상')} /></td>
                    <td><Cell c={r.weekly} onClick={() => open(r.unitCode, '주간')} /></td>
                    <td><Cell c={r.monthly} onClick={() => open(r.unitCode, '월간')} /></td>
                    <td className={r.dailyDoneDays < data.daysElapsed ? 'ec-warn' : ''}>{r.dailyDoneDays} / {data.daysElapsed}일</td>
                    <td>{r.openNg > 0 ? <span className="ck-pill bad">{r.openNg}</span> : <span className="ck-dim">0</span>}</td>
                  </tr>
                ))]
            ))}
          </tbody>
        </table>
      </div>
      <p className="ck-hint">
        설비 이름을 누르면 그 설비 점검 화면이 열립니다. 일상은 하루 1회(07시 기준), 주간은 그 주 금요일 09시까지,
        월간은 매월 첫째 주 금요일 09시까지입니다. 기한이 지나면 <span className="ck-pill bad">지연</span>으로 표시됩니다.
      </p>
    </>
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
  return (
    <>
      <div className="ck-bar-row">
        <div className="ec-chips">
          <button className={openOnly ? 'on' : ''} onClick={() => setOpenOnly(true)}>미조치</button>
          <button className={!openOnly ? 'on' : ''} onClick={() => setOpenOnly(false)}>전체</button>
        </div>
        <div className="ec-chips">
          <button className={!line ? 'on' : ''} onClick={() => setLine('')}>전체 라인</button>
          {lines.map(l => <button key={l} className={line === l ? 'on' : ''} onClick={() => setLine(l)}>{l}</button>)}
        </div>
        <button className="btn btn-ghost ck-sm" style={{ marginLeft: 'auto' }} onClick={addFault}>+ 고장·부적합 등록</button>
      </div>
      {err && <div className="ck-error">{err}</div>}
      {!rows ? <div className="ck-empty">불러오는 중…</div> : rows.length === 0 ? <div className="ck-empty">{openOnly ? '미조치 NG 가 없습니다.' : 'NG 기록이 없습니다.'}</div> : (
        <div className="ec-tw">
          <table className="ec-table ec-ng">
            <thead><tr><th>발생</th><th>설비</th><th>주기</th><th>항목</th><th>값</th><th>점검자</th><th>조치 내용 및 결과</th></tr></thead>
            <tbody>
              {rows.map(r => (
                <tr key={r.id} className={r.ngStatus === 'OPEN' ? 'open' : ''}>
                  <td className="nowrap">{timeLabel(r.checkedAt)}</td>
                  <td><b>{r.unitCode}</b><small className="ck-dim"> {r.line}</small></td>
                  <td><span className="ck-tag">{r.cycle}</span></td>
                  <td><b>{r.name}</b>{r.point && <small> {r.point}</small>}<div className="ck-sub">{r.category}{r.spec ? ` · ${r.spec}` : ''}</div>
                    {r.memo && <div className="ec-memo">메모: {r.memo}</div>}</td>
                  <td>{r.valueText}</td>
                  <td className="nowrap">{r.checkedByName}</td>
                  <td>
                    {r.ngStatus === 'DONE'
                      ? <><span className="ck-pill ok">조치 완료</span> {r.ngCloseNote}<div className="ck-sub">{r.ngClosedBy} {timeLabel(r.ngClosedAt)}</div></>
                      : <><span className="ck-pill bad">미조치</span>
                        {/* 조치 완료는 편집 등급·설비팀만 — 서버가 확인하고 안 되면 이유를 알려 준다 */}
                        <button className="btn btn-ghost ck-sm" onClick={() => close(r)}>조치 입력</button></>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="ck-hint">조치 완료는 편집 권한이 있는 사람이나 설비팀이 합니다. '비정기 세정진행·위치조정·Jointing' 처럼 현장에서 바로 조치한 보기는 자동으로 조치 완료가 됩니다.</p>
    </>
  );
}
