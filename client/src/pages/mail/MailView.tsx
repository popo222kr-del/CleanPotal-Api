import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../../api/client';
import { MailReader, type Detail } from './mailShared';
import '../MailArchive.css';

// 메일 새 창 — 메일 확인 목록에서 [새 창]·두 번 누르기로 연다. 사이드바 없이 메일 한 통만.
export default function MailView() {
  const { id } = useParams();
  const [d, setD] = useState<Detail | null>(null);
  const [err, setErr] = useState('');

  useEffect(() => {
    api.get<Detail>(`/api/mail/${id}`)
      .then(x => { setD(x); document.title = `${x.subject || '(제목 없음)'} - 메일 확인`; })
      .catch(e => setErr(e instanceof Error ? e.message : '메일을 열지 못했습니다.'));
  }, [id]);

  async function star() {
    if (!d) return;
    try {
      const on = await api.put<boolean>(`/api/mail/${d.id}/star`, { starred: !d.starred });
      setD({ ...d, starred: on });
      window.opener?.postMessage({ type: 'mail-star', id: d.id, starred: on }, location.origin);
    } catch (e) { setErr(e instanceof Error ? e.message : '바꾸지 못했습니다.'); }
  }

  return (
    <div className="ml-window">
      {err && <div className="ml-err">{err}</div>}
      {!d && !err && <div className="ml-empty">여는 중…</div>}
      {d && <div className="ml-reader ml-reader-win"><MailReader d={d} onStar={() => void star()} onError={setErr} /></div>}
    </div>
  );
}
