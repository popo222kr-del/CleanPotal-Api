import { useAttUrl } from '../hooks/useAttUrl';

/** 첨부 사진. 보관소에 있는 것은 받아서 띄우므로 잠깐 빈 칸이 보인다. */
export default function AttImage({ value, className, onClick, alt = '' }: {
  value: string;
  className?: string;
  onClick?: (e: React.MouseEvent<HTMLElement>) => void;
  alt?: string;
}) {
  const url = useAttUrl(value);
  if (!url) return <span className={`att-img-wait${className ? ` ${className}` : ''}`} aria-label="사진 불러오는 중" />;
  return <img src={url} className={className} alt={alt} onClick={onClick} />;
}
