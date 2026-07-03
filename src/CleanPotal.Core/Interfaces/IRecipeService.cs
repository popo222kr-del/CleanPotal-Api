using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>세정 레시피 관리.</summary>
public interface IRecipeService
{
    Task<IReadOnlyList<RecipeDto>> GetAllAsync(string? search);
    Task<RecipeDto> CreateAsync(RecipeUpsertRequest req);
    Task<RecipeDto?> UpdateAsync(int id, RecipeUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
