using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.Fetch;
using Menage.Core.Operacoes.Pull;
using Menage.Core.Operacoes.Stash;
using Menage.Core.Operacoes.Sync;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.Core.Saida;

namespace Menage.App.Modulos.JanelaPrincipal;

public sealed record PedidoDeCheckout(IReadOnlyList<string> Sugestoes, IReadOnlyList<string> Projetos);

public sealed record PedidoDeStash(IReadOnlyList<PendenciaDeStash> Pendencias);

public sealed record PedidoDeStashPop(IReadOnlyList<EntradaDeStash> Entradas);

/// <summary>
/// As ações Git da janela: pergunta o que falta (branch, nomes de stash...) por diálogo e entrega a operação ao coordenador.
/// A lógica de cada operação continua no Core; aqui só há o encadeamento tela, operação e aviso.
/// </summary>
public sealed class AcoesGit(
    IProjetos projetos, CoordenadorDeOperacoes coordenador, SaidaDeOperacoes saida, IServicoDeJanelas janelas,
    FetchEmLote fetch, PullEmLote pull, SyncEmLote sync, CheckoutEmLote checkout, StashEmLote stash, StashPopEmLote stashPop,
    SugestoesDeBranches sugestoes)
{
    public const string JanelaDeCheckout = "git.dialogo-checkout";
    public const string JanelaDeStash = "git.dialogo-stash";
    public const string JanelaDeStashPop = "git.dialogo-stash-pop";

    /// <summary>Texto curto para o aviso do canto (título, mensagem, grave?).</summary>
    public event Action<string, string, bool>? Aviso;

    public Task FetchAsync() => Proteger(() => Rodar(fetch, new OpcoesDeFetch()));

    public Task PullAsync() => Proteger(() => Rodar(pull, new OpcoesDePull()));

    public Task SyncAsync() => Proteger(() => Rodar(sync, new OpcoesDeSync()));

    public Task CheckoutAsync() => Proteger(async () =>
    {
        var marcados = projetos.Marcados;
        if (marcados.Count == 0 || coordenador.EmAndamento) return;
        var nomes = await sugestoes.BasesAsync(marcados);
        if (await janelas.AbrirDialogoAsync(JanelaDeCheckout, new PedidoDeCheckout(nomes, [.. marcados.Select(p => p.Nome)])) is OpcoesDeCheckout opcoes)
            await Rodar(checkout, opcoes);
    });

    public Task StashAsync() => Proteger(async () =>
    {
        if (projetos.Marcados.Count == 0 || coordenador.EmAndamento) return;
        var pendencias = await stash.ListarPendenciasAsync(projetos.Marcados);
        if (pendencias.Count == 0)
        {
            saida.Escrever("Stash: os projetos marcados não têm alterações para guardar.", TipoDeLinha.Aviso);
            Aviso?.Invoke("Stash", "Os projetos marcados não têm alterações para guardar.", false);
            return;
        }

        if (await janelas.AbrirDialogoAsync(JanelaDeStash, new PedidoDeStash(pendencias)) is OpcoesDeStash opcoes) await Rodar(stash, opcoes);
    });

    public Task StashPopAsync() => Proteger(async () =>
    {
        if (projetos.Marcados.Count == 0 || coordenador.EmAndamento) return;
        var entradas = await stashPop.ListarAsync(projetos.Marcados);
        if (entradas.Count == 0)
        {
            saida.Escrever("Stash pop: os projetos marcados não têm stash guardado.", TipoDeLinha.Aviso);
            Aviso?.Invoke("Stash pop", "Os projetos marcados não têm stash guardado.", false);
            return;
        }

        if (await janelas.AbrirDialogoAsync(JanelaDeStashPop, new PedidoDeStashPop(entradas)) is OpcoesDeStashPop opcoes) await Rodar(stashPop, opcoes);
    });

    public async Task Rodar<TOpcoes, TResultado>(OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes)
    {
        var resultado = await coordenador.ExecutarAsync(operacao, opcoes);
        if (resultado is null) Aviso?.Invoke(operacao.Nome, "Já há uma operação em andamento.", false);
        else if (!resultado.EhOk && resultado.Erro != Erro.Cancelado) Aviso?.Invoke(operacao.Nome, resultado.Erro.ToString(), true);
    }

    // O comando da pílula é assíncrono: exceção solta derrubaria o app, então vai para a Saída.
    public async Task Proteger(Func<Task> acao)
    {
        try
        {
            await acao();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            saida.Escrever($"Falha inesperada: {ex.Message}", TipoDeLinha.Erro);
            Aviso?.Invoke("Falha inesperada", ex.Message, true);
        }
    }
}
