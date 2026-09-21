import { useCallback, useRef, useState } from 'react';

/** 끌고 온 것이 파일인지. 글자를 끌어도 반응하면 성가시다. */
function hasFiles(e: React.DragEvent) {
  return Array.from(e.dataTransfer?.types ?? []).includes('Files');
}

/**
 * 파일을 끌어다 놓을 수 있게 하는 핸들러 묶음.
 *
 * dragenter/dragleave 는 안쪽 자식 위에서도 올라오므로 그냥 세면 안으로 들어갈 때마다
 * 테두리가 깜빡인다. 들어온 만큼 세어(depth) 0 이 될 때만 끈다.
 */
export function useDropZone(onFiles: (files: File[]) => void, disabled = false) {
  const [over, setOver] = useState(false);
  const depth = useRef(0);

  const onDragEnter = useCallback((e: React.DragEvent) => {
    if (disabled || !hasFiles(e)) return;
    e.preventDefault(); e.stopPropagation();
    depth.current += 1;
    setOver(true);
  }, [disabled]);

  const onDragOver = useCallback((e: React.DragEvent) => {
    if (disabled || !hasFiles(e)) return;
    // 막지 않으면 브라우저가 그 파일을 그냥 열어 버린다
    e.preventDefault(); e.stopPropagation();
    e.dataTransfer.dropEffect = 'copy';
  }, [disabled]);

  const onDragLeave = useCallback((e: React.DragEvent) => {
    if (disabled || !hasFiles(e)) return;
    e.preventDefault(); e.stopPropagation();
    depth.current = Math.max(0, depth.current - 1);
    if (depth.current === 0) setOver(false);
  }, [disabled]);

  const onDrop = useCallback((e: React.DragEvent) => {
    if (disabled) return;
    e.preventDefault(); e.stopPropagation();
    depth.current = 0;
    setOver(false);
    const files = Array.from(e.dataTransfer.files ?? []);
    if (files.length) onFiles(files);
  }, [disabled, onFiles]);

  return { over, dropProps: { onDragEnter, onDragOver, onDragLeave, onDrop } };
}
