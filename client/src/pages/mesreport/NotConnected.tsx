/** 기존 MES DB 연결이 안 될 때 안내 */
export default function NotConnected({ title }: { title: string }) {
  return (
    <div className="mr-page">
      <header className="pg-header"><div><h2>{title}</h2><p>기존 MES DB 조회</p></div></header>
      <div className="pg-body">
        <div className="mr-notice">
          <b>기존 MES DB 연결이 설정되지 않았습니다.</b>
          <span>운영 서버 appsettings.local.json 의 ConnectionStrings 에 LegacyMes(읽기 전용 계정)를 넣고 사이트를 다시 시작하세요.</span>
        </div>
      </div>
    </div>
  );
}
