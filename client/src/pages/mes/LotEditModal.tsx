import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import './Mes.css';

// MES LOT 정보 수정 — OPER 목록의 [수정] 로 연다.
//
// 반출번호 · LINE 은 이 LOT 을 만든 전산등록이 있어야 고칠 수 있다(없으면 잠기고 왜인지 알려 준다).
// S/N · 코멘트는 언제나 고칠 수 있다. PROCESS 는 화면에 없지만 저장할 때 기존 값을 그대로 둔다.

type Props = {
  lotId: number;
  lotNumber: string;
  matId: string | null;
  matDesc: string | null;
  pmEquipmentName: string | null;
  serialNumber: string;
  comment: string | null;
  onClose: () => void;
  onSaved: () => void;
};

type EditInfo = { hasRegistration: boolean; exportNumber: string | null; line: string | null };

export default function LotEditModal(p: Props) {
  const canEdit = useAccess().canEditMes;
  const [info, setInfo] = useState<EditInfo | null>(null);
  const [exportNumber, setExportNumber] = useState('');
  const [line, setLine] = useState('');
  const [serial, setSerial] = useState(p.serialNumber);
  const [comment, setComment] = useState(p.comment ?? '');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const d = await api.get<EditInfo>(`/api/mes/lot/${p.lotId}/edit`);
        setInfo(d);
        setExportNumber(d.exportNumber ?? '');
        setLine(d.line ?? '');
      } catch {
        setIsError(true); setMsg('LOT 정보를 불러오지 못했습니다.');
        setInfo({ hasRegistration: false, exportNumber: null, line: null });
      }
    })();
  }, [p.lotId]);

  async function save() {
    if (busy) return;
    setBusy(true); setMsg(null);
    try {
      const r = await api.post<{ success: boolean; message: string }>(`/api/mes/lot/${p.lotId}/edit`, {
        exportNumber, line, serialNumber: serial,
        originalSerialNumber: p.serialNumber,   // 실제로 바뀌었을 때만 S/N 을 건드리게
        comment,
      });
      setIsError(!r.success); setMsg(r.message);
      if (r.success) p.onSaved();
    } catch {
      setIsError(true); setMsg('저장 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const regLocked = !info?.hasRegistration || !canEdit;

  return (
    <div className="mes-modal-bg">
      <div className="mes-modal mes-modal-sm">
        <h3>LOT 정보 수정 — {p.lotNumber}</h3>

        <div className="mes-form">
          <div className="mes-pair">
            <label><span>LINE</span>
              <input className="input" value={line} disabled={regLocked}
                     onChange={e => setLine(e.target.value)} /></label>
            <label><span>업체명</span>
              <input className="input" value={p.pmEquipmentName ?? ''} readOnly /></label>
          </div>
          <div className="mes-pair">
            <label><span>반출번호</span>
              <input className="input" value={exportNumber} disabled={regLocked}
                     onChange={e => setExportNumber(e.target.value)} /></label>
            <label><span>MAT ID</span>
              <input className="input" value={p.matId ?? ''} readOnly /></label>
          </div>
          <label><span>MAT DESC</span>
            <input className="input" value={p.matDesc ?? ''} readOnly /></label>
          <label><span>S/N</span>
            <input className="input" value={serial} disabled={!canEdit}
                   onChange={e => setSerial(e.target.value)} /></label>

          {info && !info.hasRegistration && (
            <p className="mes-alert warn">
              이 LOT 을 만든 전산등록 정보가 없어 반출번호 · LINE 은 수정할 수 없습니다.
            </p>
          )}

          <label><span>코멘트 (누적 · 편집 가능)</span>
            <textarea className="mes-comment" rows={4} value={comment} disabled={!canEdit}
                      onChange={e => setComment(e.target.value)} /></label>
        </div>

        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}
        {!canEdit && <p className="mes-dim">LOT 정보 수정은 MES 편집 권한이 필요합니다.</p>}

        <div className="mes-modal-foot">
          <button className="btn btn-ghost" onClick={p.onClose} disabled={busy}>닫기</button>
          <button className="btn btn-primary" onClick={() => void save()} disabled={busy || !canEdit}>저장</button>
        </div>
      </div>
    </div>
  );
}
