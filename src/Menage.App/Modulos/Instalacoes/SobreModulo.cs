using Menage.App.Controles;
using Menage.Core.Atualizacao;
using Menage.Core.Comandos;
using Menage.Core;
using Menage.Core.Interacao;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Instalacoes;

/// <summary>Comando "Sobre / Atualizações" (menu Mais e paleta Ctrl+K).</summary>
public sealed class SobreModulo(IServicoDeJanelas janelas) : IModulo
{
    public const string IdDoComando = "app.sobre";

    public string Nome => "Sobre / Atualizações";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando(IdDoComando, "Sobre / Atualizações", _ =>
        {
            janelas.Abrir(JanelaSobre.Id);
            return Task.CompletedTask;
        }, "Mais", "Info"));
}

public static class SobreExtensoes
{
    public static IServiceCollection AddSobreAtualizacoes(this IServiceCollection servicos)
    {
        servicos.AddSingleton<IFabricaDeGerente, FabricaDeGerenteVelopack>();
        servicos.AddSingleton(sp => new ServicoDeInstalacoes(
            sp.GetRequiredService<IFabricaDeGerente>(), OpcoesDeAtualizacao.Carregar(OpcoesDeAtualizacao.CaminhoPadrao),
            OpcoesDeAtualizacao.CaminhoPadrao, VersaoDoApp.Atual));
        servicos.AddSingleton<DetectorDoAppAntigo>();
        servicos.AddTransient<SobreViewModel>();
        servicos.AddJanela<JanelaSobre>(JanelaSobre.Id);
        return servicos.AddModulo<SobreModulo>();
    }
}

public static class VersaoDoApp
{
    public static string Atual => typeof(VersaoDoApp).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
