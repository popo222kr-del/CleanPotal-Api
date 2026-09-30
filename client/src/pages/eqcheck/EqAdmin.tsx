import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { EqCheckItem, EqCheckTemplate, EqCheckUnit, EqCycle, EqInputType } from '../../api/types';
import { CYCLES } from './eqCommon';

// 양식 관리(관리자) — 설비 유형별 점검 항목, 호기별 양식, 월간 점검 부서·팀, 호기 QR 라벨.

const INPUTS: [EqInputType, string][] = [['OXA', 'O/△/X'], ['CHOICE', '보기'], ['NUM', '수치'], ['MULTI', '수치 여러 칸']];

const blank = (templateId: number, cycle: EqCycle): EqCheckItem => ({
  id: 0, templateId, cycle, sortOrder: 0, category: '', name: '', point: '', spec: '', inputType: cycle === '일상' ? 'OXA' : 'CHOICE',
  options: cycle === '일상' ? '' : '양호|불량', fields: '', unit: '', min: null, max: null, runOnly: false, isActive: true,
});

export default function EqAdmin() {
  const [tpls, setTpls] = useState<EqCheckTemplate[]>([]);
  const [units, setUnits] = useState<EqCheckUnit[]>([]);
  const [sel, setSel] = useState(0);
  const [teams, setTeams] = useState('');
  const [edit, setEdit] = useState<EqCheckItem | null>(null);
  const [qr, setQr] = useState<{ code: string; name: string; url: string; svg: string }[] | null>(null);

  const load = useCallback(async () => {
    const [t, u, s] = await Promise.all([
      api.get<EqCheckTemplate[]>('/api/eqcheck/templates'),
      api.get<EqCheckUnit[]>('/api/eqcheck/units'),
      api.get<{ monthlyTeams: string }>('/api/eqcheck/settings'),
    ]);
    setTpls(t); setUnits(u); setTeams(s.monthlyTeams);
    setSel(cur => cur || t[0]?.id || 0);
  }, []);
  useEffect(() => { load().catch(() => {}); }, [load]);

  const tpl = tpls.find(t => t.id === sel);

  async function saveTeams() {
    try { const r = await api.put<{ monthlyTeams: string }>('/api/eqcheck/settings', { monthlyTeams: teams }); setTeams(r.monthlyTeams); alert('저장했습니다.'); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }
  async function saveItem(i: EqCheckItem) {
    try { await api.put('/api/eqcheck/items', i); setEdit(null); await load(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }
  async function delItem(i: EqCheckItem) {
    if (!confirm(`'${i.name}${i.point ? ' ' + i.point : ''}' 항목을 지울까요?\n(점검 기록이 있으면 끄기만 합니다)`)) return;
    await api.del(`/api/eqcheck/items/${i.id}`); await load();
  }
  async function move(list: EqCheckItem[], idx: number, dir: -1 | 1) {
    const arr = [...list]; const j = idx + dir;
    if (j < 0 || j >= arr.length) return;
    [arr[idx], arr[j]] = [arr[j], arr[idx]];
    await api.post('/api/eqcheck/items/reorder', arr.map(x => x.id)); await load();
  }
  async function saveUnit(code: string, templateId: number, isActive: boolean) {
    try { await api.put('/api/eqcheck/units', { code, templateId, isActive, note: '' }); await load(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }
  async function addUnit() {
    const code = prompt('설비 호기 코드 (설비 목록 이름에서 챔버 번호를 뗀 것 — 예: MDC11, MBO05)')?.trim();
    if (!code || !tpl) return;
    await saveUnit(code, tpl.id, true);
  }
  async function addTemplate() {
    const code = prompt('새 양식 코드 (영문, 예: DIP2)')?.trim();
    const name = code && prompt('양식 이름 (예: DIP CLEANER (신형))')?.trim();
    if (!code || !name) return;
    try { const t = await api.put<EqCheckTemplate>('/api/eqcheck/templates', { id: 0, code, name, note: '', isActive: true }); await load(); setSel(t.id); }
    catch (e) { alert(e instanceof Error ? e.message : '만들지 못했습니다.'); }
  }
  async function showQr() {
    try { const r = await api.get<{ labels: { code: string; name: string; url: string; svg: string }[] }>('/api/eqcheck/qr'); setQr(r.labels); }
    catch (e) { alert(e instanceof Error ? e.message : 'QR 을 만들지 못했습니다.'); }
  }

  if (qr) {
    return (
      <>
        <div className="ck-bar-row ck-noprint">
          <button className="btn btn-ghost ck-sm" onClick={() => setQr(null)}>← 양식 관리</button>
          <button className="btn btn-primary ck-sm" onClick={() => window.print()}>인쇄</button>
          <span className="ck-hint">QR 주소는 체크시트(현장) 양식 관리의 QR 기본 주소를 같이 씁니다.</span>
        </div>
        <div className="ec-qr-grid">
          {qr.map(l => (
            <div key={l.code} className="ec-qr">
              <div className="svg" dangerouslySetInnerHTML={{ __html: l.svg }} />
              <b>{l.code}</b><span>{l.name}</span><small>체크시트 (설비)</small>
            </div>
          ))}
        </div>
      </>
    );
  }

  return (
    <>
      <div className="ck-card">
        <h3>월간 점검 담당</h3>
        <div className="ck-bar-row">
          <input className="input" style={{ maxWidth: 320 }} value={teams} onChange={e => setTeams(e.target.value)} placeholder="예: 설비팀" />
          <button className="btn btn-primary ck-sm" onClick={saveTeams}>저장</button>
          <button className="btn btn-ghost ck-sm" onClick={showQr}>호기 QR 라벨</button>
        </div>
        <p className="ck-hint" style={{ margin: 0 }}>부서나 팀 이름에 이 글자가 들어간 사람만 월간 점검을 적고 NG 조치를 완료할 수 있습니다(쉼표로 여러 개, 관리자는 언제나). 일상·주간 점검은 체크시트(설비) 조회 권한이 있으면 누구나 합니다.</p>
      </div>

      <div className="ec-admin">
        <aside>
          {tpls.map(t => (
            <button key={t.id} className={`${t.id === sel ? 'on' : ''} ${t.isActive ? '' : 'off'}`} onClick={() => setSel(t.id)}>
              <b>{t.name}</b><small>{t.units.join(' · ') || '호기 없음'}</small>
            </button>
          ))}
          <button className="add" onClick={addTemplate}>+ 양식 추가</button>
        </aside>
        {tpl && (
          <section>
            <div className="ec-admin-head">
              <h3>{tpl.name} <small className="ck-code">{tpl.code}</small></h3>
              <div className="ec-units">
                {units.filter(u => u.templateId === tpl.id).map(u => (
                  <span key={u.code} className={`ck-tag ${u.isActive ? '' : 'warn'}`} title={u.inList ? '' : '설비 목록(스케줄 보드 설비)에 없는 코드'}>
                    {u.code}{!u.inList && ' ⚠'}
                    <button className="ec-x" title={u.isActive ? '이 호기 점검 끄기' : '다시 켜기'} onClick={() => saveUnit(u.code, u.templateId, !u.isActive)}>{u.isActive ? '×' : '↺'}</button>
                  </span>
                ))}
                <button className="btn btn-ghost ck-sm" onClick={addUnit}>+ 호기</button>
                <select className="input ck-sm" style={{ width: 'auto' }} value="" onChange={e => {
                  const u = units.find(x => x.code === e.target.value);
                  if (u) saveUnit(u.code, tpl.id, true);
                }}>
                  <option value="">다른 양식에서 옮기기…</option>
                  {units.filter(u => u.templateId !== tpl.id).map(u => <option key={u.code} value={u.code}>{u.code} ({u.templateName})</option>)}
                </select>
              </div>
            </div>
            {CYCLES.map(c => {
              const list = tpl.items.filter(i => i.cycle === c);
              return (
                <div key={c} className="ec-admin-cycle">
                  <h4>{c} 점검 <small>{list.filter(i => i.isActive).length}개</small>
                    <button className="btn btn-ghost ck-sm" onClick={() => setEdit(blank(tpl.id, c))}>+ 항목</button></h4>
                  <table className="ec-table ec-items">
                    <thead><tr><th /><th>대분류</th><th>항목</th><th>판정 기준</th><th>입력</th><th /></tr></thead>
                    <tbody>
                      {list.map((i, n) => (
                        <tr key={i.id} className={i.isActive ? '' : 'off'}>
                          <td className="ec-move"><button onClick={() => move(list, n, -1)} disabled={n === 0}>▲</button><button onClick={() => move(list, n, 1)} disabled={n === list.length - 1}>▼</button></td>
                          <td className="ck-dim">{i.category}</td>
                          <td><b>{i.name}</b>{i.point && <small> {i.point}</small>}</td>
                          <td className="ck-dim">{i.spec}</td>
                          <td className="nowrap">{INPUTS.find(x => x[0] === i.inputType)?.[1]}
                            {i.inputType === 'CHOICE' && <small className="ck-dim"> {i.options.replaceAll('|', ' / ')}</small>}
                            {i.inputType === 'MULTI' && <small className="ck-dim"> {i.fields.replaceAll('|', ' / ')}</small>}
                            {i.runOnly && <span className="ck-tag">가동 중만</span>}</td>
                          <td className="nowrap"><button className="ec-link" onClick={() => setEdit(i)}>수정</button> <button className="ec-link" onClick={() => delItem(i)}>삭제</button></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              );
            })}
          </section>
        )}
      </div>
      {edit && <ItemEditor item={edit} onClose={() => setEdit(null)} onSave={saveItem} />}
    </>
  );
}

function ItemEditor({ item, onClose, onSave }: { item: EqCheckItem; onClose: () => void; onSave: (i: EqCheckItem) => void }) {
  const [v, setV] = useState(item);
  const set = (p: Partial<EqCheckItem>) => setV(x => ({ ...x, ...p }));
  const num = (s: string) => (s.trim() === '' ? null : Number(s));
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box ec-edit">
        <h3>{item.id ? '항목 수정' : '항목 추가'} <small>{v.cycle}</small></h3>
        <div className="ec-form">
          <label>주기<select className="input" value={v.cycle} onChange={e => set({ cycle: e.target.value as EqCycle })}>{CYCLES.map(c => <option key={c}>{c}</option>)}</select></label>
          <label>대분류<input className="input" value={v.category} onChange={e => set({ category: e.target.value })} placeholder="예: 게이지" /></label>
          <label>항목<input className="input" value={v.name} onChange={e => set({ name: e.target.value })} /></label>
          <label>위치<input className="input" value={v.point} onChange={e => set({ point: e.target.value })} placeholder="예: #1 DI Bath, L, R (없으면 비움)" /></label>
          <label className="w">판정 기준<input className="input" value={v.spec} onChange={e => set({ spec: e.target.value })} /></label>
          <label>입력<select className="input" value={v.inputType} onChange={e => set({ inputType: e.target.value as EqInputType })}>{INPUTS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
          {v.inputType === 'CHOICE' && <label className="w">보기 <small>| 로 나눔 · 첫 보기가 정상 · 앞에 * 는 '조치함'(NG 로 남고 바로 조치 완료)</small>
            <input className="input" value={v.options} onChange={e => set({ options: e.target.value })} placeholder="예: 양호|*위치조정|불량" /></label>}
          {v.inputType === 'MULTI' && <label className="w">칸 이름 <small>| 로 나눔 · 첫 칸과의 편차로 판정</small>
            <input className="input" value={v.fields} onChange={e => set({ fields: e.target.value })} placeholder="예: Set|Real" /></label>}
          {(v.inputType === 'NUM' || v.inputType === 'MULTI') && <>
            <label>단위<input className="input" value={v.unit} onChange={e => set({ unit: e.target.value })} /></label>
            {v.inputType === 'NUM' && <label>하한<input className="input" inputMode="decimal" value={v.min ?? ''} onChange={e => set({ min: num(e.target.value) })} /></label>}
            <label>{v.inputType === 'NUM' ? '상한' : '허용 편차(±)'}<input className="input" inputMode="decimal" value={v.max ?? ''} onChange={e => set({ max: num(e.target.value) })} /></label>
            <label className="chk"><input type="checkbox" checked={v.runOnly} onChange={e => set({ runOnly: e.target.checked })} /> 가동 중에만 잼('비가동' 가능)</label>
          </>}
          <label className="chk"><input type="checkbox" checked={v.isActive} onChange={e => set({ isActive: e.target.checked })} /> 사용</label>
        </div>
        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" onClick={() => onSave(v)}>저장</button>
        </div>
      </div>
    </div>
  );
}
