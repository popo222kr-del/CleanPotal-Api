namespace CleanPotal.Core.Entities;

/// <summary>세정 레시피 (WPF recipes.json). S2/HF/DI 공정 시간·온도.</summary>
public class Recipe
{
    public int Id { get; set; }
    public string Text { get; set; } = "";          // 레시피 텍스트(원본)
    public string DisplayText { get; set; } = "";    // 표시 텍스트
    public double S2Minutes { get; set; }            // S2 시간(분)
    public double S2Temperature { get; set; }        // S2 온도
    public double HfMinutes { get; set; }            // HF 시간(분)
    public double DiMinutes { get; set; }            // DI 시간(분)
    public double TotalMinutes { get; set; }         // 총 시간(분)
    public bool IsFavorite { get; set; }
    public int OrderIndex { get; set; }
}
