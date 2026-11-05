namespace Menage.Core.Interacao;

public enum DecisaoDeConflito { Continuar, Pular, Abortar }

/// <summary>O que perguntar ao usuário quando uma operação para num conflito.</summary>
public sealed record ConflitoDoGit(
    string Operacao,
    Projeto Projeto,
    string Descricao,
    string Status,
    string? Aviso,
    string TextoContinuar,
    string TextoPular,
    string TextoAbortar);

/// <summary>O Core para no conflito e pergunta; o App mostra a tela de decisão.</summary>
public interface IResolvedorDeConflitos
{
    Task<DecisaoDeConflito> DecidirAsync(ConflitoDoGit conflito, CancellationToken cancelar = default);
}

/// <summary>Sem tela para perguntar, o seguro é abortar: o repositório volta ao estado anterior.</summary>
public sealed class ResolvedorQueAborta : IResolvedorDeConflitos
{
    public Task<DecisaoDeConflito> DecidirAsync(ConflitoDoGit conflito, CancellationToken cancelar = default) =>
        Task.FromResult(DecisaoDeConflito.Abortar);
}
