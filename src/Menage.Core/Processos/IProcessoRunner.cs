using System.Text;

namespace Menage.Core.Processos;

/// <summary>O que executar. <c>Ambiente</c> com valor nulo remove a variável do processo filho.</summary>
public sealed record ComandoProcesso(
    string Arquivo,
    IReadOnlyList<string> Argumentos,
    string? PastaDeTrabalho = null,
    IReadOnlyDictionary<string, string?>? Ambiente = null,
    TimeSpan? Timeout = null,
    byte[]? Entrada = null);

/// <summary>Saída em bytes (binários, codificações diferentes de UTF-8) com atalhos em texto.</summary>
public sealed record SaidaProcesso(
    int CodigoDeSaida,
    byte[] Saida,
    byte[] Erro,
    TimeSpan Duracao,
    bool Expirou = false,
    bool Cancelado = false,
    string? FalhaAoIniciar = null)
{
    public bool Sucesso => CodigoDeSaida == 0 && !Expirou && !Cancelado && FalhaAoIniciar is null;

    public string Texto => Encoding.UTF8.GetString(Saida);

    public string TextoErro => Encoding.UTF8.GetString(Erro);

    public static SaidaProcesso NaoIniciou(string motivo) => new(-1, [], [], TimeSpan.Zero, FalhaAoIniciar: motivo);
}

/// <summary>Único ponto que cria processos. Não lança por falha do processo: devolve <see cref="SaidaProcesso"/>.</summary>
public interface IProcessoRunner
{
    Task<SaidaProcesso> ExecutarAsync(ComandoProcesso comando, CancellationToken cancelar = default);
}
