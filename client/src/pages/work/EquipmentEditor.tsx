import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { WorkEquipment } from '../../api/types';

// 업무 기록 설비 목록 — 약액 교체·업무보고·BAKE 그을음이 같이 쓴다.
// 순서가 곧 표·보고서 순서. 코드를 바꾸면 지난 기록도 새 코드로 따라간다. 목록에서 빼면 지우지 않고 '사용 안 함'.

const blank = (): WorkEquipment => ({ id: 0, code: '', line: 'METAL', kind: '세정', process: '', sortOrder: 0, isActive: true });

export default function EquipmentEditor({ onClose, onSaved }: { onClose: () => void; onSaved: () => Promise<void> }) {
  const [rows, setRows] = useState<WorkEquipment[] | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => { api.get<WorkEquipment[]>('/api/worklog/equipment').then(setRows).catch(() => setRows([])); }, []);

  const set = (i: number, patch: Partial<WorkEquipment>) => setRows(rs => rs!.map((r, n) => (n === i ? { ...r, ...patch } : r)));
  function move(i: number, d: number) {
    setRows(rs => {
      const next = [...rs!];
      const j = i + d;
      if (j < 0 || j >= next.length) return rs;
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });
  }
  async function save() {
    if (!rows) return;
    setSaving(true);
    try {
      await api.put('/api/worklog/equipment', { items: rows.filter(r => r.code.trim()) });
      await onSaved();
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
    } finally { setSaving(false); }
  }

  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-eq">
        <h3>설비 목록</h3>
        <p className="wf-hint">약액 교체 표·업무보고·BAKE 그을음이 같이 씁니다. 코드를 바꾸면 지난 기록도 따라갑니다. 안 쓰는 설비는 '사용'을 끄세요(기록은 남습니다).</p>
        {!rows ? <div className="wf-empty">불러오는 중…</div> : (
          <div className="wf-eq-wrap">
            <table className="wf-eq-table wf-eqm">
              <thead><tr><th>코드</th><th>라인</th><th>종류</th><th>공정</th><th>사용</th><th /></tr></thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={r.id || `n${i}`} className={r.isActive ? '' : 'off'}>
                    <td className="c-code"><input className="input" value={r.code} placeholder="코드" onChange={e => set(i, { code: e.target.value.toUpperCase() })} /></td>
                    <td className="c-line"><select className="input" value={r.line} onChange={e => set(i, { line: e.target.value })}>
                      <option>METAL</option><option>N-METAL</option></select></td>
                    <td className="c-kind"><select className="input" value={r.kind} onChange={e => set(i, { kind: e.target.value })}>
                      <option>세정</option><option>BAKE</option></select></td>
                    <td className="c-proc"><input className="input" value={r.process} placeholder="공정" onChange={e => set(i, { process: e.target.value })} /></td>
                    <td className="c-use"><label className="wf-eq-use"><input type="checkbox" checked={r.isActive} onChange={e => set(i, { isActive: e.target.checked })} /><span>사용</span></label></td>
                    <td className="wf-eq-move c-move">
                      <button type="button" disabled={i === 0} onClick={() => move(i, -1)}>▲</button>
                      <button type="button" disabled={i === rows.length - 1} onClick={() => move(i, 1)}>▼</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={() => setRows(rs => [...(rs ?? []), blank()])}>+ 설비 추가</button>
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving || !rows} onClick={save}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}
