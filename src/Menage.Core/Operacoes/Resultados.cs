namespace Menage.Core.Operacoes;

public enum Situacao { Ok, Aviso, Erro }

/// <summary>O que cada operação devolve para um projeto: situação, mensagem curta e dados próprios da operação.</summary>
public sealed record ResultadoDoProjeto<T>(Situacao Situacao, string Mensagem, T? Dados = default, Erro? Erro = null)
{
    /// <summary>O usuário decidiu não processar os projetos restantes (abortar num conflito, por exemplo).</summary>
    public bool InterromperLote { get; init; }

    public static ResultadoDoProjeto<T> Ok(T dados, string mensagem = "") => new(Situacao.Ok, mensagem, dados);

    public static ResultadoDoProjeto<T> Aviso(string mensagem, T? dados = default) => new(Situacao.Aviso, mensagem, dados);

    public static ResultadoDoProjeto<T> Falha(Erro erro, T? dados = default) => new(Situacao.Erro, erro.Causa, dados, erro);

    public static ResultadoDoProjeto<T> Falha(string causa, string? explicacao = null, string? acao = null) =>
        Falha(new Erro(causa, explicacao, acao));
}

public sealed record ItemDoLote<T>(Projeto Projeto, ResultadoDoProjeto<T> Resultado);

public sealed record LinhaDoResumo(string Projeto, Situacao Situacao, string Mensagem);

/// <summary>Frase do resumo que não pertence a um projeto; sem <c>Situacao</c> é só informação.</summary>
public sealed record ObservacaoDoLote(string Texto, Situacao? Situacao = null);

/// <summary>O que uma operação acrescenta ao resumo depois do último projeto.</summary>
public sealed record ConclusaoDoLote<T>
{
    public IReadOnlyList<ItemDoLote<T>> Itens { get; init; } = [];

    public IReadOnlyList<ObservacaoDoLote> Observacoes { get; init; } = [];
}

/// <summary>Visão sem tipo do resumo, para diálogos e decoradores.</summary>
public interface IResumoDoLote
{
    string Operacao { get; }

    /// <summary>Verdadeiro se o usuário mandou parar e sobraram projetos sem executar.</summary>
    bool Interrompido { get; }

    IReadOnlyList<LinhaDoResumo> Linhas { get; }

    /// <summary>Projetos que ficaram sem executar porque o lote parou.</summary>
    IReadOnlyList<string> NaoExecutados { get; }

    IReadOnlyList<ObservacaoDoLote> Observacoes { get; }

    int Oks { get; }

    int Avisos { get; }

    int Erros { get; }
}

public sealed record ResumoDoLote<T>(string Operacao, IReadOnlyList<ItemDoLote<T>> Itens, bool Interrompido) : IResumoDoLote
{
    public IReadOnlyList<string> NaoExecutados { get; init; } = [];

    public IReadOnlyList<ObservacaoDoLote> Observacoes { get; init; } = [];

    public IReadOnlyList<LinhaDoResumo> Linhas =>
        [.. Itens.Select(i => new LinhaDoResumo(i.Projeto.Nome, i.Resultado.Situacao, i.Resultado.Mensagem))];

    public int Oks => Itens.Count(i => i.Resultado.Situacao == Situacao.Ok);

    public int Avisos => Contar(Situacao.Aviso);

    public int Erros => Contar(Situacao.Erro);

    int Contar(Situacao situacao) =>
        Itens.Count(i => i.Resultado.Situacao == situacao) + Observacoes.Count(o => o.Situacao == situacao);
}

/// <summary>
/// Dois sinais distintos: <c>Abortar</c> mata o que está rodando; <c>PararApos</c> deixa o projeto atual
/// terminar e não começa o próximo ("Parar após o atual").
/// </summary>
public readonly record struct SinaisDoLote(CancellationToken Abortar, CancellationToken PararApos)
{
    public static SinaisDoLote Nenhum => default;
}
