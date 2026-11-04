using Menage.Core.Operacoes;

namespace Menage.Core.Saida;

/// <summary>Como o resultado completo de um lote aparece na Saída.</summary>
public static class FormatadorDeResumo
{
    public static IReadOnlyList<LinhaDeSaida> Linhas(IResumoDoLote resumo, DateTime hora)
    {
        List<LinhaDeSaida> linhas = [new(hora, $"== {resumo.Operacao}: {Totais(resumo)} ==", TipoDeLinha.Titulo)];
        linhas.AddRange(resumo.Linhas.Select(l => new LinhaDeSaida(hora, $"  {Marca(l.Situacao)} {l.Projeto}: {l.Mensagem}", Tipo(l.Situacao))));
        if (resumo.NaoExecutados.Count > 0)
            linhas.Add(new(hora, $"  Não executados: {string.Join(", ", resumo.NaoExecutados)}", TipoDeLinha.Aviso));
        linhas.AddRange(resumo.Observacoes.Select(o => new LinhaDeSaida(hora, "  " + o.Texto, o.Situacao is { } s ? Tipo(s) : TipoDeLinha.Fraco)));
        return linhas;
    }

    public static string Totais(IResumoDoLote resumo) =>
        $"{resumo.Oks} ok, {resumo.Avisos} com aviso, {resumo.Erros} com erro" + (resumo.Interrompido ? " (interrompido)" : "");

    public static TipoDeLinha Tipo(Situacao situacao) => situacao switch
    {
        Situacao.Erro => TipoDeLinha.Erro,
        Situacao.Aviso => TipoDeLinha.Aviso,
        _ => TipoDeLinha.Ok,
    };

    static string Marca(Situacao situacao) => situacao switch { Situacao.Erro => "×", Situacao.Aviso => "!", _ => "✓" };
}
