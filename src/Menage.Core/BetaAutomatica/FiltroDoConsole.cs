namespace Menage.Core.BetaAutomatica;

public enum NivelDeFiltro { SemSaida, Tudo, Problemas, Comandos }

/// <summary>Filtro do console da Beta automática (nível, origem, atividade e busca no texto). Regra pura, usada pela tela.</summary>
public sealed record FiltroDoConsole(NivelDeFiltro Nivel = NivelDeFiltro.SemSaida, string Origem = "", string Atividade = "", string Busca = "")
{
    public const string OrigemBeta = "Beta automática", OrigemMonitor = "Monitor";

    public bool Passa(RegistroDoConsole r)
    {
        var nivelOk = Nivel switch
        {
            NivelDeFiltro.SemSaida => r.Nivel != NivelDoConsole.Saida,
            NivelDeFiltro.Problemas => r.Nivel is NivelDoConsole.Aviso or NivelDoConsole.Erro,
            NivelDeFiltro.Comandos => r.Nivel == NivelDoConsole.Comando,
            _ => true,
        };
        if (!nivelOk) return false;
        if (Origem.Length > 0 && r.Origem != Origem) return false;
        if (Atividade.Trim() is { Length: > 0 } atv && !r.Atividade.Contains(atv, StringComparison.OrdinalIgnoreCase)) return false;
        return Busca.Trim() is not { Length: > 0 } busca
            || r.Texto.Contains(busca, StringComparison.OrdinalIgnoreCase) || r.Projeto.Contains(busca, StringComparison.OrdinalIgnoreCase);
    }
}
