namespace CleanPotal.Core.DTOs;

public record NoticeDto(int Id, string Title, string Content, string Author, DateTime CreatedAt);

public record NoticeUpsertRequest(string Title, string Content);
