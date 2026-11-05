namespace Menage.Core.Estado;

public enum TomDaEtiqueta { Discreto, Amarelo, Vermelho, Azul, Verde }

public enum TipoDeBranch { Principal, Atividade, Detached, Desconhecida }

public sealed record EtiquetaDoEstado(string Texto, TomDaEtiqueta Tom);

/// <summary>Regras de como a grade descreve o estado de um projeto. Texto e tom; a cor de verdade é do tema.</summary>
public static class ApresentacaoDoEstado
{
    static readonly string[] _principais = ["develop", "main", "master"];

    public static TipoDeBranch TipoDaBranch(EstadoDoProjeto estado)
    {
        if (estado.Falha.Length > 0) return TipoDeBranch.Desconhecida;
        if (estado.Detached) return TipoDeBranch.Detached;
        var nome = estado.Branch;
        return _principais.Contains(nome, StringComparer.OrdinalIgnoreCase) || nome.StartsWith("release", StringComparison.OrdinalIgnoreCase)
            ? TipoDeBranch.Principal
            : TipoDeBranch.Atividade;
    }

    public static string TextoDaBranch(EstadoDoProjeto estado) =>
        estado.Falha.Length > 0 ? "" : estado.Detached ? $"detached ({estado.Head})" : estado.Branch;

    /// <summary>Vermelho com "!" quando há versionados alterados ou operação em andamento; não versionados não contam.</summary>
    public static bool TemAlteracoes(EstadoDoProjeto estado) => estado.Alteracoes > 0 || estado.Operacao.Length > 0;

    public static int Arquivos(EstadoDoProjeto estado) => estado.Alteracoes + estado.NaoVersionados;

    public static IReadOnlyList<EtiquetaDoEstado> Situacao(EstadoDoProjeto estado)
    {
        if (estado.Falha.Length > 0) return [new(estado.Falha, TomDaEtiqueta.Vermelho)];

        List<EtiquetaDoEstado> etiquetas = [];
        if (estado.Operacao.Length > 0) etiquetas.Add(new(estado.Operacao, TomDaEtiqueta.Vermelho));
        if (estado.Alteracoes > 0) etiquetas.Add(new($"{estado.Alteracoes} alterados", TomDaEtiqueta.Amarelo));
        if (estado.NaoVersionados > 0) etiquetas.Add(new($"{estado.NaoVersionados} não versionados", TomDaEtiqueta.Discreto));
        if (etiquetas.Count == 0) etiquetas.Add(new("limpo", TomDaEtiqueta.Discreto));
        return etiquetas;
    }

    public static IReadOnlyList<EtiquetaDoEstado> Origin(EstadoDoProjeto estado)
    {
        if (estado.Falha.Length > 0 || estado.Detached) return [];
        if (!estado.TemUpstream) return [new("sem origin", TomDaEtiqueta.Discreto)];

        List<EtiquetaDoEstado> etiquetas = [];
        if (estado.Ahead > 0) etiquetas.Add(new($"↑{estado.Ahead}", TomDaEtiqueta.Azul));
        if (estado.Behind > 0) etiquetas.Add(new($"↓{estado.Behind}", TomDaEtiqueta.Amarelo));
        if (etiquetas.Count == 0) etiquetas.Add(new("em dia", TomDaEtiqueta.Discreto));
        return etiquetas;
    }
}
