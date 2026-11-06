using System.Text.RegularExpressions;

namespace Menage.Core.BetaAutomatica;

public sealed record PassoDaDecisao(string Texto, bool Ok);

/// <summary>Uma atividade devolvida pelo Jira e o que a regra decidiu: entra na lista ou fica de fora, com os passos ✓/✕ e os detalhes.</summary>
public sealed record DecisaoDaBusca(string Chave, string Titulo, bool Entra, string Decisao, IReadOnlyList<PassoDaDecisao> Passos, IReadOnlyList<string> Detalhes);

public sealed record RelatorioDoDiagnostico(
    IReadOnlyList<string> Cabecalho, IReadOnlyList<DecisaoDaBusca> Decisoes, IReadOnlyList<string> ForaDaBusca, IReadOnlyList<string> Resumo)
{
    public int Entram => Decisoes.Count(d => d.Entra);

    public int FicamDeFora => Decisoes.Count - Entram;
}

/// <summary>
/// Lê as linhas da simulação (<see cref="ServicoDeBetaAutomatica.DiagnosticarAsync"/>) e agrupa por atividade, sem mudar o texto do motor.
/// Ficam de fora: FORADOSTATUS e FORADOESCOPO; todas as outras decisões entram na lista.
/// </summary>
public static partial class InterpretadorDoDiagnostico
{
    [GeneratedRegex(@"^--- (?<chave>[A-Za-z][A-Za-z0-9]*-\d+)\s+""(?<titulo>.*)""\s*$")]
    private static partial Regex Atividade();

    [GeneratedRegex(@"^\s+DECIS[ÃA]O:\s*(?<d>\S+)\s*-?\s*(?<t>.*)$")]
    private static partial Regex Decisao();

    [GeneratedRegex(@"^\s+escopo:\s*(?<r>ENTRA|FORA)\s*-\s*(?<t>.*)$")]
    private static partial Regex Escopo();

    [GeneratedRegex(@"^\s+\[(?<m>ok|tag|--)\]\s*(?<t>.*)$")]
    private static partial Regex Projeto();

    enum Modo { Cabecalho, Atividade, Fora, Resumo }

    public static RelatorioDoDiagnostico Interpretar(IEnumerable<string> linhas)
    {
        var cabecalho = new List<string>();
        var fora = new List<string>();
        var resumo = new List<string>();
        var decisoes = new List<DecisaoDaBusca>();
        Bloco? atual = null;
        var modo = Modo.Cabecalho;

        void Fechar()
        {
            if (atual is not null) decisoes.Add(atual.Montar());
            atual = null;
        }

        foreach (var linha in linhas.Select(l => l.TrimEnd('\r', '\n')))
        {
            if (linha.StartsWith("=== RESUMO", StringComparison.Ordinal)) { Fechar(); modo = Modo.Resumo; }
            else if (Atividade().Match(linha) is { Success: true } a) { Fechar(); atual = new Bloco(a.Groups["chave"].Value, a.Groups["titulo"].Value); modo = Modo.Atividade; continue; }
            else if (linha.StartsWith("--- ", StringComparison.Ordinal)) { Fechar(); modo = Modo.Fora; continue; }

            if (linha.Trim().Length == 0) continue;
            switch (modo)
            {
                case Modo.Cabecalho: cabecalho.Add(linha); break;
                case Modo.Fora: fora.Add(linha.Trim()); break;
                case Modo.Resumo: resumo.Add(linha); break;
                default: atual!.Adicionar(linha); break;
            }
        }

        Fechar();
        return new RelatorioDoDiagnostico(cabecalho, decisoes, fora, resumo);
    }

    sealed class Bloco(string chave, string titulo)
    {
        readonly List<PassoDaDecisao> _passos = [];
        readonly List<string> _detalhes = [];
        string _decisao = "";
        bool _entra;

        public void Adicionar(string linha)
        {
            if (Decisao().Match(linha) is { Success: true } d)
            {
                _decisao = d.Groups["d"].Value.ToUpperInvariant();
                _entra = _decisao is not ("FORADOSTATUS" or "FORADOESCOPO");
                _detalhes.Add(linha.Trim());
            }
            else if (Escopo().Match(linha) is { Success: true } e) _passos.Add(new PassoDaDecisao(e.Groups["t"].Value, e.Groups["r"].Value == "ENTRA"));
            else if (Projeto().Match(linha) is { Success: true } p) _passos.Add(new PassoDaDecisao(p.Groups["t"].Value, p.Groups["m"].Value != "--"));
            else _detalhes.Add(linha.Trim());
        }

        public DecisaoDaBusca Montar() => new(chave, titulo, _entra, _decisao, _passos, _detalhes);
    }
}
