using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Melhorias;
using Menage.Core.Perfis;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Melhorias;

/// <summary>Comandos das Melhorias. Só o perfil Desenvolvedor vê o módulo (Geral e Tester não). Atalho do painel: Ctrl+M.</summary>
public sealed class MelhoriasModulo(IServicoDeJanelas janelas, RepositorioDeMelhorias repositorio, MelhoriasViewModel modelo) : IModulo
{
    static readonly IReadOnlyCollection<PerfilDeUso> Perfis = ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor);

    public string Nome => "Melhorias";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando("melhorias.painel", "Melhorias em andamento (painel)", _ =>
        {
            janelas.Abrir(JanelaMelhorias.Id);
            return Task.CompletedTask;
        }, "Melhorias", "Rocket", "Ctrl+M", Perfis));
        catalogo.Adicionar(new Comando("melhorias.nova", "Nova melhoria", async _ =>
        {
            janelas.Abrir(JanelaMelhorias.Id);
            await modelo.NovaAsync();
        }, "Melhorias", "Add", Perfis: Perfis));
        catalogo.Adicionar(new Comando("melhorias.desvincular", "Desvincular a melhoria ativa", _ => modelo.DesvincularAsync(),
            "Melhorias", "LinkDismiss", Perfis: Perfis, Habilitado: () => repositorio.Config.AtivaMelhoria is not null));
    }
}

public static class ComposicaoDasMelhorias
{
    public static IServiceCollection AddMelhoriasApp(this IServiceCollection servicos)
    {
        servicos.AddMelhorias();
        servicos.AddModulo<MelhoriasModulo>();
        servicos.AddSingleton<MelhoriasViewModel>();
        servicos.AddJanela<JanelaMelhorias>(JanelaMelhorias.Id);
        servicos.AddJanela<JanelaEditorDeMelhoria>(JanelaEditorDeMelhoria.Id);
        return servicos;
    }
}
