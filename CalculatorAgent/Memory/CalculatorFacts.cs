namespace CalculatorAgent.Memory;

public class CalculatorFacts
{
    public Dictionary<string, decimal> SavedValues { get; set; } = new();
    public string? LastResult { get; set; }
    public string? LastExpression { get; set; }
}
