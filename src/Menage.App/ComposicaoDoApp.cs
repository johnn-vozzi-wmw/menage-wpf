using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Controles;
using Menage.App.Galeria;
using Menage.App.Tema;
using Menage.Core;
using Menage.Core.Armazenamento;
using Menage.Core.Atualizacao;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Menage.App;

/// <summary>Como o app foi aberto: normal, <c>--galeria &lt;pasta&gt;</c> (automático) ou <c>--catalogo</c> (a mesma tela, para clicar).</summary>
public sealed record ModoDeInicio(bool Galeria, string? PastaDaGaleria)
{
    public static ModoDeInicio Ler(string[] argumentos)
    {
        var indice = Array.IndexOf(argumentos, "--galeria");
        if (indice >= 0 && indice + 1 < argumentos.Length) return new ModoDeInicio(true, argumentos[indice + 1]);
        return new ModoDeInicio(argumentos.Contains("--catalogo"), null);
    }
}

/// <summary>Toda a ligação de dependências do app.</summary>
public static class ComposicaoDoApp
{
    public static ServiceProvider Montar(ModoDeInicio modo)
    {
        // A galeria troca de tema sem parar: usa pasta própria para não mexer no tema salvo do usuário.
        var pasta = modo.Galeria ? new PastaDeDados(Path.Combine(Path.GetTempPath(), "menage-galeria")) : PastaDeDados.Padrao();
        var servicos = new ServiceCollection();
        servicos.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        servicos.AddMenageCore(pasta);
        AdicionarAparencia(servicos, pasta);
        AdicionarTelas(servicos);
        if (modo.Galeria) AdicionarGaleria(servicos);
        return servicos.BuildServiceProvider();
    }

    static void AdicionarAparencia(IServiceCollection servicos, PastaDeDados pasta)
    {
        servicos.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        servicos.AddSingleton<ICatalogoDeTemas, CatalogoDeTemas>();
        servicos.AddSingleton<IAplicadorDeTema, AplicadorDeTemaWpf>();
        servicos.AddSingleton<IServicoDeTema, ServicoDeTema>();
        servicos.AddSingleton<IArmazenamento<PreferenciasDeTema>>(
            new Armazenamento<PreferenciasDeTema>(pasta, "tema", padrao: () => new PreferenciasDeTema(CatalogoDeTemas.IdEscuro)));
        servicos.AddSingleton<IArmazenamento<ArquivoDeTemasPrivados>>(
            new Armazenamento<ArquivoDeTemasPrivados>(pasta, "temas-privados", padrao: () => new ArquivoDeTemasPrivados([])));
        servicos.AddSingleton<CarregadorDeTemasPrivados>();
        servicos.AddModulo<AparenciaModulo>();
    }

    static void AdicionarTelas(IServiceCollection servicos)
    {
        servicos.AddSingleton<IServicoDeDialogos, ServicoDeDialogos>();
        servicos.AddSingleton<IServicoDeJanelas, ServicoDeJanelas>();
        servicos.AddTransient<PaletaDeComandosViewModel>();
        servicos.AddSingleton(sp => new ContextoDeJanela(
            sp.GetRequiredService<IServicoDeTema>(), sp.GetRequiredService<PaletaDeComandosViewModel>));
        servicos.AddSingleton<IServicoDeAtualizacao>(_ =>
            new ServicoDeAtualizacaoVelopack(OpcoesDeAtualizacao.Carregar(OpcoesDeAtualizacao.CaminhoPadrao), OpcoesDeAtualizacao.CaminhoPadrao));
        servicos.AddSingleton<CoordenadorDeAtualizacao>();
        servicos.AddSingleton<MainWindow>();
    }

    static void AdicionarGaleria(IServiceCollection servicos)
    {
        servicos.AddModulo<ComandosDeExemploModulo>();
        servicos.AddSingleton<JanelaDaGaleria>();
        servicos.AddSingleton<RoteiroDaGaleria>();
    }
}
