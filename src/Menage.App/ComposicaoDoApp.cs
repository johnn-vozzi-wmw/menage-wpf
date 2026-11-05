using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Animacoes;
using Menage.App.Controles;
using Menage.App.Galeria;
using Menage.App.Modulos.Banco;
using Menage.App.Modulos.CherryPick;
using Menage.App.Modulos.Diario;
using Menage.App.Modulos.GeradorBeta;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote;
using Menage.App.Tema;
using Menage.Core;
using Menage.Core.Armazenamento;
using Menage.Core.Atualizacao;
using Menage.Core.Comandos;
using Menage.Core.Estado;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Projetos;
using Menage.Core.Saida;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Menage.App;

/// <summary>
/// Como o app foi aberto: normal; <c>--projetos &lt;pasta&gt;</c> (usa os repositórios de uma pasta em vez da configuração);
/// <c>--catalogo</c> / <c>--galeria &lt;pasta&gt;</c> (design system); <c>--galeria-principal &lt;pasta&gt;</c> (janela principal com dados de exemplo).
/// </summary>
public sealed record ModoDeInicio(bool Galeria, string? PastaDaGaleria, string? PastaDeProjetos = null, string? PastaDaGaleriaPrincipal = null, string? PastaDaGaleriaGerador = null, string? PastaDaGaleriaDiarioBanco = null)
{
    public bool GaleriaPrincipal => PastaDaGaleriaPrincipal is not null;

    public bool GaleriaGerador => PastaDaGaleriaGerador is not null;

    public bool GaleriaDiarioBanco => PastaDaGaleriaDiarioBanco is not null;

    public static ModoDeInicio Ler(string[] argumentos)
    {
        var projetos = Valor(argumentos, "--projetos");
        var principal = Valor(argumentos, "--galeria-principal");
        if (principal is not null) return new ModoDeInicio(false, null, null, principal);
        if (Valor(argumentos, "--galeria-gerador") is { } gerador) return new ModoDeInicio(false, null, null, null, gerador);
        if (Valor(argumentos, "--galeria-diario-banco") is { } diario) return new ModoDeInicio(false, null, null, null, null, diario);
        var galeria = Valor(argumentos, "--galeria");
        return galeria is not null
            ? new ModoDeInicio(true, galeria, projetos)
            : new ModoDeInicio(argumentos.Contains("--catalogo"), null, projetos);
    }

    static string? Valor(string[] argumentos, string nome)
    {
        var indice = Array.IndexOf(argumentos, nome);
        return indice >= 0 && indice + 1 < argumentos.Length ? argumentos[indice + 1] : null;
    }
}

/// <summary>Toda a ligação de dependências do app.</summary>
public static class ComposicaoDoApp
{
    public static ServiceProvider Montar(ModoDeInicio modo)
    {
        // As galerias trocam de tema sem parar: usam pasta própria para não mexer no tema salvo do usuário.
        var temporaria = modo.Galeria || modo.GaleriaPrincipal || modo.GaleriaGerador || modo.GaleriaDiarioBanco;
        var pasta = temporaria ? new PastaDeDados(Path.Combine(Path.GetTempPath(), "menage-galeria")) : PastaDeDados.Padrao();
        var servicos = new ServiceCollection();
        servicos.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        EscolherProjetos(servicos, modo);
        servicos.AddMenageCore(pasta);
        AdicionarAparencia(servicos, pasta);
        AdicionarTelas(servicos);
        AdicionarJanelaPrincipal(servicos, pasta, modo);
        if (modo.Galeria) AdicionarGaleria(servicos);
        if (modo.GaleriaPrincipal) AdicionarGaleriaPrincipal(servicos);
        if (modo.GaleriaGerador) servicos.AddSingleton<RoteiroDoGerador>();
        if (modo.GaleriaDiarioBanco) servicos.AddSingleton<RoteiroDoDiarioEBanco>();
        return servicos.BuildServiceProvider();
    }

    // Registrado antes do Core, que só põe a configuração real se ninguém pôs outra (TryAdd).
    static void EscolherProjetos(IServiceCollection servicos, ModoDeInicio modo)
    {
        if (modo.GaleriaPrincipal || modo.GaleriaGerador)
        {
            servicos.AddSingleton<IConfiguracaoDeProjetos, ConfiguracaoDeExemplo>();
            servicos.AddSingleton<Menage.Core.Banco.IServicoDeBanco, Menage.Core.Banco.ServicoDeBancoFalso>();
        }
        else if (modo.GaleriaDiarioBanco)
        {
            var emMemoria = new ProjetosEmMemoria();
            servicos.AddSingleton(emMemoria);
            servicos.AddSingleton<IProjetos>(emMemoria);
        }
        else if (modo.PastaDeProjetos is { } pasta)
            servicos.AddSingleton<IConfiguracaoDeProjetos>(_ => ConfiguracaoDeTeste(pasta));
    }

