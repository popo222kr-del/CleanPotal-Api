import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import { useBlobUrl } from './mesReport';

// LOT 상세 — 성적서 사진(기존 MES 파일 서버 Inspection_img\{LOT ID}\…). 폴더(성적서 항목)별로 작게 보이고, 누르면 크게.

interface Img { path: string; folder: string; name: string; size: number; modified: string }

/** 파일 이름 → 읽기 쉬운 이름(1ck_Cleaning_Before_Image → 1차 세정 전) */
function caption(name: string) {
  const n = name.replace(/\.[^.]+$/, '');
  const parts: string[] = [];
  const ck = /(\d)ck/i.exec(n);
  if (ck) parts.push(`${ck[1]}차`);
  if (/before/i.test(n)) parts.push('세정 전');
  else if (/after/i.test(n)) parts.push('세정 후');
  if (/uv/i.test(n)) parts.push('UV');
  return parts.length ? parts.join(' ') : n.replace(/_/g, ' ');
}
const folderLabel = (f: string) => (f ? f.replace(/^\d+\.\s*/, '').replace(/\\/g, ' › ') : '사진');

function Thumb({ lotId, img, onOpen }: { lotId: string; img: Img; onOpen: (url: string, title: string) => void }) {
  const url = useBlobUrl(`/api/mes-report/lot/${encodeURIComponent(lotId)}/images/file?path=${encodeURIComponent(img.path)}`);
  const title = caption(img.name);
  return (
    <button type="button" className="ld-img" onClick={() => url && onOpen(url, title)} title={`${img.path} · ${new Date(img.modified).toLocaleString()}`}>
      {url ? <img src={url} alt={title} loading="lazy" /> : <span className="ld-img-wait" />}
      <span className="ld-img-cap">{title}</span>
    </button>
  );
}

export default function LotImages({ lotId }: { lotId: string }) {
  const [list, setList] = useState<Img[] | null>(null);
  const [info, setInfo] = useState<{ folder: string; status: string } | null>(null);
  const [big, setBig] = useState<{ url: string; title: string } | null>(null);
  useEffect(() => {
    setList(null);
    let alive = true;
    api.get<{ folder: string; status: string; images: Img[] }>(`/api/mes-report/lot/${encodeURIComponent(lotId)}/images`)
      .then(x => { if (alive) { setList(x.images); setInfo({ folder: x.folder, status: x.status }); } })
      .catch(e => { if (alive) { setList([]); setInfo({ folder: '', status: e instanceof Error ? e.message : '사진 목록을 불러오지 못했습니다' }); } });
    return () => { alive = false; };
  }, [lotId]);
  useEffect(() => {
    if (!big) return;
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') setBig(null); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [big]);
  if (!list) return null;
  // 사진이 없으면 — 어디를 찾았고 왜 없는지(폴더 없음 · 권한) 한 줄로
  if (list.length === 0) return (
    <div className="mr-card">
      <div className="mr-card-h"><b>성적서 사진</b><span className="mr-dim">{info?.status || '사진 없음'}{info?.folder ? ` · ${info.folder}` : ''}</span></div>
    </div>
  );

  const groups = [...new Set(list.map(i => i.folder))];
  return (
    <div className="mr-card">
      <div className="mr-card-h"><b>성적서 사진</b><span className="mr-dim">{list.length}장 · 누르면 크게</span></div>
      <div className="ld-imgs">
        {groups.map(g => (
          <div key={g} className="ld-img-grp">
            <div className="ld-img-grp-h">{folderLabel(g)}</div>
            <div className="ld-img-row">
              {list.filter(i => i.folder === g).map(i => <Thumb key={i.path} lotId={lotId} img={i} onOpen={(url, title) => setBig({ url, title })} />)}
            </div>
          </div>
        ))}
      </div>
      {big && (
        <div className="ld-img-big" onClick={() => setBig(null)} role="dialog" aria-label={big.title}>
          <img src={big.url} alt={big.title} />
          <span>{big.title} · 아무 곳이나 누르면 닫힘</span>
        </div>
      )}
    </div>
  );
}
