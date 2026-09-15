using CleanPotal.Core.Interfaces;

namespace CleanPotal.Tests;

/// <summary>테스트용 '현재 로그인 사용자'.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public int? Id { get; init; }
    public string RealName { get; init; } = "";
    public bool IsAdmin { get; init; }
    public string Department { get; init; } = "";
    public string TeamName { get; init; } = "";

    public static FakeCurrentUser Person(int id, string name) => new() { Id = id, RealName = name };
    public static FakeCurrentUser In(string name, string department, string team)
        => new() { Id = 1, RealName = name, Department = department, TeamName = team };
    public static FakeCurrentUser Admin(int id = 99, string name = "관리자") => new() { Id = id, RealName = name, IsAdmin = true };
    public static FakeCurrentUser Anonymous() => new();
}
