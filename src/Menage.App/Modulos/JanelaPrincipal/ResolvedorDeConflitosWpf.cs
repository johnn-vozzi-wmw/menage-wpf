using Menage.Core.Interacao;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Mostra a decisão de conflito do merge e do rebase; sem resposta (janela fechada à força) aborta, o caminho seguro.</summary>
public sealed class ResolvedorDeConflitosWpf(IServicoDeJanelas janelas) : IResolvedorDeConflitos
{
    public const string JanelaId = "git.dialogo-conflito";

    public async Task<DecisaoDeConflito> DecidirAsync(ConflitoDoGit conflito, CancellationToken cancelar = default) =>
        await janelas.AbrirDialogoAsync(JanelaId, conflito) is DecisaoDeConflito decisao ? decisao : DecisaoDeConflito.Abortar;
}
