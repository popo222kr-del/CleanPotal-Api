import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { WorkMember, WorkMemberDetail, WorkAccount, WorkEdu } from '../api/types';
import '../styles/member-list.css';   // 사용자 계정 관리와 같은 인원 목록 패널
import './WorkAssignment.css';

/** 표에서 편집 중인 한 줄. key 는 React 용이고, id 가 0 이면 아직 저장되지 않은 새 줄이다. */
type EduRow = {
  key: string; id: number;
  eduName: string; startDate: string; endDate: string; instructor: string; note: string;
};

function toRow(e: WorkEdu): EduRow {
  // 저장된 값을 그대로 들고 온다. 날짜 표기가 섞여 있어도(2018.06.04 등)
  // 임의로 비우지 않는다 — 비운 채로 저장하면 실제 값이 지워진다.
  return {
    key: `e${e.id}`, id: e.id, eduName: e.eduName,
    startDate: (e.startDate ?? '').trim(), endDate: (e.endDate ?? '').trim(),
    instructor: e.instructor, note: e.note,
  };
}

const YMD = /^\d{4}-\d{2}-\d{2}$/;

/**
 * 날짜 칸. 브라우저 날짜 선택기는 yyyy-MM-dd 만 받으므로,
 * 그 형식이 아닌 기존 값은 글자 그대로 보여주고 고칠 수 있게 둔다.
 * (선택기로 바꾸면 화면에 빈칸이 뜨고, 그대로 저장하면 원래 날짜가 사라진다)
 */
function DateCell({ value, readOnly, onChange }: { value: string; readOnly: boolean; onChange: (v: string) => void }) {
  const usePicker = value === '' || YMD.test(value);
  return (
    <input
      className="wa-cell"
      type={usePicker ? 'date' : 'text'}
      value={value}
      readOnly={readOnly}
      title={usePicker ? undefined : '표기 형식이 달라 직접 입력합니다 (예: 2018-06-04)'}
      onChange={e => onChange(e.target.value)}
    />
  );
}

