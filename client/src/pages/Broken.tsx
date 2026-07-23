import { useEffect, useMemo, useRef, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import { useIsMobile } from '../hooks/useIsMobile';
import type { BrokenRecord, BrokenFilterOptions, BrokenTraining, BrokenGoal } from '../api/types';
import './Broken.css';

// ── WPF BrokenManagementView 이식: 무사고 요약·차트·직위 자동·첨부·교육 현황 ──

const STATUSES = ['접수', '조치중', '완료'];
const emptyForm = {
  occurDate: new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10), line: '', productName: '', productType: '',
  sn: '', team: '', causer: '', jobTitle: '', career: '', occurStage: '', description: '',
  status: '접수', isOfficial: true, positionFrozen: false,
  incidentReports: '[]', countermeasureReports: '[]', trainingDocs: '[]', trainingImages: '[]',
};
type Tab = 'records' | 'trainings' | 'status';
type BUser = { realName: string; jobTitle: string; hireDate: string };

const ATT_KEYS = [
  ['incidentReports', '경위서'],
  ['countermeasureReports', '대책서'],
  ['trainingDocs', '교육서'],
  ['trainingImages', '교육이미지'],
] as const;
type AttKey = typeof ATT_KEYS[number][0];

function parseList(s: string): string[] {
  if (!s) return [];
  try {
    const v = JSON.parse(s);
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];
  } catch { return []; }
}
// 경력: 입사일 → "N년 M개월" (WPF CareerFromHire)
function careerFrom(hireDate: string): string {
  if (!hireDate) return '';
  const hire = new Date(hireDate);
  if (isNaN(hire.getTime())) return '';
  const today = new Date();
  let years = today.getFullYear() - hire.getFullYear();
  let months = today.getMonth() - hire.getMonth();
  if (months < 0) { years--; months += 12; }
  if (years < 0) return '';
  if (years === 0) return `${months}개월`;
  if (months === 0) return `${years}년`;
  return `${years}년 ${months}개월`;
}
// 발생일: "07월 22일 (화)" (WPF OccurDateShort)
function occurDisp(d: string | null): string {
  if (!d) return '-';
  const dt = new Date(d + 'T00:00:00');
  if (isNaN(dt.getTime())) return d;
  const dow = '일월화수목금토'[dt.getDay()];
  return `${String(dt.getMonth() + 1).padStart(2, '0')}월 ${String(dt.getDate()).padStart(2, '0')}일 (${dow})`;
}
// 직위 (경력) 표시 — 고정 스냅샷 우선, 없으면 디렉터리 실시간 (WPF PositionDisplay)
function posDisplay(b: BrokenRecord, dir: Map<string, BUser>): string {
  if (b.positionFrozen || b.jobTitle || b.career) {
    const t = b.jobTitle || '-';
    return b.career ? `${t} (${b.career})` : t;
  }
  const u = dir.get(b.causer.trim());
  if (u) {
    const c = careerFrom(u.hireDate);
    return c ? `${u.jobTitle || '-'} (${c})` : (u.jobTitle || '-');
  }
  return b.jobTitle || '-';
}
const acc = (pt: string) => pt.trim().toLowerCase() === 'acc';

// 차트 팔레트 (뮤트 톤)
const PALETTE = ['#4478AE', '#4E9D77', '#DA9E4A', '#C0453E', '#6D5BA8', '#5B8FA8', '#A86D8B', '#64748B'];

const resizeImg = (dataUrl: string): Promise<string> => new Promise(res => {
  const img = new Image();
  img.onload = () => {
    const scale = Math.min(1, 1400 / Math.max(img.width, img.height));
    const cv = document.createElement('canvas');
    cv.width = Math.round(img.width * scale); cv.height = Math.round(img.height * scale);
    const ctx = cv.getContext('2d');
    if (!ctx) { res(dataUrl); return; }
    ctx.drawImage(img, 0, 0, cv.width, cv.height);
    try { res(cv.toDataURL('image/jpeg', 0.72)); } catch { res(dataUrl); }
  };
  img.onerror = () => res(dataUrl);
  img.src = dataUrl;
});
const fileToUrl = (f: File): Promise<string> => new Promise((res, rej) => {
  const fr = new FileReader();
  fr.onload = () => res(fr.result as string);
  fr.onerror = rej;
  fr.readAsDataURL(f);
});

