using System.ComponentModel;
using System.Data;

namespace CalculatorAgent.Tools;

public class CalculatorTool
{
    [Description("Vyhodnotí matematický výraz a vrátí výsledek")]
    public string Calculate([Description("Matematický výraz k vyhodnocení, např. '2+2', '15*0.21', '100/4'")] string expression)
    {
        try
        {
            var result = new DataTable().Compute(expression, null);
            return result?.ToString() ?? "Chyba výpočtu";
        }
        catch (Exception ex)
        {
            return $"Chyba při výpočtu: {ex.Message}";
        }
    }
}
