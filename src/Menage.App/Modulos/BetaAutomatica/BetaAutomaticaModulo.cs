using Menage.App.Controles;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core;
using Menage.Core.BetaAutomatica;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Perfis;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>
/// Comandos da Beta automática (perfis Desenvolvedor e Tester; o perfil Geral não vê o módulo). Atalhos: Ctrl+Shift+B abre o painel
/// (o Ctrl+B da janela principal recolhe a barra lateral) e Ctrl+Shift+T expande o console ancorado. Configurar, JQL e Diagnóstico são abas da mesma janela.
/// </summary>
public sealed class BetaAutomaticaModulo(IServicoDeJanelas janelas, ServicoDeBetaAutomatica servico) : IModulo
{
    static readonly IReadOnlyCollection<PerfilDeUso> Perfis = ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor, PerfilDeUso.Tester);

    public string Nome => "Beta automática";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(Abrir("beta-auto.painel", "Betas habilitadas (Beta automática)", new PedidoDeAba(AbaDaBeta.Betas), "Beaker", "Ctrl+Shift+B"));
        catalogo.Adicionar(Abrir("beta-auto.configurar", "Configurar a Beta automática", new PedidoDeAba(AbaDaBeta.Configurar), "Settings"));
        catalogo.Adicionar(Abrir("beta-auto.jql", "JQL da busca da Beta automática", new PedidoDeAba(AbaDaBeta.Jql), "Search"));
        catalogo.Adicionar(Abrir("beta-auto.diagnostico", "Diagnóstico da busca no Jira", new PedidoDeAba(AbaDaBeta.Diagnostico), "Stethoscope"));
        // O console não é aba: abre a janela na aba em que estava e expande o console ancorado.
        catalogo.Adicionar(Abrir("beta-auto.console", "Console da geração da beta", new PedidoDeAba(null, null, Console: true), "WindowConsole", "Ctrl+Shift+T"));
        catalogo.Adicionar(new Comando("beta-auto.verificar", "Verificar a Beta automática agora", ct =>
        {
            servico.Iniciar();
            servico.VerificarAgora();
            if (!servico.Config.Ativo) _ = servico.CicloAsync(false, ct);
            return Task.CompletedTask;
        }, "Beta", "ArrowSync", Perfis: Perfis));
    }

    Comando Abrir(string id, string texto, PedidoDeAba pedido, string icone, string? atalho = null) =>
        new(id, texto, _ =>
        {
            janelas.Abrir(JanelaBetaAutomatica.Id, pedido);
            return Task.CompletedTask;
        }, "Beta", icone, atalho, Perfis);
}

/// <summary>Liga o motor da Beta automática ao app (monitor em segundo plano, alerta, pergunta da ordem dos projetos).</summary>
public sealed class InicioDaBetaAutomatica(ServicoDeBetaAutomatica servico, IServicoDeJanelas janelas, IPerfilAtual perfil)
{
    public void Iniciar()
    {
        servico.PerguntarOrdem = async ordem => await janelas.AbrirDialogoAsync(JanelaDeOrdem.Id, ordem.ToList()) as List<string>;
        servico.ItemPronto += item => janelas.Abrir(JanelaBetaAutomatica.Id, item.Chave);
        if (perfil.Atual is not (PerfilDeUso.Desenvolvedor or PerfilDeUso.Tester)) return;

        servico.DestravarPresas();
        if (servico.Config.Ativo) servico.Iniciar();
    }
}

public static class ComposicaoDaBetaAutomatica
{
    public static IServiceCollection AddBetaAutomaticaApp(this IServiceCollection servicos)
    {
        servicos.AddBetaAutomatica();
        servicos.AddModulo<BetaAutomaticaModulo>();
        servicos.AddSingleton<ConfigBetaViewModel>();
        servicos.AddSingleton<JqlViewModel>();
        servicos.AddSingleton<DiagnosticoViewModel>();
        servicos.AddSingleton<ConsoleDoDockViewModel>();
        servicos.AddSingleton<BetaAutomaticaViewModel>();
        servicos.AddSingleton<InicioDaBetaAutomatica>();
        servicos.AddJanela<JanelaBetaAutomatica>(JanelaBetaAutomatica.Id);
        return servicos;
    }
}
