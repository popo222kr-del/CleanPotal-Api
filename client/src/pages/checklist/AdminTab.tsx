import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CheckImportResult, CheckItemDef, CheckQrPage, CheckZoneDef } from '../../api/types';
import { parseCheckWorkbook, type ParsedCheckWorkbook } from './checkImport';
import { PHOTO_POLICIES, TIMINGS, WEEKDAYS } from './common';

// 양식 관리(관리자) — 구역·항목·QR 라벨·설정·엑셀 가져오기.

type Sub = 'zones' | 'items' | 'labels' | 'settings' | 'import';

export default function AdminTab() {
  const [sub, setSub] = useState<Sub>('items');
  const [zones, setZones] = useState<CheckZoneDef[]>([]);
  const [items, setItems] = useState<CheckItemDef[]>([]);

  const load = useCallback(async () => {
    const [z, i] = await Promise.all([
      api.get<CheckZoneDef[]>('/api/checklist/zones'),
      api.get<CheckItemDef[]>('/api/checklist/items'),
    ]);
    setZones(z); setItems(i);
  }, []);
  useEffect(() => { load().catch(e => alert(e instanceof Error ? e.message : '불러오지 못했습니다.')); }, [load]);

  const subs: [Sub, string][] = [['items', '점검 항목'], ['zones', '구역'], ['labels', 'QR 라벨'], ['settings', '설정'], ['import', '엑셀 가져오기']];
  return (
    <div>
      <div className="ck-subtabs ck-noprint">
        {subs.map(([s, l]) => <button key={s} className={`ck-subtab ${sub === s ? 'on' : ''}`} onClick={() => setSub(s)}>{l}</button>)}
      </div>
      {sub === 'items' && <ItemsAdmin zones={zones} items={items} reload={load} />}
      {sub === 'zones' && <ZonesAdmin zones={zones} reload={load} />}
      {sub === 'labels' && <LabelsAdmin />}
      {sub === 'settings' && <SettingsAdmin />}
      {sub === 'import' && <ImportAdmin reload={load} />}
    </div>
  );
}

// ── 구역 ──

const emptyZone: CheckZoneDef = { id: 0, code: '', name: '', line: 'METAL', sortOrder: 1, isCommon: false, hasQr: true, qrLocation: '', qrCount: 1, isActive: true, note: '' };

