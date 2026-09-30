import { useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import type { WorkForm } from '../../api/types';
import { attName, filesToAtts, parseRef, saveAtt } from '../attach';

// 양식 다운로드 — 웹으로 옮기지 않고 엑셀 양식 그대로 쓰는 업무 파일(4·6·10·11번 등).
// 받기는 누구나(OFFICE 조회), 양식 올리기·바꾸기는 OFFICE 편집 권한. 파일은 첨부 보관소 '양식' 폴더에 둔다.

const EXT_TONE: Record<string, string> = { xlsx: 'xls', xlsm: 'xls', xls: 'xls', docx: 'doc', doc: 'doc', hwp: 'hwp', hwpx: 'hwp', pdf: 'pdf', pptx: 'ppt' };
const ext = (name: string) => (/\.([a-z0-9]+)$/i.exec(name)?.[1] ?? '').toLowerCase();

export default function Forms() {
  const { canEditField: canEdit } = useAccess();
  const [forms, setForms] = useState<WorkForm[] | null>(null);
  const [draft, setDraft] = useState<WorkForm[] | null>(null);   // 관리 중인 목록
  const [saving, setSaving] = useState(false);
  const [upIdx, setUpIdx] = useState<number | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => { api.get<WorkForm[]>('/api/worklog/forms').then(setForms).catch(() => setForms([])); }, []);

  function set(i: number, patch: Partial<WorkForm>) { setDraft(d => d?.map((f, j) => (j === i ? { ...f, ...patch } : f)) ?? d); }
  function move(i: number, by: number) {
    setDraft(d => {
      if (!d || i + by < 0 || i + by >= d.length) return d;
      const n = [...d]; [n[i], n[i + by]] = [n[i + by], n[i]]; return n;
    });
  }
  async function upload(f: File) {
    if (upIdx === null || !draft) return;
    const [ref] = await filesToAtts([f], { scope: 'office', cat: '양식', label: draft[upIdx].title || f.name });
    if (ref) set(upIdx, { fileRef: ref, title: draft[upIdx].title || f.name.replace(/\.[^.]+$/, '') });
    setUpIdx(null);
  }
  async function save() {
    if (!draft) return;
    if (draft.some(f => !f.title.trim())) { alert('이름이 빈 양식이 있습니다.'); return; }
    setSaving(true);
    try {
      const items = draft.map(f => ({ id: f.id, no: f.no, title: f.title, description: f.description, fileRef: f.fileRef }));
      setForms(await api.put<WorkForm[]>('/api/worklog/forms', { items }));
      setDraft(null);
    }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); } finally { setSaving(false); }
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>양식 다운로드</h2>
          <p>엑셀 양식 그대로 쓰는 업무 파일 — 내려받아 작성하세요</p>
        </div>
        {canEdit && !draft && <button className="btn btn-ghost" onClick={() => setDraft(forms ?? [])}>양식 관리</button>}
      </header>
      <div className="pg-body">
        {!forms ? <div className="wf-empty">불러오는 중…</div> : draft ? (
          <div className="wf-bblock">
            <div className="wf-eq-wrap wf-fedit">
              <table className="wf-eq-table">
                <thead><tr><th>번호</th><th>이름</th><th>설명</th><th>파일</th><th /></tr></thead>
                <tbody>
                  {draft.map((f, i) => (
                    <tr key={f.id || `n${i}`}>
                      <td style={{ width: 64 }}><input className="input" value={f.no} placeholder="4" onChange={e => set(i, { no: e.target.value })} /></td>
                      <td><input className="input" value={f.title} placeholder="양식 이름" onChange={e => set(i, { title: e.target.value })} /></td>
                      <td><input className="input" value={f.description} placeholder="(선택) 언제·어떻게 쓰는지" onChange={e => set(i, { description: e.target.value })} /></td>
                      <td className="wf-ffile">
                        {f.fileRef ? <span title={attName(f.fileRef)}>{attName(f.fileRef)}</span> : <span className="wf-dim">파일 없음</span>}
                        <button className="wf-link" onClick={() => { setUpIdx(i); fileRef.current?.click(); }}>{f.fileRef ? '바꾸기' : '올리기'}</button>
                      </td>
                      <td className="wf-eq-move">
                        <button onClick={() => move(i, -1)} disabled={i === 0} aria-label="위로">▲</button>
                        <button onClick={() => move(i, 1)} disabled={i === draft.length - 1} aria-label="아래로">▼</button>
                        <button className="wf-link wf-danger" onClick={() => confirm(`'${f.title || '이 양식'}' 을 목록에서 뺄까요?`) && setDraft(d => d?.filter((_, j) => j !== i) ?? d)}>빼기</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <input ref={fileRef} type="file" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void upload(f); e.target.value = ''; }} />
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setDraft(d => [...(d ?? []), { id: 0, no: '', title: '', description: '', fileRef: '', updatedBy: '', updatedAt: '' }])}>+ 양식</button>
              <span style={{ flex: 1 }} />
              <button className="btn btn-ghost" onClick={() => setDraft(null)}>취소</button>
              <button className="btn btn-primary" disabled={saving} onClick={save}>{saving ? '저장 중…' : '저장'}</button>
            </div>
          </div>
        ) : forms.length === 0 ? (
          <div className="wf-empty">등록된 양식이 없습니다.{canEdit ? ' 오른쪽 위 "양식 관리" 에서 파일을 올리세요.' : ''}</div>
        ) : (
          <div className="wf-forms">
            {forms.map(f => {
              const ref = parseRef(f.fileRef);
              const name = f.fileRef ? attName(f.fileRef) : '';
              return (
                <button key={f.id} className="wf-form" disabled={!ref} onClick={() => ref && saveAtt(f.fileRef)} title={ref ? `${name} 받기` : '파일이 아직 없습니다'}>
                  <span className={`wf-fext ${EXT_TONE[ext(name)] ?? ''}`}>{ext(name).toUpperCase() || '—'}</span>
                  <span className="wf-fbody">
                    <b>{f.no && <em>{f.no}</em>}{f.title}</b>
                    {f.description && <span>{f.description}</span>}
                    <small>{ref ? name : '파일 없음'}{f.updatedAt && ref ? ` · ${f.updatedAt.slice(0, 10)}` : ''}</small>
                  </span>
                  {ref && <span className="wf-fdown">받기</span>}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