    static ConfiguracaoDeProjetos ConfiguracaoDeTeste(string pasta)
    {
        var completa = Path.GetFullPath(pasta);
        var destino = Path.Combine(Path.GetTempPath(), "menage-projetos-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(completa)))[..8]);
        var configuracao = new ConfiguracaoDeProjetos(new PastaDeDados(destino));
        configuracao.Salvar(CatalogoDePasta.Criar(completa));
        return configuracao;
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
        // O resumo de lote vai para a Saída da janela principal; confirmações e erros seguem como diálogos.
        servicos.AddSingleton<ServicoDeDialogos>();
        servicos.AddSingleton(sp => new DialogosComResumoNaSaida(sp.GetRequiredService<ServicoDeDialogos>(), sp.GetRequiredService<SaidaDeOperacoes>()));
        servicos.AddSingleton<IServicoDeDialogos>(sp => sp.GetRequiredService<DialogosComResumoNaSaida>());
        servicos.AddSingleton<IServicoDeJanelas, ServicoDeJanelas>();
        servicos.AddTransient<PaletaDeComandosViewModel>();
        servicos.AddSingleton(sp => new ContextoDeJanela(
            sp.GetRequiredService<IServicoDeTema>(), sp.GetRequiredService<PaletaDeComandosViewModel>,
            sp.GetRequiredService<ServicoDeTransicao>()));
        servicos.AddSingleton<ServicoDeTransicao>();
        servicos.AddSingleton<IServicoDeAtualizacao>(_ =>
            new ServicoDeAtualizacaoVelopack(OpcoesDeAtualizacao.Carregar(OpcoesDeAtualizacao.CaminhoPadrao), OpcoesDeAtualizacao.CaminhoPadrao));
        servicos.AddSingleton<CoordenadorDeAtualizacao>();
    }

    static void AdicionarJanelaPrincipal(IServiceCollection servicos, PastaDeDados pasta, ModoDeInicio modo)
    {
        servicos.AddSingleton<IArmazenamento<PreferenciasDaJanela>>(
            new Armazenamento<PreferenciasDaJanela>(pasta, PreferenciasDaJanela.Nome, padrao: () => new PreferenciasDaJanela()));
        servicos.AddSingleton<PreferenciasDaJanelaServico>();
        servicos.AddSingleton(new OrigemDosProjetos(
            modo.PastaDeProjetos is { } p ? $"Projetos: pasta de teste {Path.GetFullPath(p)}" : modo.GaleriaPrincipal ? "Projetos: dados de exemplo" : null));
        servicos.AddSingleton<PedidosDaJanela>();
        servicos.AddSingleton<AcoesGit>();
        servicos.AddModulo<ComandosDaJanelaModulo>();
        servicos.AddCherryPickTelas();
        servicos.AddGeradorBetaApp();
        servicos.AddBancoApp();
        servicos.AddDiarioApp();
        servicos.AddSingleton<GradeDeProjetosViewModel>();
        servicos.AddSingleton<BarraLateralViewModel>();
        servicos.AddSingleton<SaidaViewModel>();
        servicos.AddSingleton<BarraDeAcoesViewModel>();
        servicos.AddSingleton<JanelaPrincipalViewModel>();
        servicos.AddSingleton<JanelaPrincipal>();
        servicos.AddJanela<JanelaDeCheckout>(AcoesGit.JanelaDeCheckout);
        servicos.AddJanela<JanelaDeStash>(AcoesGit.JanelaDeStash);
        servicos.AddJanela<JanelaDeStashPop>(AcoesGit.JanelaDeStashPop);
        servicos.AddJanela<JanelaDeProjetos>(JanelaDeProjetos.Id);
        servicos.AddModuloOperacoesLote();
    }

    static void AdicionarGaleria(IServiceCollection servicos)
    {
        servicos.AddModulo<ComandosDeExemploModulo>();
        servicos.AddSingleton<JanelaDaGaleria>();
        servicos.AddSingleton<RoteiroDaGaleria>();
        servicos.AddJanela<JanelaDeExemplo>(JanelaDeExemplo.Id);
    }

    static void AdicionarGaleriaPrincipal(IServiceCollection servicos)
    {
        servicos.AddSingleton<ILeitorDeSituacao, LeitorDeExemplo>();
        servicos.AddSingleton<RoteiroDaJanelaPrincipal>();
    }
}
