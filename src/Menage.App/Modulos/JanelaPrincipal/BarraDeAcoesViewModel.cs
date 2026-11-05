using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.Comandos;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Como uma pílula da barra se liga ao catálogo: o comando principal, ou os do menu.</summary>
public sealed record DefinicaoDePilula(
    string Texto, string Icone, TomDeCor Tom, string? IdPrincipal, IReadOnlyList<string>? IdsDoMenu = null, bool Destaque = false);

/// <summary>Um item do menu de uma pílula, com o motivo quando está apagado.</summary>
public sealed record ItemDeMenu(IComando Comando, string? Motivo)
{
    public bool Habilitado => Motivo is null;
}

/// <summary>Uma pílula pronta para a tela. <see cref="Motivo"/> não nulo = apagada, com esse texto como dica.</summary>
public sealed partial class AcaoDaBarra(DefinicaoDePilula definicao) : ObservableObject
{
    [ObservableProperty]
    string? _motivo;

    [ObservableProperty]
    IComando? _principal;

    [ObservableProperty]
    IReadOnlyList<ItemDeMenu> _menu = [];

    public DefinicaoDePilula Definicao { get; } = definicao;

    public bool TemMenu => Definicao.IdsDoMenu is not null;

    public bool Habilitada => Motivo is null;
}

/// <summary>
/// As pílulas da barra de ações, na ordem do Git Manager. Tudo vem do catálogo; o que ainda não existe ou o perfil não libera
/// fica apagado, com o motivo na dica.
/// </summary>
public sealed class BarraDeAcoesViewModel : IDisposable
{
    public static readonly IReadOnlyList<DefinicaoDePilula> Definicoes =
    [
        new("Fetch", "ArrowSync", TomDeCor.Azul, "git.fetch"),
        new("Pull", "ArrowDownload", TomDeCor.Verde, "git.pull"),
        new("Checkout", "BranchFork", TomDeCor.Laranja, "git.checkout"),
        new("Commit", "Checkmark", TomDeCor.Verde, "git.commit"),
        new("Push", "ArrowUpload", TomDeCor.Azul, "git.push"),
        new("Cherry-pick", "ArrowSplit", TomDeCor.Laranja, "git.cherry-pick"),
        new("Stash", "Box", TomDeCor.Amarelo, null, ["git.stash", "git.stash-pop", "git.sync"]),
        new("Branch", "BranchFork", TomDeCor.Neutro, null, ["git.branch-criar", "git.branch-renomear", "git.branch-excluir", "git.rebase", "git.com-develop"]),
        new("Mais", "MoreHorizontal", TomDeCor.Neutro, null, ["janela.marcar-todos", "janela.limpar-selecao", "mais.diagnostico", "mais.historico", "projetos.gerenciar"]),
    ];

    public static readonly DefinicaoDePilula Beta = new("Gerar beta", "Rocket", TomDeCor.Destaque, "beta.gerar", Destaque: true);

    readonly ICatalogoDeComandos _catalogo;
    readonly IPerfilAtual _perfil;
    readonly CoordenadorDeOperacoes _coordenador;
    readonly IProjetos _projetos;

    public BarraDeAcoesViewModel(ICatalogoDeComandos catalogo, IPerfilAtual perfil, CoordenadorDeOperacoes coordenador, IProjetos projetos)
    {
        (_catalogo, _perfil, _coordenador, _projetos) = (catalogo, perfil, coordenador, projetos);
        Acoes = [.. Definicoes.Select(d => new AcaoDaBarra(d))];
        AcaoBeta = new AcaoDaBarra(Beta);
        Atualizar();
        _coordenador.Alterado += Atualizar;
        _projetos.Alterado += AoAlterarProjetos;
    }

    public IReadOnlyList<AcaoDaBarra> Acoes { get; }

    public AcaoDaBarra AcaoBeta { get; }

    public void Dispose()
    {
        _coordenador.Alterado -= Atualizar;
        _projetos.Alterado -= AoAlterarProjetos;
    }

    /// <summary>Releitura dos motivos: muda com a operação em curso, a seleção e o perfil.</summary>
    public void Atualizar()
    {
        foreach (var acao in Acoes.Append(AcaoBeta)) Preencher(acao);
    }

    /// <summary>Por que o comando não pode rodar agora (nulo se pode).</summary>
    public string? Motivo(IComando comando)
    {
        if (!comando.PerfisQueLiberam.Contains(_perfil.Atual)) return $"Disponível só no perfil {string.Join(" ou ", comando.PerfisQueLiberam)}";
        if (comando.PodeExecutar()) return null;
        if (ComandosDaJanelaModulo.EmBreve.Contains(comando.Id)) return "Em breve";
        return _coordenador.EmAndamento ? "Aguarde a operação terminar" : "Marque ao menos um projeto na grade";
    }

    void AoAlterarProjetos(object? origem, EventArgs e) => Atualizar();

    void Preencher(AcaoDaBarra acao)
    {
        var definicao = acao.Definicao;
        if (definicao.IdsDoMenu is { } ids)
        {
            var itens = ids.Select(_catalogo.Obter).OfType<IComando>().Select(c => new ItemDeMenu(c, Motivo(c))).ToList();
            acao.Menu = itens;
            acao.Principal = null;
            acao.Motivo = itens.Count == 0 ? "Em breve" : itens.TrueForAll(i => !i.Habilitado) ? itens[0].Motivo : null;
            return;
        }

        var comando = definicao.IdPrincipal is null ? null : _catalogo.Obter(definicao.IdPrincipal);
        acao.Principal = comando;
        acao.Motivo = comando is null ? "Em breve" : Motivo(comando);
    }
}
