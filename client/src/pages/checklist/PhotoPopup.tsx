import { useEffect } from 'react';
import AttImage from '../../components/AttImage';

// 체크시트 사진 크게 보기 — 구역 화면·NG 관리 공용. 다른 화면 CSS(.modal-bg)를 빌리지 않는다
// (빌려 쓸 때는 그 CSS 가 안 받아진 PC 에서 사진이 페이지 아래에 원본 크기로 붙어 나왔다).
export default function PhotoPopup({ value, onClose }: { value: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="ck-pv" onClick={onClose} role="dialog" aria-label="사진 크게 보기">
      <AttImage value={value} className="ck-preview" />
      <button type="button" className="ck-pv-x" onClick={onClose} aria-label="닫기">✕</button>
    </div>
  );
}
