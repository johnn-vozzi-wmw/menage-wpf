namespace Menage.Core.Operacoes;

public enum Situacao { Ok, Aviso, Erro }

/// <summary>O que cada operação devolve para um projeto: situação, mensagem curta e dados próprios da operação.</summary>
public sealed record ResultadoDoProjeto<T>(Situacao Situacao, string Mensagem, T? Dados = default, Erro? Erro = null)
{
    public static ResultadoDoProjeto<T> Ok(T dados, string mensagem = "") => new(Situacao.Ok, mensagem, dados);

    public static ResultadoDoProjeto<T> Aviso(string mensagem, T? dados = default) => new(Situacao.Aviso, mensagem, dados);

    public static ResultadoDoProjeto<T> Falha(Erro erro) => new(Situacao.Erro, erro.Causa, default, erro);
}

public sealed record ItemDoLote<T>(Projeto Projeto, ResultadoDoProjeto<T> Resultado);

public sealed record LinhaDoResumo(string Projeto, Situacao Situacao, string Mensagem);

/// <summary>Visão sem tipo do resumo, para diálogos e decoradores.</summary>
public interface IResumoDoLote
{
    string Operacao { get; }

    /// <summary>Verdadeiro se o usuário mandou parar e sobraram projetos sem executar.</summary>
    bool Interrompido { get; }

    IReadOnlyList<LinhaDoResumo> Linhas { get; }

    int Oks { get; }

    int Avisos { get; }

    int Erros { get; }
}

public sealed record ResumoDoLote<T>(string Operacao, IReadOnlyList<ItemDoLote<T>> Itens, bool Interrompido) : IResumoDoLote
{
    public IReadOnlyList<LinhaDoResumo> Linhas =>
        [.. Itens.Select(i => new LinhaDoResumo(i.Projeto.Nome, i.Resultado.Situacao, i.Resultado.Mensagem))];

    public int Oks => Itens.Count(i => i.Resultado.Situacao == Situacao.Ok);

    public int Avisos => Itens.Count(i => i.Resultado.Situacao == Situacao.Aviso);

    public int Erros => Itens.Count(i => i.Resultado.Situacao == Situacao.Erro);
}

/// <summary>
/// Dois sinais distintos: <c>Abortar</c> mata o que está rodando; <c>PararApos</c> deixa o projeto atual
/// terminar e não começa o próximo ("Parar após o atual").
/// </summary>
public readonly record struct SinaisDoLote(CancellationToken Abortar, CancellationToken PararApos)
{
    public static SinaisDoLote Nenhum => default;
}