export default function WorkAssignment() {
  const { canEditOffice: canEdit } = useAccess();
  const [members, setMembers] = useState<WorkMember[]>([]);
  const [includeHidden, setIncludeHidden] = useState(false);
  const [tab, setTab] = useState<'active' | 'resigned'>('active');
  const [search, setSearch] = useState('');
  const [sel, setSel] = useState<string | null>(null);
  const [detail, setDetail] = useState<WorkMemberDetail | null>(null);
  const [acc, setAcc] = useState<WorkAccount | 'new' | null>(null);
  // 기본 교육 기록은 표에서 바로 고치고 한 번에 저장한다(WPF 와 같은 방식).
  const [eduRows, setEduRows] = useState<EduRow[]>([]);
  const [eduDirty, setEduDirty] = useState(false);
  const [eduSaving, setEduSaving] = useState(false);
  const [copyOpen, setCopyOpen] = useState(false);

  // 숨김 인원까지 전부 받아온다. 서버에서 먼저 걸러내면
  // '숨김 처리된 퇴사자'가 퇴사자 탭에서도 사라진다.
  const loadMembers = useCallback(async () => {
    setMembers(await api.get<WorkMember[]>('/api/workassignment/members?includeHidden=true'));
  }, []);
  useEffect(() => { loadMembers(); }, [loadMembers]);

  const loadDetail = useCallback(async (u: string) => {
    const d = await api.get<WorkMemberDetail>(`/api/workassignment/members/${encodeURIComponent(u)}`);
    setDetail(d);
    setEduRows(d.edus.map(toRow));
    setEduDirty(false);
  }, []);
  useEffect(() => { if (sel) loadDetail(sel); else setDetail(null); }, [sel, loadDetail]);

  function setEduRow(i: number, patch: Partial<EduRow>) {
    setEduRows(rows => rows.map((r, idx) => idx === i ? { ...r, ...patch } : r));
    setEduDirty(true);
  }
  function addEduRow() {
    setEduRows(rows => [...rows, { key: `new-${Date.now()}-${rows.length}`, id: 0, eduName: '', startDate: '', endDate: '', instructor: '', note: '' }]);
    setEduDirty(true);
  }
  function removeEduRow(i: number) {
    setEduRows(rows => rows.filter((_, idx) => idx !== i));
    setEduDirty(true);
  }
  async function saveEdus() {
    if (!sel || eduSaving) return;
    setEduSaving(true);
    try {
      // 보낸 목록이 곧 최종 상태다 — 지운 줄은 서버에서 함께 삭제된다.
      const rows = eduRows.map(r => ({
        id: r.id, eduName: r.eduName, startDate: r.startDate, endDate: r.endDate,
        instructor: r.instructor, note: r.note,
      }));
      const saved = await api.put<WorkEdu[]>('/api/workassignment/edus/bulk', { username: sel, rows });
      setEduRows(saved.map(toRow));
      setEduDirty(false);
      setDetail(d => d ? { ...d, edus: saved } : d);
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
      loadDetail(sel);   // 서버 최신 상태로 되돌린다
    } finally {
      setEduSaving(false);
    }
  }
  async function copyEdusFrom(fromUsername: string) {
    if (!sel) return;
    try {
      const saved = await api.post<WorkEdu[]>('/api/workassignment/edus/copy', { fromUsername, toUsername: sel });
      setEduRows(saved.map(toRow));
      setEduDirty(false);
      setDetail(d => d ? { ...d, edus: saved } : d);
      setCopyOpen(false);
    } catch (e) {
      alert(e instanceof Error ? e.message : '가져오지 못했습니다.');
    }
  }

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
  const visible = includeHidden ? members : members.filter(m => !m.isHidden);
  const active = visible.filter(m => !m.isResigned);
  const resigned = visible.filter(m => m.isResigned);
  let list = tab === 'active' ? active : resigned;

  // 한 사람이 아이디와 사번으로 따로 등록돼 목록에 두 번 나오는 경우를 찾아낸다.
  // 두 줄이 똑같아 보이면 어느 쪽을 지워야 할지 알 수 없으므로 화면에서 구분해 준다.
  const dupUserIds = new Set(
    members
      .filter(m => m.linkedUserId !== null)
      .map(m => m.linkedUserId as number)
      .filter((id, _i, arr) => arr.filter(x => x === id).length > 1),
  );
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
                      {m.linkedUserId !== null && dupUserIds.has(m.linkedUserId) && (
                        <span className="wa-tag warn" title="같은 사람이 두 번 등록되어 있습니다">중복</span>
                      )}
                    </div>
                    <div className="um-meta">
                      {[m.department, m.teamName, m.jobTitle].filter(Boolean).join(' · ') || '-'}
                      {/* 중복된 줄끼리는 등록 키와 내용 유무로만 구분된다 */}
                      {m.linkedUserId !== null && dupUserIds.has(m.linkedUserId) && (
                        <span className="wa-dup">
                          {' '}· 키 {m.username} ·{' '}
                          {m.accountCount + m.eduCount === 0 ? '내용 없음' : `계정 ${m.accountCount} · 교육 ${m.eduCount}`}
                        </span>
                      )}
                    </div>
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
                  <div className="wa-sec-h">기본 정보</div>
                  {!detail.member.hasAccount ? (
                    <div className="wa-nolink">
                      이 인원과 연결된 사용자 계정을 찾지 못했습니다(분장표 키: {detail.member.username}).
                      사용자 계정 관리에서 계정을 만들거나 사번을 맞춰 주세요.
                    </div>
                  ) : (
                    <div className="wa-basic">
                      <Info l="이름" v={detail.member.realName} />
                      <Info l="사번" v={detail.member.employeeNumber} />
                      <Info l="소속팀" v={[detail.member.department, detail.member.teamName].filter(Boolean).join(' · ')} />
                      <Info l="직위" v={detail.member.jobTitle} />
                      <Info l="입사일" v={detail.member.hireDate} />
                      <Info l="경력" v={detail.member.tenure} />
                      <Info l="이메일" v={detail.member.email} />
                      <Info l="전화번호" v={detail.member.phoneNumber} />
                    </div>
                  )}
                  {/* 퇴사 처리는 사용자 계정 관리가 정본 — 여기서 또 찍게 하면 두 값이 어긋난다 */}
                  <div className="wa-basic-note">
                    {detail.member.isResigned
                      ? `퇴사 처리된 계정입니다${detail.member.resignDate ? ` (${detail.member.resignDate})` : ''}.`
                      : '재직 중입니다.'}
                    {' '}소속·연락처·퇴사 처리는 <b>사용자 계정 관리</b>에서 변경합니다.
                  </div>
                </div>

                <div className="wa-sec">
                  <div className="wa-sec-h">
                    기본 교육 기록
                    {eduDirty && <span className="wa-dirty">저장하지 않은 변경</span>}
                    <div style={{ flex: 1 }} />
                    {canEdit && (
                      <>
                        <button className="btn btn-ghost wa-add" onClick={() => setCopyOpen(true)}>복사 가져오기</button>
                        <button className="btn btn-ghost wa-add" onClick={addEduRow}>+ 행 추가</button>
                        <button className="btn btn-primary wa-add" onClick={saveEdus} disabled={!eduDirty || eduSaving}>
                          {eduSaving ? '저장 중…' : '저장'}
                        </button>
                      </>
                    )}
                  </div>
                  <table className="pm-table wa-edu-table">
                    <thead><tr><th>교육 내용</th><th>시작일</th><th>종료일</th><th>강사</th><th>비고</th><th></th></tr></thead>
                    <tbody>
                      {eduRows.length === 0 && <tr><td colSpan={6} className="pm-empty">교육 기록 없음</td></tr>}
                      {eduRows.map((r, i) => (
                        <tr key={r.key}>
                          <td><input className="wa-cell" value={r.eduName} readOnly={!canEdit} placeholder="교육 내용"
                                     onChange={e => setEduRow(i, { eduName: e.target.value })} /></td>
                          <td><DateCell value={r.startDate} readOnly={!canEdit} onChange={v => setEduRow(i, { startDate: v })} /></td>
                          <td><DateCell value={r.endDate} readOnly={!canEdit} onChange={v => setEduRow(i, { endDate: v })} /></td>
                          <td><input className="wa-cell" value={r.instructor} readOnly={!canEdit}
                                     onChange={e => setEduRow(i, { instructor: e.target.value })} /></td>
                          <td><input className="wa-cell" value={r.note} readOnly={!canEdit}
                                     onChange={e => setEduRow(i, { note: e.target.value })} /></td>
                          <td className="wa-row-btns">
                            {canEdit && <button className="wa-mini del" title="이 줄 지우기" onClick={() => removeEduRow(i)}>✕</button>}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {canEdit && (
                    <div className="wa-basic-note">
                      하루짜리 교육은 종료일을 비워 두세요. <b>저장</b>을 눌러야 반영되고,
                      ✕ 로 지운 줄도 저장할 때 함께 삭제됩니다.
                    </div>
                  )}
                </div>

                <div className="wa-sec">
                  <div className="wa-sec-h">
                    외부 교육 기록
                    <span className="wa-sec-note">교육 현황 대시보드에서 자동으로 가져옵니다</span>
                  </div>
                  {detail.externalEduNameAmbiguous && (
                    <div className="wa-nolink" style={{ marginBottom: 8 }}>
                      같은 이름을 쓰는 계정이 둘 이상입니다. 교육 현황 대시보드는 사람을 이름으로 기록하므로
                      아래 목록에 다른 분의 교육이 섞여 있을 수 있습니다.
                    </div>
                  )}
                  <table className="pm-table">
                    <thead><tr><th>교육명</th><th>기간</th><th>상태</th><th>진행률</th><th>교육 방법</th></tr></thead>
                    <tbody>
                      {detail.externalEdus.length === 0 && (
                        <tr><td colSpan={5} className="pm-empty">외부 교육 기록 없음</td></tr>
                      )}
                      {detail.externalEdus.map(e => (
                        <tr key={e.id}>
                          <td>{e.courseName}</td>
                          <td>{fmtPeriod(e.startDate, e.endDate)}</td>
                          <td><span className={`wa-st ${statusClass(e.status)}`}>{e.status}</span></td>
                          <td>{e.progress > 0 ? `${e.progress}%` : '-'}</td>
                          <td>{e.eduMethod || '-'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
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
              </>
            )}
          </div>
        </div>
      </div>

      {acc && sel && <AccountModal username={sel} account={acc === 'new' ? null : acc} onClose={() => setAcc(null)} onSaved={() => { setAcc(null); loadDetail(sel); }} />}
      {copyOpen && sel && (
        <CopyEduModal members={members.filter(m => m.username !== sel)} onClose={() => setCopyOpen(false)} onPick={copyEdusFrom} />
      )}
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

/**
 * 복사 가져오기 — 다른 사람의 교육 목록을 그대로 받아온다.
 * 교육명만 가져오고 이수 내역(날짜·강사·비고)은 가져오지 않는다.
 * 남의 이수일을 옮기면 받지 않은 교육을 받은 것처럼 기록되기 때문이다.
 */
function CopyEduModal({ members, onClose, onPick }: { members: WorkMember[]; onClose: () => void; onPick: (username: string) => void }) {
  const [q, setQ] = useState('');
  const list = q.trim()
    ? members.filter(m => m.realName.toLowerCase().includes(q.trim().toLowerCase()))
    : members;
  return (
    <div className="pm-modal-bg" onClick={onClose}>
      <div className="pm-modal" onClick={e => e.stopPropagation()}>
        <div className="pm-modal-head"><h3>교육 목록 복사 가져오기</h3><button className="pm-x" onClick={onClose}>✕</button></div>
        <div className="pm-modal-body">
          <p className="wa-basic-note" style={{ marginTop: 0 }}>
            고른 사람의 <b>교육 내용만</b> 가져옵니다. 이수일·강사·비고는 가져오지 않습니다.
            이미 있는 교육은 건너뛰므로 여러 번 눌러도 줄이 늘지 않습니다.
          </p>
          <input className="input" placeholder="이름으로 찾기…" value={q} autoFocus onChange={e => setQ(e.target.value)} />
          <div className="wa-copy-list">
            {list.length === 0 && <div className="um-no">대상이 없습니다</div>}
            {list.map(m => (
              <button key={m.id} className="wa-copy-item" onClick={() => onPick(m.username)}>
                <span className="um-avatar">{m.realName[0] ?? '?'}</span>
                <span className="um-info">
                  <span className="um-name">{m.realName}</span>
                  <span className="um-meta">{[m.department, m.teamName, m.jobTitle].filter(Boolean).join(' · ') || '-'}</span>
                </span>
                <span className="um-uid">교육 {m.eduCount}건</span>
              </button>
            ))}
          </div>
        </div>
        <div className="pm-modal-foot"><div style={{ flex: 1 }} /><button className="btn btn-ghost" onClick={onClose}>닫기</button></div>
      </div>
    </div>
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

/** 외부 교육 기간 표기. 기본 교육 기록과 같은 규칙으로 읽히게 맞춘다. */
function fmtPeriod(start: string | null, end: string | null): string {
  const s = (start ?? '').slice(0, 10);
  const e = (end ?? '').slice(0, 10);
  if (!s && !e) return '-';
  if (!s) return e;
  if (!e || e === s) return s;
  if (s.slice(0, 8) === e.slice(0, 8)) return `${s}~${e.slice(8)}`;   // 같은 달이면 일만
  if (s.slice(0, 5) === e.slice(0, 5)) return `${s}~${e.slice(5)}`;   // 같은 해면 월-일
  return `${s}~${e}`;
}

/** 상태 뱃지 색 — 대기/신청완료/진행/완료/취소 */
function statusClass(status: string): string {
  if (status.includes('완료') && !status.includes('신청')) return 'done';
  if (status.includes('진행')) return 'ing';
  if (status.includes('취소')) return 'cancel';
  return '';
}

/** 기본 정보 한 칸. 값이 없으면 '-' 로 두고 빈칸을 남기지 않는다. */
function Info({ l, v }: { l: string; v: string }) {
  return (
    <div className="wa-info">
      <div className="wa-info-l">{l}</div>
      <div className="wa-info-v">{v?.trim() ? v : '-'}</div>
    </div>
  );
}
