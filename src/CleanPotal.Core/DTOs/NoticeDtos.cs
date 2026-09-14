namespace CleanPotal.Core.DTOs;

/// <summary>공지 한 건. <c>RowVersion</c> 은 저장할 때 그대로 돌려보내면
/// 그 사이 남이 먼저 고쳤는지 서버가 판단한다. <c>CanModify</c> 는
/// 이 사용자가 수정·삭제할 수 있는지(작성자 본인 또는 관리자) — 화면에서 버튼을 가리는 용도다.</summary>
public record NoticeDto(int Id, string Title, string Content, string Author, DateTime CreatedAt,
                        int RowVersion, bool CanModify);

/// <summary>공지 등록·수정 요청. 수정 시 <c>RowVersion</c> 에 불러올 때 받은 값을 넣는다
/// (비워 보내면 동시 수정 검사를 건너뛴다 — 구버전 클라이언트 호환).</summary>
public record NoticeUpsertRequest(string Title, string Content, int? RowVersion = null);
