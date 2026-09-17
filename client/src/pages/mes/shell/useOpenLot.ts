import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMesWindows } from './windowTypes';
import { MES_WINDOW_TITLES } from './windowRegistry';

/**
 * 목록에서 LOT 번호를 눌렀을 때 "그 LOT 의 현황" 을 여는 길.
 *
 * MES 셸 안(대시보드·OPER·다른 창)에서는 창으로 띄운다 — 보고 있던 화면을 넘기면 고르던 LOT 과
 * 입력하던 값이 사라진다. 셸 밖(예전 주소로 직접 들어온 경우)에서는 예전처럼 그 화면으로 이동한다.
 */
export function useOpenLotHistory() {
  const windows = useMesWindows();
  const nav = useNavigate();

  return useCallback((lotNumber: string) => {
    if (windows) {
      windows.open('history', MES_WINDOW_TITLES.history, { lot: lotNumber });
      return;
    }
    nav(`/mes/history?lot=${encodeURIComponent(lotNumber)}`);
  }, [windows, nav]);
}
