using Menage.App.Controles;
using Menage.App.Modulos.Perfis;
using Menage.Core;
using Menage.Core.Armazenamento;
using Menage.Core.Comandos;
using Menage.Core.Configuracoes;
using Menage.Core.Interacao;
using Menage.Core.Perfis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.App.Modulos.Configuracoes;

/// <summary>Comandos de Configurações e Perfis: a paleta Ctrl+K e o menu da barra lateral abrem as mesmas janelas.</summary>
public sealed class ConfiguracoesModulo(IServicoDeJanelas janelas) : IModulo
{
    public string Nome => "Configurações e perfis";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando("config.abrir", "Configurações", Abrir(JanelaDeConfiguracoes.Id), "Configurações", "Settings"));
        catalogo.Adicionar(new Comando("config.perfis", "Perfis de uso: escolher e gerenciar", Abrir(JanelaDePerfis.Id), "Configurações", "People"));
        catalogo.Adicionar(new Comando("config.importar", "Importar configurações do Git Manager antigo",
            Abrir(JanelaDeConfiguracoes.Id, JanelaDeConfiguracoes.AbrirImportacao), "Configurações", "ArrowDownload"));
    }

    Func<CancellationToken, Task> Abrir(string id, object? parametro = null) => _ =>
    {
        janelas.Abrir(id, parametro);
        return Task.CompletedTask;
    };
}

public static class ComposicaoDeConfiguracoes
{
    /// <summary>Liga as telas de Configurações e Perfis; o motor (serviços e cofre) vem do Core.</summary>
    public static IServiceCollection AddConfiguracoesApp(this IServiceCollection servicos)
    {
        servicos.AddConfiguracoes();
        servicos.AddModulo<ConfiguracoesModulo>();
        servicos.AddTransient<ConfiguracoesViewModel>();
        servicos.AddTransient<PerfisViewModel>();
        servicos.AddJanela<JanelaDeConfiguracoes>(JanelaDeConfiguracoes.Id);
        servicos.AddJanela<JanelaDePerfis>(JanelaDePerfis.Id);
        return servicos;
    }
}

public static class ConfiguracoesCoreExtensions
{
    /// <summary>Serviços do Core: configurações gerais, cofre com DPAPI e migração do Git Manager antigo.</summary>
    public static IServiceCollection AddConfiguracoes(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton(sp => new ServicoDeConfiguracoes(sp.GetRequiredService<PastaDeDados>()));
        servicos.TryAddSingleton<IProtetorDeSegredos, ProtetorDpapi>();
        servicos.TryAddSingleton(sp => new CofreDeCredenciais(sp.GetRequiredService<PastaDeDados>(), sp.GetRequiredService<IProtetorDeSegredos>()));
        servicos.TryAddSingleton<MigradorDoGitManager>();
        return servicos;
    }
}