export default function Broken() {
  const [tab, setTab] = useState<Tab>('records');
  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>BROKEN 관리</h2>
          <p>파손 기록과 팀별 무사고 현황, 재발 방지 교육을 관리합니다.</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="bk-tabs">
          <button className={tab === 'records' ? 'active' : ''} onClick={() => setTab('records')}>파손 기록</button>
          <button className={tab === 'trainings' ? 'active' : ''} onClick={() => setTab('trainings')}>교육 기록</button>
          <button className={tab === 'status' ? 'active' : ''} onClick={() => setTab('status')}>교육 현황</button>
        </div>
        {tab === 'records' && <Records />}
        {tab === 'trainings' && <Trainings />}
        {tab === 'status' && <TrainingStatus />}
      </div>
    </div>
  );
}

// ── 파손 기록 ──
function Records() {
  const { canEditOffice: canEdit } = useAccess();
  const isMobile = useIsMobile();
  const [items, setItems] = useState<BrokenRecord[]>([]);
  const [opts, setOpts] = useState<BrokenFilterOptions>({ years: [], teams: [], productTypes: [] });
  const [year, setYear] = useState<number | ''>(new Date().getFullYear());
  const [team, setTeam] = useState('전체');
  const [ptype, setPtype] = useState('전체');
  const [official, setOfficial] = useState('전체');
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [dashOpen, setDashOpen] = useState(() => localStorage.getItem('bk_dash_open') !== '0');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [dirUsers, setDirUsers] = useState<BUser[]>([]);
  const [causerOpen, setCauserOpen] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);
  const attRef = useRef<HTMLInputElement>(null);
  const attTarget = useRef<AttKey>('incidentReports');

  const dir = useMemo(() => new Map(dirUsers.map(u => [u.realName.trim(), u])), [dirUsers]);

  const loadOpts = useCallback(async () => setOpts(await api.get<BrokenFilterOptions>('/api/broken/filters')), []);
  const load = useCallback(async () => {
    const p = new URLSearchParams();
    if (year !== '') p.set('year', String(year));
    if (team !== '전체') p.set('team', team);
    if (ptype !== '전체') p.set('productType', ptype);
    if (official !== '전체') p.set('official', official);
    if (search) p.set('search', search);
    setItems(await api.get<BrokenRecord[]>(`/api/broken?${p.toString()}`));
  }, [year, team, ptype, official, search]);

  useEffect(() => { loadOpts(); }, [loadOpts]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { api.get<BUser[]>('/api/broken/user-directory').then(setDirUsers).catch(() => {}); }, []);
  // 검색 디바운스
  useEffect(() => {
    const t = window.setTimeout(() => setSearch(searchInput.trim()), 300);
    return () => window.clearTimeout(t);
  }, [searchInput]);

  function toggleDash() {
    setDashOpen(o => { localStorage.setItem('bk_dash_open', o ? '0' : '1'); return !o; });
  }
  function resetFilters() {
    setYear(new Date().getFullYear()); setTeam('전체'); setPtype('전체'); setOfficial('전체'); setSearchInput('');
  }

  // ── A. 팀별 무사고 요약 (acc=0.5 가중, 달성 시 포상 90%) ──
  const summary = useMemo(() => {
    const teams = new Set<string>(opts.teams);
    items.forEach(b => { if (b.team) teams.add(b.team); });
    const payMonth = new Date().getMonth() + 1 <= 6 ? '7월 지급예정' : '익년 1월 지급예정';
    return [...teams].sort().map(t => {
      const list = items.filter(b => b.team === t);
      const weighted = list.reduce((s, b) => s + (acc(b.productType) ? 0.5 : 1), 0);
      return { team: t, raw: list.length, weighted, achieved: weighted === 0, payMonth };
    });
  }, [items, opts.teams]);

  // ── A. 월×제품군 누적 차트 데이터 ──
  const chart = useMemo(() => {
    const types = [...new Set(items.map(b => b.productType.trim() || '기타'))].sort();
    const colors = new Map(types.map((t, i) => [t, PALETTE[i % PALETTE.length]]));
    const months = Array.from({ length: 12 }, (_, m) => {
      const inMonth = items.filter(b => b.occurDate && new Date(b.occurDate + 'T00:00:00').getMonth() === m);
      const segs = types
        .map(t => ({ type: t, count: inMonth.filter(b => (b.productType.trim() || '기타') === t).length }))
        .filter(s => s.count > 0);
      return { m: m + 1, total: inMonth.length, segs };
    });
    const max = Math.max(1, ...months.map(x => x.total));
    return { types, colors, months, max };
  }, [items]);

  // ── B. 유발자 선택 → 직위/경력 입력 시점 스냅샷 ──
  function pickCauser(name: string) {
    const u = dir.get(name.trim());
    setForm(f => ({
      ...f, causer: name,
      jobTitle: u ? u.jobTitle : f.jobTitle,
      career: u ? careerFrom(u.hireDate) : f.career,
      positionFrozen: u ? true : f.positionFrozen,
    }));
    setCauserOpen(false);
  }

  function openAdd() {
    if (!canEdit) return; setEditId(null); setForm(emptyForm); setModal(true);
  }
  function openEdit(b: BrokenRecord) {
    setEditId(b.id);
    setForm({
      occurDate: b.occurDate ?? '', line: b.line, productName: b.productName, productType: b.productType,
      sn: b.sn, team: b.team, causer: b.causer, jobTitle: b.jobTitle, career: b.career,
      occurStage: b.occurStage, description: b.description, status: b.status, isOfficial: b.isOfficial,
      positionFrozen: b.positionFrozen,
      incidentReports: b.incidentReports || '[]', countermeasureReports: b.countermeasureReports || '[]',
      trainingDocs: b.trainingDocs || '[]', trainingImages: b.trainingImages || '[]',
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = { ...form, occurDate: form.occurDate || null };
    if (editId) await api.put(`/api/broken/${editId}`, body);
    else await api.post('/api/broken', body);
    setModal(false); load(); loadOpts();
  }
  async function remove(b: BrokenRecord) {
    if (!canEdit) return;
    if (!confirm(`No.${b.no} '${b.productName}' 기록을 삭제하시겠습니까?`)) return;
    await api.del(`/api/broken/${b.id}`); load(); loadOpts();
  }

  // ── C. 첨부 (모달 내 4종) ──
  async function addAtt(key: AttKey, files: File[]) {
    const imgs = files.filter(f => f.type.startsWith('image/'));
    if (imgs.length === 0) return;
    const urls: string[] = [];
    for (const f of imgs) urls.push(await resizeImg(await fileToUrl(f)));
    setForm(f => ({ ...f, [key]: JSON.stringify([...parseList(f[key]), ...urls]) }));
  }
  function delAtt(key: AttKey, i: number) {
    setForm(f => ({ ...f, [key]: JSON.stringify(parseList(f[key]).filter((_, idx) => idx !== i)) }));
  }

  // ── E. 엑셀 내보내기 (현재 필터 기준) ──
  async function exportExcel() {
    try {
      const ExcelJS = (await import('exceljs')).default;
      const wb = new ExcelJS.Workbook();
      const ws = wb.addWorksheet('BROKEN');
      ws.columns = [
        { header: 'NO', width: 6 }, { header: '발생일', width: 12 }, { header: '라인', width: 10 },
        { header: '제품명', width: 34 }, { header: 'S/N', width: 18 }, { header: '팀', width: 10 },
        { header: '유발자', width: 10 }, { header: '직위 (경력)', width: 16 }, { header: '제품군', width: 10 },
        { header: '발생단계', width: 12 }, { header: '공식여부', width: 10 }, { header: '상태', width: 8 },
        { header: '내용', width: 50 },
      ];
      ws.getRow(1).font = { bold: true };
      items.forEach(b => ws.addRow([
        b.no, b.occurDate ?? '-', b.line, b.productName, b.sn, b.team,
        b.causer, posDisplay(b, dir), b.productType, b.occurStage,
        b.isOfficial ? '공식' : '비공식', b.status, b.description,
      ]));
      const buf = await wb.xlsx.writeBuffer();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
      a.download = `BROKEN_${year === '' ? '전체' : year}.xlsx`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch (err) {
      alert(err instanceof Error ? err.message : '엑셀 내보내기에 실패했습니다.');
    }
  }

  const causerQ = form.causer.trim();
  const causerHits = causerQ ? dirUsers.filter(u => u.realName.includes(causerQ)) : dirUsers;

  function attCell(b: BrokenRecord) {
    const chips = ATT_KEYS
      .map(([key, label]) => ({ label, n: parseList(b[key]).length }))
      .filter(x => x.n > 0);
    if (chips.length === 0) return <span className="bk-att-none">-</span>;
    return (
      <div className="bk-att-chips">
        {chips.map(c => <span key={c.label} className="bk-att-chip" onClick={() => canEdit && openEdit(b)}>{c.label} {c.n}</span>)}
      </div>
    );
  }

  return (
    <>
      <div className="bk-filters">
        <select value={year} onChange={e => setYear(e.target.value === '' ? '' : Number(e.target.value))}>
          <option value="">전체 연도</option>
          {opts.years.map(y => <option key={y} value={y}>{y}년</option>)}
        </select>
        <select value={team} onChange={e => setTeam(e.target.value)}>
          <option>전체</option>{opts.teams.map(t => <option key={t}>{t}</option>)}
        </select>
        <select value={ptype} onChange={e => setPtype(e.target.value)}>
          <option>전체</option>{opts.productTypes.map(t => <option key={t}>{t}</option>)}
        </select>
        <select value={official} onChange={e => setOfficial(e.target.value)}>
          <option>전체</option><option>공식</option><option>비공식</option>
        </select>
        <input className="bk-search" placeholder="제품/유발자/SN 검색" value={searchInput} onChange={e => setSearchInput(e.target.value)} />
        <button className="bk-sm" onClick={resetFilters}>필터 초기화</button>
        <span className="bk-count">{items.length}건</span>
        <button className="bk-sm" onClick={exportExcel}>엑셀 내보내기</button>
        {canEdit && <button className="btn btn-primary bk-add" onClick={openAdd}>+ 등록</button>}
      </div>

      {/* A. 무사고 요약 + 월×제품군 차트 */}
      <div className="bk-dash">
        <button className="bk-dash-head" onClick={toggleDash}>
          <span>무사고 현황 대시보드 <small>{year === '' ? '전체 연도' : `${year}년`} · 필터 기준</small></span>
          <span className="bk-dash-arrow">{dashOpen ? '접기 ▲' : '펼치기 ▼'}</span>
        </button>
        {dashOpen && (
          <div className="bk-dash-body">
            <div className="bk-sums">
              {summary.length === 0 && <div className="bk-empty">팀 데이터가 없습니다</div>}
              {summary.map(s => (
                <div key={s.team} className={`bk-sum ${s.achieved ? 'ok' : 'no'}`}>
                  <div className="bk-sum-team">{s.team}</div>
                  <div className="bk-sum-cnt">{s.raw}건 <small>(가중 {s.weighted}건 · acc 0.5)</small></div>
                  <div className="bk-sum-badge">{s.achieved ? '무사고 달성 O' : '달성 X'}</div>
                  <div className="bk-sum-pay">포상율 {s.achieved ? '90%' : '30%'} · {s.payMonth}</div>
                </div>
              ))}
            </div>
            <div className="bk-chart-wrap">
              <div className="bk-chart">
                {chart.months.map(mo => (
                  <div key={mo.m} className="bk-col">
                    {mo.total > 0 && <span className="bk-col-total">{mo.total}</span>}
                    <div className="bk-col-bar">
                      {mo.segs.map(s => (
                        <div key={s.type} className="bk-seg" title={`${mo.m}월 ${s.type} ${s.count}건`}
                          style={{ height: `${(s.count / chart.max) * 100}%`, background: chart.colors.get(s.type) }} />
                      ))}
                    </div>
                    <span className="bk-col-m">{mo.m}월</span>
                  </div>
                ))}
              </div>
              <div className="bk-legend">
                {chart.types.map(t => (
                  <span key={t} className="bk-lg"><i style={{ background: chart.colors.get(t) }} />{t}</span>
                ))}
              </div>
            </div>
          </div>
        )}
      </div>

      {isMobile ? (
        <div className="bk-mlist">
          {items.length === 0 && <div className="bk-empty">기록이 없습니다</div>}
          {items.map(b => (
            <div key={b.id} className="bk-mcard">
              <div className="bk-mc-top">
                <span className="bk-mc-no">No.{b.no}</span>
                <span className="bk-mc-date">{occurDisp(b.occurDate)}</span>
                <span className={`bk-official ${b.isOfficial ? 'on' : 'off'}`}>{b.isOfficial ? '공식' : '비공식'}</span>
                <span className={`bk-status s-${b.status}`}>{b.status}</span>
              </div>
              <div className="bk-mc-prod">{b.productName}{b.sn && <small> ({b.sn})</small>}{b.productType && <span className="bk-mc-type"> · {b.productType}</span>}</div>
              <div className="bk-mc-meta">
                <span>라인 {b.line || '-'}</span>
                <span>{b.occurStage || '-'}</span>
                <span>유발 {b.causer} {posDisplay(b, dir)}</span>
                <span>{b.team || '-'}</span>
              </div>
              {canEdit && (
                <div className="bk-mc-foot">
                  <button className="bk-sm" onClick={() => openEdit(b)}>수정</button>
                  <button className="bk-sm danger" onClick={() => remove(b)}>삭제</button>
                </div>
              )}
            </div>
          ))}
        </div>
      ) : (
        <div className="bk-table-wrap">
          <table className="bk-table">
            <thead>
              <tr><th>No</th><th>발생일</th><th>라인</th><th>제품</th><th>제품군</th><th>유발자</th><th>팀</th><th>발생단계</th><th>구분</th><th>상태</th><th>첨부</th><th>관리</th></tr>
            </thead>
            <tbody>
              {items.length === 0 && <tr><td colSpan={12} className="bk-empty">기록이 없습니다</td></tr>}
              {items.map(b => (
                <tr key={b.id}>
                  <td>{b.no}</td>
                  <td className="bk-date">{occurDisp(b.occurDate)}</td>
                  <td>{b.line || '-'}</td>
                  <td className="bk-prod">{b.productName}{b.sn && <small> ({b.sn})</small>}</td>
                  <td>{b.productType || '-'}</td>
                  <td className="bk-causer">{b.causer}<small>{posDisplay(b, dir)}</small></td>
                  <td>{b.team || '-'}</td>
                  <td>{b.occurStage || '-'}</td>
                  <td><span className={`bk-official ${b.isOfficial ? 'on' : 'off'}`}>{b.isOfficial ? '공식' : '비공식'}</span></td>
                  <td><span className={`bk-status s-${b.status}`}>{b.status}</span></td>
                  <td>{attCell(b)}</td>
                  <td>{canEdit && <div className="bk-actions"><button className="bk-sm" onClick={() => openEdit(b)}>수정</button><button className="bk-sm danger" onClick={() => remove(b)}>삭제</button></div>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box bk-modal" onSubmit={save}>
            <h3>{editId ? 'BROKEN 수정' : 'BROKEN 등록'}</h3>
            <div className="bk-grid">
              <L l="발생일"><input className="input" type="date" value={form.occurDate} onChange={e => setForm({ ...form, occurDate: e.target.value })} /></L>
              <L l="라인"><input className="input" value={form.line} onChange={e => setForm({ ...form, line: e.target.value })} /></L>
              <L l="제품명"><input className="input" required value={form.productName} onChange={e => setForm({ ...form, productName: e.target.value })} /></L>
              <L l="제품군"><input className="input" value={form.productType} onChange={e => setForm({ ...form, productType: e.target.value })} placeholder="acc는 0.5건 가중" /></L>
              <L l="S/N"><input className="input" value={form.sn} onChange={e => setForm({ ...form, sn: e.target.value })} /></L>
              <L l="팀"><input className="input" value={form.team} onChange={e => setForm({ ...form, team: e.target.value })} placeholder="생산 / 물류" /></L>
              <L l="유발자">
                <div className="bk-suggest">
                  <input className="input" value={form.causer}
                    onChange={e => { setForm({ ...form, causer: e.target.value }); setCauserOpen(true); }}
                    onFocus={() => setCauserOpen(true)}
                    onBlur={() => window.setTimeout(() => setCauserOpen(false), 120)}
                    placeholder="이름 선택 시 직위·경력 자동" />
                  {causerOpen && causerHits.length > 0 && (
                    <div className="bk-suggest-pop">
                      {causerHits.slice(0, 20).map(u => (
                        <button type="button" key={u.realName} className="bk-suggest-item"
                          onMouseDown={e => { e.preventDefault(); pickCauser(u.realName); }}>
                          <b>{u.realName}</b>
                          <span>{u.jobTitle || '-'}{careerFrom(u.hireDate) && ` · ${careerFrom(u.hireDate)}`}</span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </L>
              <L l="직위"><input className="input" value={form.jobTitle} onChange={e => setForm({ ...form, jobTitle: e.target.value })} /></L>
              <L l="경력"><input className="input" value={form.career} onChange={e => setForm({ ...form, career: e.target.value })} placeholder="예: 3년 2개월" /></L>
              <L l="발생단계"><input className="input" value={form.occurStage} onChange={e => setForm({ ...form, occurStage: e.target.value })} /></L>
              <L l="상태"><select className="input" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>{STATUSES.map(s => <option key={s}>{s}</option>)}</select></L>
              <L l="구분">
                <div className="bk-radio">
                  <label className={form.isOfficial ? 'on' : ''}><input type="radio" checked={form.isOfficial} onChange={() => setForm({ ...form, isOfficial: true })} /> 공식</label>
                  <label className={!form.isOfficial ? 'on' : ''}><input type="radio" checked={!form.isOfficial} onChange={() => setForm({ ...form, isOfficial: false })} /> 비공식</label>
                </div>
              </L>
            </div>
            <label className="bk-frozen">
              <input type="checkbox" checked={form.positionFrozen} onChange={e => setForm({ ...form, positionFrozen: e.target.checked })} />
              직위/경력 입력시점 고정 <small>(해제 시 사용자 정보 기준으로 실시간 표시)</small>
            </label>
            <L l="내용"><textarea className="input ta" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></L>

            {/* C. 첨부 4종 */}
            <div className="bk-attgrid">
              {ATT_KEYS.map(([key, label]) => {
                const list = parseList(form[key]);
                return (
                  <div key={key} className="bk-attsec">
                    <div className="bk-attsec-h">{label} {list.length > 0 && <em>{list.length}건</em>}</div>
                    <div className="bk-atts">
                      {list.map((v, i) => (
                        <div key={i} className="bk-att">
                          {v.startsWith('data:') ? (
                            <img src={v} alt="" onClick={() => setPreview(v)} />
                          ) : (
                            <span className="bk-att-file" title={v}>{v.split(/[\\/]/).pop()}</span>
                          )}
                          <button type="button" className="bk-att-x" onClick={() => delAtt(key, i)}>✕</button>
                        </div>
                      ))}
                      <button type="button" className="bk-att-add"
                        onClick={() => { attTarget.current = key; attRef.current?.click(); }}>+ 사진</button>
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '저장' : '등록'}</button>
            </div>
          </form>
        </div>
      )}

      <input ref={attRef} type="file" accept="image/*" multiple style={{ display: 'none' }}
        onChange={e => {
          const files = Array.from(e.target.files ?? []);
          if (files.length) addAtt(attTarget.current, files);
          e.target.value = '';
        }} />

      {preview && (
        <div className="bk-preview" onClick={() => setPreview(null)}>
          <img src={preview} alt="" />
        </div>
      )}
    </>
  );
}

// ── 교육 기록 ──
function Trainings() {
  const { canEditOffice: canEdit } = useAccess();
  const [type, setType] = useState('전체');
  const [list, setList] = useState<BrokenTraining[]>([]);
  const [edit, setEdit] = useState<BrokenTraining | 'new' | null>(null);
  const [form, setForm] = useState({ trainingType: 'production', trainingDate: '', content: '', documents: '[]', images: '[]' });
  const [preview, setPreview] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    const q = type === '전체' ? '' : `?type=${type}`;
    setList(await api.get<BrokenTraining[]>(`/api/broken/trainings${q}`));
  }, [type]);
  useEffect(() => { load(); }, [load]);

  function openNew() {
    setEdit('new');
    setForm({ trainingType: type === '전체' ? 'production' : type, trainingDate: '', content: '', documents: '[]', images: '[]' });
  }
  function openEdit(t: BrokenTraining) {
    setEdit(t);
    setForm({ trainingType: t.trainingType, trainingDate: t.trainingDate ?? '', content: t.content, documents: t.documents || '[]', images: t.images || '[]' });
  }
  async function save() {
    const body = { ...form, trainingDate: form.trainingDate || null };
    if (edit === 'new') await api.post('/api/broken/trainings', body);
    else if (edit) await api.put(`/api/broken/trainings/${edit.id}`, body);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('교육 기록을 삭제할까요?')) return;
    await api.del(`/api/broken/trainings/${id}`); setEdit(null); load();
  }
  async function addImgs(files: File[]) {
    const imgs = files.filter(f => f.type.startsWith('image/'));
    if (!imgs.length) return;
    const urls: string[] = [];
    for (const f of imgs) urls.push(await resizeImg(await fileToUrl(f)));
    setForm(f => ({ ...f, images: JSON.stringify([...parseList(f.images), ...urls]) }));
  }

  const attSummary = (t: BrokenTraining) => {
    const d = parseList(t.documents).length, i = parseList(t.images).length;
    if (d === 0 && i === 0) return '-';
    return [d > 0 ? `문서 ${d}` : '', i > 0 ? `사진 ${i}` : ''].filter(Boolean).join(' · ');
  };

  return (
    <>
      <div className="bk-filters">
        <select value={type} onChange={e => setType(e.target.value)}>
          <option value="전체">전체</option><option value="production">생산</option><option value="logistics">물류</option>
        </select>
        <span className="bk-count">{list.length}건</span>
        {canEdit && <button className="btn btn-primary bk-add" onClick={openNew}>+ 교육 추가</button>}
      </div>
      <div className="bk-table-wrap">
        <table className="bk-table">
          <thead><tr><th>구분</th><th>일자</th><th>내용</th><th>첨부</th><th>관리</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={5} className="bk-empty">교육 기록이 없습니다</td></tr>}
            {list.map(t => (
              <tr key={t.id}>
                <td>{t.trainingType === 'logistics' ? '물류' : '생산'}</td>
                <td>{t.trainingDate ?? '-'}</td>
                <td className="bk-prod">{t.content}</td>
                <td className="bk-att-sum">{attSummary(t)}</td>
                <td>{canEdit && <div className="bk-actions"><button className="bk-sm" onClick={() => openEdit(t)}>수정</button><button className="bk-sm danger" onClick={() => del(t.id)}>삭제</button></div>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {edit && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setEdit(null); }}>
          <div className="modal-box bk-modal">
            <h3>{edit === 'new' ? '교육 기록 추가' : '교육 기록 수정'}</h3>
            <div className="bk-grid">
              <L l="구분"><select className="input" value={form.trainingType} onChange={e => setForm({ ...form, trainingType: e.target.value })}><option value="production">생산</option><option value="logistics">물류</option></select></L>
              <L l="일자"><input className="input" type="date" value={form.trainingDate} onChange={e => setForm({ ...form, trainingDate: e.target.value })} /></L>
            </div>
            <L l="내용"><textarea className="input ta" value={form.content} onChange={e => setForm({ ...form, content: e.target.value })} /></L>
            <div className="bk-attsec">
              <div className="bk-attsec-h">교육 사진 {parseList(form.images).length > 0 && <em>{parseList(form.images).length}건</em>}</div>
              <div className="bk-atts">
                {parseList(form.images).map((v, i) => (
                  <div key={i} className="bk-att">
                    <img src={v} alt="" onClick={() => setPreview(v)} />
                    <button type="button" className="bk-att-x"
                      onClick={() => setForm(f => ({ ...f, images: JSON.stringify(parseList(f.images).filter((_, idx) => idx !== i)) }))}>✕</button>
                  </div>
                ))}
                <button type="button" className="bk-att-add" onClick={() => fileRef.current?.click()}>+ 사진</button>
              </div>
              {parseList(form.documents).length > 0 && (
                <div className="bk-docs">
                  문서: {parseList(form.documents).map(d => d.split(/[\\/]/).pop()).join(', ')}
                </div>
              )}
            </div>
            <div className="modal-actions">
              {edit !== 'new' && <button type="button" className="btn danger-btn" onClick={() => del(edit.id)}>삭제</button>}
              <button type="button" className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
              <button type="button" className="btn btn-primary" onClick={save}>저장</button>
            </div>
          </div>
        </div>
      )}

      <input ref={fileRef} type="file" accept="image/*" multiple style={{ display: 'none' }}
        onChange={e => { const fs = Array.from(e.target.files ?? []); if (fs.length) addImgs(fs); e.target.value = ''; }} />
      {preview && <div className="bk-preview" onClick={() => setPreview(null)}><img src={preview} alt="" /></div>}
    </>
  );
}

// ── 교육 현황 (목표 대비 실행률 매트릭스 · WPF 교육 현황 탭) ──
function TrainingStatus() {
  const { canEditOffice: canEdit } = useAccess();
  const [year, setYear] = useState(new Date().getFullYear());
  const [trainings, setTrainings] = useState<BrokenTraining[]>([]);
  const [goals, setGoals] = useState<BrokenGoal[]>([]);
  const [memo, setMemo] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    api.get<BrokenTraining[]>('/api/broken/trainings').then(setTrainings);
    api.get<BrokenGoal[]>('/api/broken/goals').then(setGoals);
    api.get<{ memo: string }>('/api/broken/memo').then(m => setMemo(m.memo));
  }, []);

  const goalOf = (cat: string, y: number) => goals.find(g => g.category === cat && g.year === y)?.target ?? '';
  function setGoal(cat: string, y: number, target: string) {
    setGoals(gs => {
      const hit = gs.find(g => g.category === cat && g.year === y);
      if (hit) return gs.map(g => g === hit ? { ...g, target } : g);
      return [...gs, { id: 0, category: cat, year: y, target }];
    });
  }
  const countOf = (cat: string, y: number) =>
    trainings.filter(t => t.trainingType === cat && t.trainingDate && new Date(t.trainingDate + 'T00:00:00').getFullYear() === y).length;
  const monthly = (cat: string | null, y: number, m: number) =>
    trainings.filter(t => (cat === null || t.trainingType === cat) && t.trainingDate &&
      new Date(t.trainingDate + 'T00:00:00').getFullYear() === y &&
      new Date(t.trainingDate + 'T00:00:00').getMonth() === m).length;

  async function save() {
    await api.put('/api/broken/goals', goals.filter(g => g.target !== '').map(g => ({ category: g.category, year: g.year, target: g.target })));
    await api.put('/api/broken/memo', { memo });
    setSaved(true); setTimeout(() => setSaved(false), 1500);
  }

  const rows = [
    { key: 'production', label: '생산 안전 교육' },
    { key: 'logistics', label: '물류 안전 교육' },
  ];
  const years = Array.from({ length: new Date().getFullYear() - 2024 + 2 }, (_, i) => 2024 + i);

  return (
    <div className="bk-stpage">
      <div className="bk-filters">
        <select value={year} onChange={e => setYear(Number(e.target.value))}>
          {years.map(y => <option key={y} value={y}>{y}년</option>)}
        </select>
        <span className="bk-count">교육 활동 실행률</span>
        {canEdit && <button className="btn btn-primary bk-add" onClick={save}>{saved ? '저장됨 ✓' : '저장'}</button>}
      </div>
      <div className="bk-table-wrap">
        <table className="bk-table bk-mx">
          <thead>
            <tr>
              <th>구분</th><th>{year - 1}년 실적</th><th>{year}년 목표</th><th>{year}년 실적</th>
              {Array.from({ length: 12 }, (_, m) => <th key={m} className="c-m">{m + 1}월</th>)}
            </tr>
          </thead>
          <tbody>
            {rows.map(r => (
              <tr key={r.key}>
                <td className="bk-mx-cat">{r.label}</td>
                <td><input className="bk-mx-in" value={goalOf(r.key, year - 1)} disabled={!canEdit}
                  onChange={e => setGoal(r.key, year - 1, e.target.value)} placeholder="-" /></td>
                <td><input className="bk-mx-in" value={goalOf(r.key, year)} disabled={!canEdit}
                  onChange={e => setGoal(r.key, year, e.target.value)} placeholder="-" /></td>
                <td className="bk-mx-actual">{countOf(r.key, year)}</td>
                {Array.from({ length: 12 }, (_, m) => {
                  const n = monthly(r.key, year, m);
                  return <td key={m} className={`c-m ${n > 0 ? 'hit' : ''}`}>{n > 0 ? n : '-'}</td>;
                })}
              </tr>
            ))}
            <tr className="bk-mx-total">
              <td className="bk-mx-cat">합계</td>
              <td>{rows.reduce((s, r) => s + (Number(goalOf(r.key, year - 1)) || 0), 0) || '-'}</td>
              <td>{rows.reduce((s, r) => s + (Number(goalOf(r.key, year)) || 0), 0) || '-'}</td>
              <td className="bk-mx-actual">{rows.reduce((s, r) => s + countOf(r.key, year), 0)}</td>
              {Array.from({ length: 12 }, (_, m) => {
                const n = monthly(null, year, m);
                return <td key={m} className={`c-m ${n > 0 ? 'hit' : ''}`}>{n > 0 ? n : '-'}</td>;
              })}
            </tr>
          </tbody>
        </table>
      </div>
      <div className="bk-goal-sec">
        <div className="bk-goal-h">메모</div>
        <textarea className="input ta" value={memo} disabled={!canEdit} onChange={e => setMemo(e.target.value)} />
      </div>
    </div>
  );
}

function L({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="bk-field"><label>{l}</label>{children}</div>;
}
