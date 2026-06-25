namespace CleanPotal.Core.DTOs;

/// <summary>모든 API 응답 표준 봉투: { success, data, error }.</summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResponse Ok(object? data) => new() { Success = true, Data = data, Error = null };
    public static ApiResponse Fail(string error) => new() { Success = false, Data = null, Error = error };
}
