using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Beta;
using Menage.Core.Entregas;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Liga o Gerador de Beta: motor do Core, ações do Windows, módulo de comandos, tela e diálogos.</summary>
public static class ComposicaoDoGerador
{
    public static IServiceCollection AddGeradorBetaApp(this IServiceCollection servicos)
    {
        servicos.AddGeradorBeta();
        servicos.AddSingleton<AcoesDoWindows>();
        servicos.AddSingleton<IAcoesDeEntrega>(sp => sp.GetRequiredService<AcoesDoWindows>());
        servicos.AddModulo<GeradorBetaModulo>();
        servicos.AddSingleton<GeradorBetaViewModel>();
        servicos.AddJanela<JanelaDoGerador>(JanelaDoGerador.Id);
        servicos.AddJanela<JanelaDeTexto>(JanelaDeTexto.Id);
        servicos.AddJanela<JanelaDeOrdem>(JanelaDeOrdem.Id);
        servicos.AddJanela<JanelaDeLimparTarget>(JanelaDeLimparTarget.Id);
        servicos.AddJanela<JanelaDeEntrega>(JanelaDeEntrega.Id);
        servicos.AddJanela<JanelaDeEntregas>(JanelaDeEntregas.Id);
        return servicos;
    }
}
