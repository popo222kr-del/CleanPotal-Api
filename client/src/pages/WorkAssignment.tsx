import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { WorkMember, WorkMemberDetail, WorkAccount, WorkEdu } from '../api/types';
import '../styles/member-list.css';   // 사용자 계정 관리와 같은 인원 목록 패널
import './WorkAssignment.css';

export default function WorkAssignment() {
  const { canEditOffice: canEdit } = useAccess();
  const [members, setMembers] = useState<WorkMember[]>([]);
  const [includeHidden, setIncludeHidden] = useState(false);
  const [tab, setTab] = useState<'active' | 'resigned'>('active');
  const [search, setSearch] = useState('');
  const [sel, setSel] = useState<string | null>(null);
  const [detail, setDetail] = useState<WorkMemberDetail | null>(null);
  const [acc, setAcc] = useState<WorkAccount | 'new' | null>(null);
  const [edu, setEdu] = useState<WorkEdu | 'new' | null>(null);

  const loadMembers = useCallback(async () => {
    setMembers(await api.get<WorkMember[]>(`/api/workassignment/members?includeHidden=${includeHidden}`));
  }, [includeHidden]);
  useEffect(() => { loadMembers(); }, [loadMembers]);

  const loadDetail = useCallback(async (u: string) => {
    setDetail(await api.get<WorkMemberDetail>(`/api/workassignment/members/${encodeURIComponent(u)}`));
  }, []);
  useEffect(() => { if (sel) loadDetail(sel); else setDetail(null); }, [sel, loadDetail]);

  async function addMember() {
    if (!canEdit) return;
    const username = prompt('추가할 인원의 아이디(Username)를 입력하세요');
    if (!username) return;
    await api.post('/api/workassignment/members', { username, isHidden: false, resignDate: '' });
    loadMembers();
  }
  async function toggleHidden(m: WorkMember) {
    await api.put(`/api/workassignment/members/${m.id}`, { username: m.username, isHidden: !m.isHidden, resignDate: m.resignDate });
    loadMembers();
  }
  async function delMember(m: WorkMember) {
    if (!canEdit) return;
    if (!confirm(`'${m.realName}' 인원과 계정·교육이수를 모두 삭제할까요?`)) return;
    await api.del(`/api/workassignment/members/${m.id}`);
    if (sel === m.username) setSel(null);
    loadMembers();
  }

  // 목록은 사용자 계정 관리와 같은 방식으로 나눈다 — 재직/퇴사 탭 + 검색.
  // 재직 여부는 계정(User.isResigned)이 정본이라 두 화면의 인원수가 어긋나지 않는다.
  const active = members.filter(m => !m.isResigned);
  const resigned = members.filter(m => m.isResigned);
  let list = tab === 'active' ? active : resigned;
  if (search.trim()) {
    const q = search.trim().toLowerCase();
    list = list.filter(m =>
      m.realName.toLowerCase().includes(q) ||
      m.employeeNumber.toLowerCase().includes(q) ||
      m.teamName.toLowerCase().includes(q) ||
      m.department.toLowerCase().includes(q));
  }
  const selMember = members.find(m => m.username === sel) ?? null;

  return (
    <div>
      <header className="pg-header">
        <div><h2>개인별 업무 분장표</h2></div>
        <label className="wa-toggle"><input type="checkbox" checked={includeHidden} onChange={e => setIncludeHidden(e.target.checked)} /> 숨김 포함</label>
      </header>
      <div className="pg-body">
        <div className="um-layout">
          <div className="um-left">
            <div className="um-tabs">
              <button className={tab === 'active' ? 'active' : ''} onClick={() => setTab('active')}>재직 중 <span>{active.length}</span></button>
              <button className={tab === 'resigned' ? 'active' : ''} onClick={() => setTab('resigned')}>퇴사자 <span>{resigned.length}</span></button>
            </div>
            <input className="input um-search" placeholder="검색…" value={search} onChange={e => setSearch(e.target.value)} />
            <div className="um-list">
              {list.map(m => (
                <div key={m.id} className={`um-item ${sel === m.username ? 'active' : ''}`} onClick={() => setSel(m.username)}>
                  <div className="um-avatar">{m.realName[0] ?? '?'}</div>
                  <div className="um-info">
                    <div className="um-name">
                      {m.realName}
                      {m.isHidden && <span className="wa-tag">숨김</span>}
                      {!m.hasAccount && <span className="wa-tag warn">계정 없음</span>}
                    </div>
                    <div className="um-meta">{[m.department, m.teamName, m.jobTitle].filter(Boolean).join(' · ') || '-'}</div>
                  </div>
                  <div className="um-uid">{m.employeeNumber}</div>
                </div>
              ))}
              {list.length === 0 && <div className="um-no">인원이 없습니다</div>}
            </div>
            {canEdit && tab === 'active' && <button className="btn btn-primary um-add" onClick={addMember}>+ 인원 추가</button>}
          </div>

          <div className="um-right">
            {!detail && <div className="wa-none"><div style={{ fontSize: 34 }}>🗂️</div><p>인원을 선택하세요</p></div>}
            {detail && (
              <>
                <div className="wa-dhead">
                  <div className="um-avatar lg">{detail.member.realName[0] ?? '?'}</div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div className="wa-dname">
                      {detail.member.realName}
                      {detail.member.isResigned && <span className="wa-tag warn">퇴사{detail.member.resignDate && ` ${detail.member.resignDate}`}</span>}
                    </div>
                    <div className="wa-dmeta">
                      {[detail.member.department, detail.member.teamName, detail.member.jobTitle].filter(Boolean).join(' · ') || '-'}
                      {' · '}{detail.member.employeeNumber}
                    </div>
                  </div>
                  {canEdit && selMember && (
                    <div className="wa-dbtns">
                      <button className="btn btn-ghost" onClick={() => toggleHidden(selMember)}>
                        {selMember.isHidden ? '목록에 표시' : '목록에서 숨김'}
                      </button>
                      <button className="btn btn-ghost wa-danger" onClick={() => delMember(selMember)}>삭제</button>
                    </div>
                  )}
                </div>

                <div className="wa-sec">
                  <div className="wa-sec-h">계정 <button className="btn btn-ghost wa-add" onClick={() => setAcc('new')}>+ 계정</button></div>
                  <table className="pm-table">
                    <thead><tr><th>서비스</th><th>아이디</th><th>비밀번호</th><th>비고</th><th></th></tr></thead>
                    <tbody>
                      {detail.accounts.length === 0 && <tr><td colSpan={5} className="pm-empty">계정 없음</td></tr>}
                      {detail.accounts.map(a => (
                        <tr key={a.id}>
                          <td>{a.serviceName}</td><td>{a.accountId}</td><td className="wa-pw">{a.accountPassword}</td><td>{a.note}</td>
                          <td className="wa-row-btns"><button className="wa-mini" onClick={() => setAcc(a)}>수정</button><button className="wa-mini del" onClick={async () => { if (confirm('삭제?')) { await api.del(`/api/workassignment/accounts/${a.id}`); loadDetail(sel!); } }}>✕</button></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <div className="wa-sec">
                  <div className="wa-sec-h">교육 이수 <button className="btn btn-ghost wa-add" onClick={() => setEdu('new')}>+ 교육</button></div>
                  <table className="pm-table">
                    <thead><tr><th>교육명</th><th>교육 일자</th><th>강사</th><th>비고</th><th></th></tr></thead>
                    <tbody>
                      {detail.edus.length === 0 && <tr><td colSpan={5} className="pm-empty">교육이수 없음</td></tr>}
                      {detail.edus.map(e => (
                        <tr key={e.id}>
                          <td>{e.eduName}</td><td>{e.eduDateText}</td><td>{e.instructor}</td><td>{e.note}</td>
                          <td className="wa-row-btns"><button className="wa-mini" onClick={() => setEdu(e)}>수정</button><button className="wa-mini del" onClick={async () => { if (confirm('삭제?')) { await api.del(`/api/workassignment/edus/${e.id}`); loadDetail(sel!); } }}>✕</button></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </div>
        </div>
      </div>

      {acc && sel && <AccountModal username={sel} account={acc === 'new' ? null : acc} onClose={() => setAcc(null)} onSaved={() => { setAcc(null); loadDetail(sel); }} />}
      {edu && sel && <EduModal username={sel} edu={edu === 'new' ? null : edu} onClose={() => setEdu(null)} onSaved={() => { setEdu(null); loadDetail(sel); }} />}
    </div>
  );
}

function AccountModal({ username, account, onClose, onSaved }: { username: string; account: WorkAccount | null; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ serviceName: account?.serviceName ?? '', accountId: account?.accountId ?? '', accountPassword: account?.accountPassword ?? '', note: account?.note ?? '' });
  async function save() {
    const body = { username, ...f };
    if (account) await api.put(`/api/workassignment/accounts/${account.id}`, body);
    else await api.post('/api/workassignment/accounts', body);
    onSaved();
  }
  return (
    <Modal title={account ? '계정 수정' : '계정 추가'} onClose={onClose} onSave={save}>
      <FF l="서비스명"><input className="input" value={f.serviceName} onChange={e => setF({ ...f, serviceName: e.target.value })} /></FF>
      <FF l="아이디"><input className="input" value={f.accountId} onChange={e => setF({ ...f, accountId: e.target.value })} /></FF>
      <FF l="비밀번호"><input className="input" value={f.accountPassword} onChange={e => setF({ ...f, accountPassword: e.target.value })} /></FF>
      <FF l="비고"><input className="input" value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></FF>
    </Modal>
  );
}

function EduModal({ username, edu, onClose, onSaved }: { username: string; edu: WorkEdu | null; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ eduName: edu?.eduName ?? '', eduDate: edu?.eduDate ?? '', instructor: edu?.instructor ?? '', note: edu?.note ?? '', startDate: edu?.startDate ?? '', endDate: edu?.endDate ?? '' });
  async function save() {
    const body = { username, ...f };
    if (edu) await api.put(`/api/workassignment/edus/${edu.id}`, body);
    else await api.post('/api/workassignment/edus', body);
    onSaved();
  }
  return (
    <Modal title={edu ? '교육이수 수정' : '교육이수 추가'} onClose={onClose} onSave={save}>
      <FF l="교육명"><input className="input" value={f.eduName} onChange={e => setF({ ...f, eduName: e.target.value })} /></FF>
      {/* WPF 와 동일하게 시작일·종료일로 입력받는다. 하루짜리면 종료일은 비워 둔다. */}
      <FF l="시작일"><input className="input" type="date" value={f.startDate} onChange={e => setF({ ...f, startDate: e.target.value })} /></FF>
      <FF l="종료일 (여러 날 진행한 경우)"><input className="input" type="date" value={f.endDate} onChange={e => setF({ ...f, endDate: e.target.value })} /></FF>
      <FF l="강사"><input className="input" value={f.instructor} onChange={e => setF({ ...f, instructor: e.target.value })} /></FF>
      <FF l="비고"><input className="input" value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></FF>
    </Modal>
  );
}

function Modal({ title, children, onClose, onSave }: { title: string; children: React.ReactNode; onClose: () => void; onSave: () => void }) {
  return (
    <div className="pm-modal-bg" onClick={onClose}>
      <div className="pm-modal" onClick={e => e.stopPropagation()}>
        <div className="pm-modal-head"><h3>{title}</h3><button className="pm-x" onClick={onClose}>✕</button></div>
        <div className="pm-modal-body">{children}</div>
        <div className="pm-modal-foot"><div style={{ flex: 1 }} /><button className="btn btn-ghost" onClick={onClose}>취소</button><button className="btn btn-primary" onClick={onSave}>저장</button></div>
      </div>
    </div>
  );
}

function FF({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="pm-field"><label>{l}</label>{children}</div>;
}