function ZonesAdmin({ zones, reload }: { zones: CheckZoneDef[]; reload: () => Promise<void> }) {
  const [edit, setEdit] = useState<CheckZoneDef | null>(null);
  const original = edit && edit.id ? zones.find(z => z.id === edit.id) : undefined;
  const codeChanged = !!original && original.code !== edit?.code;
  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (!edit) return;
    if (codeChanged && !confirm(`구역코드를 ${original!.code} → ${edit.code} 로 바꿉니다.\n이 구역의 항목·점검 기록은 새 코드로 옮겨지지만, 이미 붙인 QR 은 옛 주소라 다시 인쇄해서 바꿔 붙여야 합니다.`)) return;
    try { await api.put('/api/checklist/zones', edit); setEdit(null); await reload(); }
    catch (err) { alert(err instanceof Error ? err.message : '저장하지 못했습니다.'); }
  }
  async function remove(z: CheckZoneDef) {
    if (!confirm(`${z.name}(${z.code}) 구역과 그 항목을 지울까요?\n점검 기록이 있으면 지울 수 없고, '사용' 을 끄면 됩니다.`)) return;
    try { await api.del(`/api/checklist/zones/${z.id}`); setEdit(null); await reload(); }
    catch (err) { alert(err instanceof Error ? err.message : '지우지 못했습니다.'); }
  }
  return (
    <div>
      <div className="ck-bar-row"><button className="btn btn-primary" onClick={() => setEdit({ ...emptyZone, sortOrder: zones.length + 1 })}>+ 구역 추가</button></div>
      <table className="ck-admin">
        <thead><tr><th>코드</th><th>이름</th><th>라인</th><th>순서</th><th>구분</th><th>QR 부착 위치</th><th>사용</th><th /></tr></thead>
        <tbody>
          {zones.map(z => (
            <tr key={z.id} className={z.isActive ? '' : 'off'}>
              <td><b>{z.code}</b></td><td>{z.name}</td><td>{z.line}</td><td>{z.sortOrder}</td>
              <td>{z.isCommon ? '공통 항목(QR 없음)' : z.hasQr ? `QR ${z.qrCount}장` : 'QR 없음'}</td>
              <td>{z.qrLocation || (z.hasQr && !z.isCommon ? <span className="ck-need">미입력</span> : '')}</td>
              <td>{z.isActive ? '예' : '아니오'}</td>
              <td><button className="btn btn-ghost ck-sm" onClick={() => setEdit(z)}>수정</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      {edit && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setEdit(null); }}>
          <form className="modal-box ck-modal" onSubmit={save}>
            <h3>{edit.id ? '구역 수정' : '구역 추가'}</h3>
            <div className="ck-form">
              <label>구역코드<input className="input" value={edit.code} placeholder="예: N-OUT"
                onChange={e => setEdit({ ...edit, code: e.target.value.toUpperCase() })} /></label>
              <label>이름<input className="input" value={edit.name} onChange={e => setEdit({ ...edit, name: e.target.value })} /></label>
              <label>라인<select className="input" value={edit.line} onChange={e => setEdit({ ...edit, line: e.target.value })}>
                {['METAL', 'N-METAL', '공통'].map(l => <option key={l}>{l}</option>)}</select></label>
              <label>순서<input className="input" type="number" value={edit.sortOrder} onChange={e => setEdit({ ...edit, sortOrder: Number(e.target.value) })} /></label>
              <label className="ck-chk"><input type="checkbox" checked={edit.isCommon} onChange={e => setEdit({ ...edit, isCommon: e.target.checked })} /> 공통 항목용 구역(QR 없이 같은 라인 모든 구역에 뜸)</label>
              {!edit.isCommon && <>
                <label className="ck-chk"><input type="checkbox" checked={edit.hasQr} onChange={e => setEdit({ ...edit, hasQr: e.target.checked })} /> QR 부착</label>
                <label>QR 부착 위치<input className="input" value={edit.qrLocation} onChange={e => setEdit({ ...edit, qrLocation: e.target.value })} /></label>
                <label>QR 장수<input className="input" type="number" min={0} value={edit.qrCount} onChange={e => setEdit({ ...edit, qrCount: Number(e.target.value) })} /></label>
              </>}
              <label className="ck-chk"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> 사용</label>
              <label className="wide">비고<input className="input" value={edit.note} onChange={e => setEdit({ ...edit, note: e.target.value })} /></label>
            </div>
            <p className={`ck-hint ${codeChanged ? 'ck-need' : ''}`}>
              구역코드는 QR 주소(…/c/구역코드)에 들어갑니다. 바꾸면 항목·기록은 따라가지만 붙인 QR 은 다시 인쇄해야 합니다. 이름·위치는 언제든 바꿔도 QR 은 그대로입니다.
            </p>
            <div className="modal-actions">
              {edit.id > 0 && <button type="button" className="btn btn-ghost ck-danger" onClick={() => remove(edit)}>삭제</button>}
              <button type="button" className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
              <button className="btn btn-primary">저장</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

// ── 항목 ──

function ItemsAdmin({ zones, items, reload }: { zones: CheckZoneDef[]; items: CheckItemDef[]; reload: () => Promise<void> }) {
  const [zoneFilter, setZoneFilter] = useState('');
  const [edit, setEdit] = useState<CheckItemDef | null>(null);
  const shown = items.filter(i => !zoneFilter || i.zoneCode === zoneFilter);
  const zoneName = (c: string) => zones.find(z => z.code === c)?.name ?? c;

  function blank(): CheckItemDef {
    return {
      id: 0, code: '', zoneCode: zoneFilter || zones.find(z => !z.isCommon)?.code || '', sortOrder: shown.length + 1,
      text: '', detail: '', cycle: '', timing: '주·야 각 1회', weekday: null, resultType: 'OKNG', unit: '',
      minValue: null, maxValue: null, judgeMode: 'NONE', photoPolicy: 'NG 시', required: true, allowNa: false,
      paperForm: '', ngDept: '', validFrom: null, validTo: null, revisionNote: '', isActive: true, note: '', updatedAt: '', updatedBy: '',
    };
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (!edit) return;
    try { await api.put('/api/checklist/items', edit); setEdit(null); await reload(); }
    catch (err) { alert(err instanceof Error ? err.message : '저장하지 못했습니다.'); }
  }
  async function remove(i: CheckItemDef) {
    if (!confirm(`${i.code} ${i.text} 항목을 지울까요?\n(점검 기록이 있으면 지울 수 없고, '사용' 을 끄면 됩니다)`)) return;
    try { await api.del(`/api/checklist/items/${i.id}`); setEdit(null); await reload(); }
    catch (err) { alert(err instanceof Error ? err.message : '지우지 못했습니다.'); }
  }
  const num = (v: string) => (v.trim() === '' ? null : Number(v));

  return (
    <div>
      <div className="ck-bar-row">
        <select className="input ck-sel" value={zoneFilter} onChange={e => setZoneFilter(e.target.value)}>
          <option value="">전체 구역</option>
          {zones.map(z => <option key={z.code} value={z.code}>{z.line} · {z.name} ({z.code})</option>)}
        </select>
        <button className="btn btn-primary" onClick={() => setEdit(blank())}>+ 항목 추가</button>
        <span className="ck-dim">{shown.length}개</span>
      </div>
      <table className="ck-admin">
        <thead><tr><th>ID</th><th>구역</th><th>점검 내용</th><th>시점</th><th>결과</th><th>사진</th><th>필수</th><th>N/A</th><th>사용</th><th /></tr></thead>
        <tbody>
          {shown.map(i => (
            <tr key={i.id} className={i.isActive ? '' : 'off'}>
              <td><b>{i.code}</b></td>
              <td>{zoneName(i.zoneCode)}</td>
              <td>{i.text}{i.detail && <div className="ck-dim">{i.detail}</div>}</td>
              <td>{i.timing}{i.timing === '주 1회' && (i.weekday ? ` (${WEEKDAYS[i.weekday - 1]})` : <span className="ck-need"> 요일 미정</span>)}</td>
              <td>{i.resultType === 'NUM' ? `수치 ${i.judgeMode === 'ABS' ? `±${i.maxValue}` : `${i.minValue ?? ''}~${i.maxValue ?? ''}`}${i.unit}` : 'OK/NG'}</td>
              <td>{i.photoPolicy}</td>
              <td>{i.required ? '예' : ''}</td>
              <td>{i.allowNa ? '예' : ''}</td>
              <td>{i.isActive ? '예' : '아니오'}</td>
              <td><button className="btn btn-ghost ck-sm" onClick={() => setEdit(i)}>수정</button></td>
            </tr>
          ))}
        </tbody>
      </table>

      {edit && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setEdit(null); }}>
          <form className="modal-box ck-modal" onSubmit={save}>
            <h3>{edit.id ? `항목 수정 — ${edit.code}` : '항목 추가'}</h3>
            <div className="ck-form">
              <label>구역<select className="input" value={edit.zoneCode} onChange={e => setEdit({ ...edit, zoneCode: e.target.value })}>
                {zones.map(z => <option key={z.code} value={z.code}>{z.line} · {z.name} ({z.code})</option>)}</select></label>
              <label>순서<input className="input" type="number" value={edit.sortOrder} onChange={e => setEdit({ ...edit, sortOrder: Number(e.target.value) })} /></label>
              <label className="wide">점검 내용<input className="input" value={edit.text} onChange={e => setEdit({ ...edit, text: e.target.value })} /></label>
              <label className="wide">세부 대상<input className="input" value={edit.detail} onChange={e => setEdit({ ...edit, detail: e.target.value })} /></label>
              <label>점검 시점<select className="input" value={edit.timing} onChange={e => setEdit({ ...edit, timing: e.target.value })}>
                {TIMINGS.map(t => <option key={t}>{t}</option>)}</select></label>
              {edit.timing === '주 1회' && (
                <label>요일<select className="input" value={edit.weekday ?? ''} onChange={e => setEdit({ ...edit, weekday: e.target.value ? Number(e.target.value) : null })}>
                  <option value="">그 주 아무 날</option>
                  {WEEKDAYS.map((w, idx) => <option key={w} value={idx + 1}>{w}</option>)}</select></label>
              )}
              <label>결과 형식<select className="input" value={edit.resultType} onChange={e => setEdit({ ...edit, resultType: e.target.value as 'OKNG' | 'NUM', judgeMode: e.target.value === 'NUM' ? 'ABS' : 'NONE' })}>
                <option value="OKNG">OK/NG</option><option value="NUM">수치</option></select></label>
              {edit.resultType === 'NUM' && <>
                <label>판정<select className="input" value={edit.judgeMode} onChange={e => setEdit({ ...edit, judgeMode: e.target.value })}>
                  <option value="ABS">절댓값 이하</option><option value="RANGE">범위(하한~상한)</option><option value="NONE">판정 없음</option></select></label>
                <label>단위<input className="input" value={edit.unit} onChange={e => setEdit({ ...edit, unit: e.target.value })} /></label>
                {edit.judgeMode === 'RANGE' && <label>하한<input className="input" inputMode="decimal" value={edit.minValue ?? ''} onChange={e => setEdit({ ...edit, minValue: num(e.target.value) })} /></label>}
                <label>상한<input className="input" inputMode="decimal" value={edit.maxValue ?? ''} onChange={e => setEdit({ ...edit, maxValue: num(e.target.value) })} /></label>
              </>}
              <label>사진 정책<select className="input" value={edit.photoPolicy} onChange={e => setEdit({ ...edit, photoPolicy: e.target.value })}>
                {PHOTO_POLICIES.map(p => <option key={p}>{p}</option>)}</select></label>
              <label>연계 종이 양식<input className="input" value={edit.paperForm} onChange={e => setEdit({ ...edit, paperForm: e.target.value })} /></label>
              <label>NG 담당부서<input className="input" value={edit.ngDept} onChange={e => setEdit({ ...edit, ngDept: e.target.value })} /></label>
              <label>적용 시작일<input className="input" type="date" value={edit.validFrom ?? ''} onChange={e => setEdit({ ...edit, validFrom: e.target.value || null })} /></label>
              <label>적용 종료일<input className="input" type="date" value={edit.validTo ?? ''} onChange={e => setEdit({ ...edit, validTo: e.target.value || null })} /></label>
              <label className="ck-chk"><input type="checkbox" checked={edit.required} onChange={e => setEdit({ ...edit, required: e.target.checked })} /> 필수</label>
              <label className="ck-chk"><input type="checkbox" checked={edit.allowNa} onChange={e => setEdit({ ...edit, allowNa: e.target.checked })} /> N/A 허용</label>
              <label className="ck-chk"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> 사용</label>
              <label className="wide">개정 사유<input className="input" value={edit.revisionNote} onChange={e => setEdit({ ...edit, revisionNote: e.target.value })} /></label>
              <label className="wide">비고<input className="input" value={edit.note} onChange={e => setEdit({ ...edit, note: e.target.value })} /></label>
            </div>
            <p className="ck-hint">문구를 고쳐도 이미 남은 점검 기록은 당시 문구 그대로입니다. 항목을 없애려면 '사용' 을 끄거나 적용 종료일을 넣으세요.</p>
            <div className="modal-actions">
              {edit.id > 0 && <button type="button" className="btn btn-ghost ck-danger" onClick={() => remove(edit)}>삭제</button>}
              <button type="button" className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
              <button className="btn btn-primary">저장</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

// ── QR 라벨 ──

function LabelsAdmin() {
  const [page, setPage] = useState<CheckQrPage | null>(null);
  const [zones, setZones] = useState<CheckZoneDef[]>([]);
  const [pick, setPick] = useState<Set<string>>(new Set());
  const [base, setBase] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async (first = false) => {
    const [q, z] = await Promise.all([api.get<CheckQrPage>('/api/checklist/qr'), api.get<CheckZoneDef[]>('/api/checklist/zones')]);
    // 테스트 서버처럼 이 서버 주소가 따로 정해져 있으면, 입력 칸에는 DB 에 저장된(운영이 쓰는) 주소를 보여 준다.
    setPage(q); setZones(z); setBase(q.overridden ? q.savedUrl : q.baseUrl);
    if (first) setPick(new Set(q.labels.map(x => x.code)));
  }, []);
  useEffect(() => { load(true).catch(e => alert(e instanceof Error ? e.message : 'QR 을 만들지 못했습니다.')); }, [load]);

  async function saveBase(value: string) {
    const v = value.trim().replace(/\/+$/, '');
    if (!/^https?:\/\/[^/\s]+$/.test(v)) { alert('http://10.10.10.119:8713 처럼 주소만 적어 주세요(뒤에 /c/… 는 자동으로 붙습니다).'); return; }
    setSaving(true);
    try { await api.put('/api/checklist/settings', { QrBaseUrl: v }); await load(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
    finally { setSaving(false); }
  }

  if (!page) return <div className="ck-empty">QR 을 만드는 중…</div>;
  const zone = (c: string) => zones.find(z => z.code === c);
  const toggle = (c: string) => setPick(p => { const s = new Set(p); if (s.has(c)) s.delete(c); else s.add(c); return s; });
  const labels = page.labels.filter(q => pick.has(q.code)).flatMap(q => Array.from({ length: Math.max(1, zone(q.code)?.qrCount ?? 1) }, (_, i) => ({ q, i })));

  return (
    <div>
      <div className={`ck-card ck-qrbase ck-noprint ${page.isLocal ? 'bad' : ''}`}>
        <div className="ck-qrbase-row">
          <label>QR 주소
            <input className="input" value={base} onChange={e => setBase(e.target.value)} placeholder="http://10.10.10.119:8713" />
          </label>
          <button className="btn btn-primary" disabled={saving || base.trim() === (page.overridden ? page.savedUrl : page.baseUrl)} onClick={() => saveBase(base)}>{saving ? '저장 중…' : '주소 저장'}</button>
        </div>
        {page.overridden && (
          <div className="ck-qrwarn ck-qrinfo">
            이 서버는 테스트 서버라 라벨에 <b>자기 주소({page.baseUrl})</b>를 씁니다(설정 파일·실행 스크립트에서 정함).
            위 칸에 저장하는 주소는 <b>운영 서버 라벨</b>에 쓰입니다 — 지금 저장된 값: <b>{page.savedUrl || '(없음)'}</b>
          </div>
        )}
        {page.overridden ? null : page.isLocal ? (
          <div className="ck-qrwarn">
            지금 주소(<b>{page.baseUrl}</b>)는 이 컴퓨터 자신을 가리켜 <b>휴대폰에서 열 수 없습니다</b>. 서버 주소로 바꿔 저장한 뒤 인쇄하세요.
            {page.suggestions.length > 0 && (
              <div className="ck-qrsug">
                이 서버 주소: {page.suggestions.map(s => (
                  <button key={s} className="btn btn-ghost ck-sm" onClick={() => { setBase(s); saveBase(s); }}>{s} 로 저장</button>
                ))}
              </div>
            )}
          </div>
        ) : (
          <div className="ck-hint">
            {page.fromSetting ? '저장된 주소입니다.' : '저장된 주소가 없어 지금 접속한 주소를 쓰고 있습니다 — 한 번 저장해 두세요.'}{' '}
            주소를 바꾸면 모든 라벨이 새 주소로 다시 만들어집니다(이미 붙인 QR 은 다시 인쇄해야 합니다).
            사내 DNS 이름(예: http://cleanpotal:8713)을 쓰면 서버 IP 가 바뀌어도 QR 을 다시 뽑지 않아도 됩니다.
          </div>
        )}
      </div>

      <div className="ck-bar-row ck-noprint">
        {page.labels.map(q => (
          <label key={q.code} className="ck-check"><input type="checkbox" checked={pick.has(q.code)} onChange={() => toggle(q.code)} /> {q.name}</label>
        ))}
        <button className="btn btn-primary" onClick={() => window.print()} disabled={labels.length === 0 || page.isLocal}>
          라벨 인쇄 ({labels.length}장)
        </button>
      </div>
      <p className="ck-hint ck-noprint">인쇄 전에 휴대폰(사내 와이파이)으로 화면의 QR 을 한 장 찍어 점검 화면이 열리는지 확인하세요. 구역 이름·부착 위치는 '구역' 탭에서 언제든 바꿀 수 있고 QR 은 그대로입니다.</p>
      <div className="ck-labels">
        {labels.map(({ q, i }) => (
          <div key={q.code + i} className="ck-label">
            <div className="ck-lqr" dangerouslySetInnerHTML={{ __html: q.svg }} />
            <div className="ck-ltext">
              <div className="ck-lline">{zone(q.code)?.line} · 3정 5S 점검</div>
              <div className="ck-lname">{q.name}</div>
              <div className="ck-lcode">{q.code}</div>
              <div className="ck-lhow">휴대폰 카메라로 찍어 점검하세요</div>
              <div className="ck-lurl">{q.url}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── 설정 ──

const SETTING_FIELDS: [string, string, string][] = [
  ['DayStart', '주간조 시작 시각', '07:00'],
  ['NightStart', '야간조 시작 시각', '17:30'],
  ['QrBaseUrl', 'QR 기본 주소', 'http://10.10.10.119:8713'],
  ['FormName', '양식 이름(리포트 제목)', '3정 5S 점검 Sheet'],
  ['Revision', '개정 번호', 'Rev.2'],
  ['EffectiveDate', '시행일(이날부터 미점검 표시)', '2026-10-01'],
];

function SettingsAdmin() {
  const [vals, setVals] = useState<Record<string, string> | null>(null);
  useEffect(() => { api.get<Record<string, string>>('/api/checklist/settings').then(setVals).catch(() => {}); }, []);
  if (!vals) return <div className="ck-empty">불러오는 중…</div>;
  async function save(e: React.FormEvent) {
    e.preventDefault();
    try { setVals(await api.put<Record<string, string>>('/api/checklist/settings', vals)); alert('저장했습니다.'); }
    catch (err) { alert(err instanceof Error ? err.message : '저장하지 못했습니다.'); }
  }
  return (
    <form className="ck-card ck-settings" onSubmit={save}>
      {SETTING_FIELDS.map(([k, label, ph]) => (
        <label key={k}>{label}
          <input className="input" type={k === 'EffectiveDate' ? 'date' : 'text'} placeholder={ph} value={vals[k] ?? ''}
            onChange={e => setVals({ ...vals, [k]: e.target.value })} />
        </label>
      ))}
      <p className="ck-hint">
        QR 기본 주소를 바꾸면 이미 붙인 QR 은 옛 주소를 가리킵니다 — 사내 DNS 이름(예: http://cleanpotal:8713)을 쓰면 서버 IP 가 바뀌어도 QR 을 다시 뽑지 않아도 됩니다.
      </p>
      <div className="modal-actions"><button className="btn btn-primary">저장</button></div>
    </form>
  );
}

// ── 엑셀 가져오기 ──

function ImportAdmin({ reload }: { reload: () => Promise<void> }) {
  const [parsed, setParsed] = useState<ParsedCheckWorkbook | null>(null);
  const [fileName, setFileName] = useState('');
  const [result, setResult] = useState<CheckImportResult | null>(null);
  const [busy, setBusy] = useState(false);

  async function read(f: File | undefined) {
    if (!f) return;
    setResult(null);
    try { setParsed(await parseCheckWorkbook(f)); setFileName(f.name); }
    catch (e) { alert(e instanceof Error ? e.message : '파일을 읽지 못했습니다.'); }
  }
  async function run() {
    if (!parsed) return;
    setBusy(true);
    try {
      setResult(await api.post<CheckImportResult>('/api/checklist/import', { zones: parsed.zones, items: parsed.items }));
      await reload();
    } catch (e) { alert(e instanceof Error ? e.message : '가져오지 못했습니다.'); }
    finally { setBusy(false); }
  }
  return (
    <div className="ck-card">
      <p>"체크시트_QR_양식입력.xlsx" 양식 파일을 올리면 <b>2_구역</b>·<b>3_점검항목</b> 시트를 읽어 코드 기준으로 넣거나 고칩니다. 파일에 없는 구역·항목은 그대로 둡니다. N-METAL 도 같은 파일에 이어 적어 올리면 됩니다.</p>
      <input type="file" accept=".xlsx" onChange={e => { read(e.target.files?.[0]); e.target.value = ''; }} />
      {parsed && (
        <div className="ck-import">
          <div><b>{fileName}</b> — 구역 {parsed.zones.length}개, 항목 {parsed.items.length}개</div>
          <div className="ck-dim">라인: {[...new Set(parsed.zones.map(z => z.line))].join(', ')}</div>
          {parsed.notes.length > 0 && <ul>{parsed.notes.map(n => <li key={n}>{n}</li>)}</ul>}
          <button className="btn btn-primary" onClick={run} disabled={busy}>{busy ? '가져오는 중…' : '가져오기'}</button>
        </div>
      )}
      {result && (
        <div className="ck-import">
          <div>구역 추가 {result.zonesAdded} · 수정 {result.zonesUpdated} / 항목 추가 {result.itemsAdded} · 수정 {result.itemsUpdated}</div>
          {result.warnings.length > 0 && <ul className="ck-warn">{result.warnings.map(w => <li key={w}>{w}</li>)}</ul>}
        </div>
      )}
    </div>
  );
}
