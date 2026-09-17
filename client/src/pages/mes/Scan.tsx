import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import ScanPanel from './ScanPanel';
import { useCurrentMesWindow } from './shell/windowTypes';
import { useOpenLotHistory } from './shell/useOpenLot';
import './Mes.css';

// MES "LOT 스캔". LOT 라벨을 찍으면 그 LOT 이 있는 공정(OPER) 화면으로 넘어간다.
// 공정 처리(TRAN 실행)는 여기서 하지 않는다 — OPER 화면의 기존 게이트를 그대로 거친다.

type ScanResult = {
  found: boolean; scannedText: string | null; lotId: number;
  lotNumber: string | null; serialNumber: string | null;
  operCode: number; operName: string | null;
  hasOperScreen: boolean; message: string | null;
};

export default function MesScan() {
  const nav = useNavigate();
  const openLot = useOpenLotHistory();
  // 창으로 열려 있으면, 그 공정으로 넘어간 뒤 이 창은 닫는다 — 찍으러 연 창이라 할 일이 끝났다.
  const self = useCurrentMesWindow();
  const [msg, setMsg] = useState<string | null>(null);
  const [stuck, setStuck] = useState<ScanResult | null>(null);   // 찾았지만 OPER 화면이 없는 LOT

  async function resolve(text: string) {
    setMsg(null); setStuck(null);
    try {
      const r = await api.get<ScanResult>(`/api/mes/lot/scan?code=${encodeURIComponent(text)}`);
      if (r.found && r.hasOperScreen) {
        nav(`/mes/oper/${r.operCode}?lot=${encodeURIComponent(r.lotNumber ?? '')}`);
        self?.close();
        return;
      }
      if (r.found) setStuck(r);
      setMsg(r.message);
    } catch {
      setMsg('LOT 을 찾는 중 문제가 발생했습니다.');
    }
  }

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>LOT 스캔</h2></div></header>

      <div className="pg-body">
        <div className="mes-scan">
          <section className="mes-info">
            <ScanPanel onScanned={resolve} errorMessage={msg} autoFocus />
            {stuck?.lotNumber && (
              <button className="mes-sm" onClick={() => openLot(stuck.lotNumber!)}>
                LOT 현황 조회로 보기
              </button>
            )}
          </section>

          <section className="mes-info">
            <div className="mes-title">사용 방법</div>
            <ol className="mes-howto">
              <li>휴대폰: [카메라로 바코드 · QR 찍기] 를 누르고 LOT 라벨이 화면 가운데에 크게 나오게 찍습니다.</li>
              <li>PC: USB·블루투스 스캐너로 입력칸에 찍거나, LOT 번호를 입력하고 Enter 를 누릅니다.</li>
              <li>LOT 이 있는 공정(OPER) 화면으로 이동해 그 LOT 이 선택됩니다. TRAN 을 골라 [실행] 하세요.</li>
            </ol>
            <p className="mes-dim">
              인식하는 값: LOT 번호 · S/N · 반출번호. LOT QR 은 LOT 현황 조회 화면에서 확인·인쇄할 수 있습니다.
            </p>
          </section>
        </div>
      </div>
    </div>
  );
}
