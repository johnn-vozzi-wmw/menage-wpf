using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.App.Controles;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Conflito;
using Menage.Core.CherryPick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.App.Modulos.CherryPick;

/// <summary>Comandos do módulo: a pílula Cherry-pick (histórico dos marcados) e o item "Fila de cherry-pick" do menu Mais.</summary>
public sealed class CherryPickModulo(IServicoDeJanelas janelas, IProjetos projetos) : IModulo
{
    public const string IdDoHistorico = "git.cherry-pick";
    public const string IdDaFila = "git.cherry-fila";
    public const string JanelaDoHistorico = "cherry.historico";

    static readonly IReadOnlyCollection<PerfilDeUso> _perfis = ConjuntosDePerfis.Somente(PerfilDeUso.Geral, PerfilDeUso.Desenvolvedor);

    public string Nome => "Cherry-pick";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando(IdDoHistorico, "Cherry-pick (histórico dos projetos marcados)", _ =>
        {
            janelas.Abrir(JanelaDoHistorico);
            return Task.CompletedTask;
        }, "Git", "ArrowSplit", Perfis: _perfis, Habilitado: () => projetos.Marcados.Count > 0));

        catalogo.Adicionar(new Comando(IdDaFila, "Fila de cherry-pick", _ =>
        {
            janelas.Abrir(JanelaCherryPickViewModel.IdDaFila);
            return Task.CompletedTask;
        }, "Mais", "TaskListSquareLtr", Perfis: _perfis));
    }
}

public static class CherryPickExtensoes
{
    /// <summary>Liga o módulo no app: comandos, janelas e o editor de conflito do módulo Diff (usado por "Resolver aqui").</summary>
    public static IServiceCollection AddCherryPickTelas(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<Menage.Core.Diff.ServicoConflito>();
        servicos.TryAddSingleton<IPerguntaDeLado, DialogosDiff>();
        servicos.TryAddSingleton<JanelaConflito.Fabrica>();
        servicos.AddTransient<JanelaCherryPickViewModel>();
        servicos.AddTransient<FilaViewModel>();
        servicos.AddJanela<JanelaCherryPick>(CherryPickModulo.JanelaDoHistorico);
        servicos.AddJanela<JanelaFila>(JanelaCherryPickViewModel.IdDaFila);
        return servicos.AddModulo<CherryPickModulo>();
    }
}
