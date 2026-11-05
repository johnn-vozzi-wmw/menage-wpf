using Menage.Core.Comandos;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Pedidos da paleta e das pílulas que só a janela principal sabe atender (buscar, recolher a barra...).</summary>
public sealed class PedidosDaJanela
{
    public event Action<string>? Pedido;

    public void Pedir(string id) => Pedido?.Invoke(id);
}

/// <summary>
/// Registra no catálogo o que a janela principal oferece: as operações Git implementadas, os comandos da própria janela e,
/// apagados com a dica "Em breve", os que outros módulos ainda vão trazer (cada um substitui o seu quando existir).
/// </summary>
public sealed class ComandosDaJanelaModulo(AcoesGit acoes, PedidosDaJanela pedidos, CoordenadorDeOperacoes coordenador, IProjetos projetos) : IModulo
{
    public static readonly IReadOnlyList<string> EmBreve =
    [
        "git.cherry-pick", "mais.historico", "beta.gerar",
    ];

    public string Nome => "Janela principal";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        bool PodeOperar() => !coordenador.EmAndamento && projetos.Marcados.Count > 0;

        void Git(string id, string texto, Func<Task> acao, string icone, string? atalho = null) =>
            catalogo.Adicionar(new Comando(id, texto, _ => acao(), "Git", icone, atalho, Habilitado: PodeOperar));

        Git("git.fetch", "Fetch dos projetos marcados", acoes.FetchAsync, "ArrowSync");
        Git("git.pull", "Pull dos projetos marcados", acoes.PullAsync, "ArrowDownload");
        Git("git.sync", "Sync (stash, pull e stash pop)", acoes.SyncAsync, "ArrowSyncCircle");
        Git("git.checkout", "Checkout de branch ou tag", acoes.CheckoutAsync, "BranchFork");
        Git("git.stash", "Stash com nome por projeto", acoes.StashAsync, "Box");
        Git("git.stash-pop", "Stash pop", acoes.StashPopAsync, "BoxArrowUp");

        Janela(catalogo, "janela.atualizar", "Atualizar situação dos projetos", "ArrowClockwise", "F5");
        Janela(catalogo, "janela.buscar-projetos", "Buscar projetos", "Search", "Ctrl+F");
        Janela(catalogo, "janela.barra-lateral", "Recolher ou expandir a barra lateral", "PanelLeft", "Ctrl+B");
        Janela(catalogo, "janela.saida", "Mostrar ou recolher a Saída", "WindowConsole");
        Janela(catalogo, "janela.marcar-todos", "Marcar todos os projetos", "CheckboxChecked", "Ctrl+A");
        Janela(catalogo, "janela.limpar-selecao", "Limpar a seleção de projetos", "Dismiss");
        Janela(catalogo, "projetos.gerenciar", "Gerenciar projetos (adicionar ou remover)", "FolderAdd");

        EmBrevePara(catalogo, "git.cherry-pick", "Cherry-pick", "ArrowSplit", "Git");
        EmBrevePara(catalogo, "mais.historico", "Histórico de releases", "History", "Mais");
        EmBrevePara(catalogo, "beta.gerar", "Gerar beta", "Rocket", "Beta");
    }

    void Janela(ICatalogoDeComandos catalogo, string id, string texto, string icone, string? atalho = null) =>
        catalogo.Adicionar(new Comando(id, texto, _ =>
        {
            pedidos.Pedir(id);
            return Task.CompletedTask;
        }, "Janela", icone, atalho));

    static void EmBrevePara(ICatalogoDeComandos catalogo, string id, string texto, string icone, string grupo) =>
        catalogo.Adicionar(new Comando(id, texto, _ => Task.CompletedTask, grupo, icone, Perfis: ConjuntosDePerfis.Todos, Habilitado: () => false));
}
