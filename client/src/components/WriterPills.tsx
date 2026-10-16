import './WriterPills.css';

/** 칸을 실제로 쓴 사람 이름표(알약) — 생산 미팅 주간·야간·Office 메모 머리에 붙는다. 이름이 없으면 아무것도 안 그린다. */
export default function WriterPills({ names }: { names?: string[] | null }) {
  if (!names?.length) return null;
  return (
    <span className="wr-pills" title={`작성: ${names.join(', ')}`}>
      {names.map(n => <span key={n} className="wr-pill">{n}</span>)}
    </span>
  );
}
