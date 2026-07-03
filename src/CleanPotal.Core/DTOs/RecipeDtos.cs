namespace CleanPotal.Core.DTOs;

public record RecipeDto(
    int Id, string Text, string DisplayText,
    double S2Minutes, double S2Temperature, double HfMinutes, double DiMinutes, double TotalMinutes,
    bool IsFavorite, int OrderIndex);

public record RecipeUpsertRequest(
    string Text, string DisplayText,
    double S2Minutes, double S2Temperature, double HfMinutes, double DiMinutes, double TotalMinutes,
    bool IsFavorite, int OrderIndex);
