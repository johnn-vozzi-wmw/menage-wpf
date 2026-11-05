using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Menage.App.Inicializacao;

/// <summary>Marcos da abertura do app (ms desde o início do processo) e memória; usado por <c>--medir-inicio</c>.</summary>
public sealed class MedidorDeInicio
{
    static readonly JsonSerializerOptions _opcoes = new() { WriteIndented = true };
    readonly Dictionary<string, double> _marcos = [];

    public void Marcar(string nome) => _marcos[nome] = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    public void Gravar(string arquivo)
    {
        var p = Process.GetCurrentProcess();
        var dados = new
        {
            marcosMs = _marcos.ToDictionary(m => m.Key, m => Math.Round(m.Value)),
            memoriaPrivadaMb = Math.Round(p.PrivateMemorySize64 / 1048576.0, 1),
            workingSetMb = Math.Round(p.WorkingSet64 / 1048576.0, 1),
            assembliesCarregados = AppDomain.CurrentDomain.GetAssemblies().Length,
        };
        File.WriteAllText(arquivo, JsonSerializer.Serialize(dados, _opcoes));
    }
}
