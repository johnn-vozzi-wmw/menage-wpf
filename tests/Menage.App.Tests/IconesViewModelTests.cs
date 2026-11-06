using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Icones;
using Menage.App.Modulos.Personalizacao;
using Menage.App.Tema;
using Menage.Core;
using Menage.Core.Armazenamento;
using Menage.Core.Comandos;
using Menage.Core.Personalizacao;
using Menage.Core.Projetos;

namespace Menage.App.Tests;

public sealed class IconesViewModelTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgv" + Guid.NewGuid().ToString("N")[..6]);

    public IconesViewModelTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    sealed class AplicadorNulo : IAplicadorDeTema
    {
        public void Aplicar(DefinicaoDeTema tema, bool animar) { }
    }

    sealed class SeletorFalso(Func<string?> resposta) : ISeletorDeImagem
    {
        public int Chamadas { get; private set; }

        public string? Escolher() { Chamadas++; return resposta(); }
    }

    sealed class ConfiguracaoFalsa : IConfiguracaoDeProjetos
    {
        public CatalogoDeProjetos Catalogo { get; } = new();
        public IReadOnlyList<Projeto> Projetos { get; } = [];
        public string Aviso => "";
        public bool Ligado => false;
        public Resultado<Nada> Salvar(CatalogoDeProjetos catalogo) => Resultado.Ok();
        public Resultado<Nada> SalvarModoBeta(bool ligado) => Resultado.Ok();
        public Resultado<Nada> SalvarRaiz(string raiz) => Resultado.Ok();
        public event EventHandler? Alterado { add { } remove { } }
    }

    (IconesViewModel Modelo, ServicoDePreferencias Preferencias) Criar(ISeletorDeImagem seletor)
    {
        var pasta = new PastaDeDados(_pasta);
        var preferencias = new ServicoDePreferencias(new Armazenamento<PreferenciasDeAparencia>(pasta, "aparencia", padrao: () => new PreferenciasDeAparencia()));
        var mensageiro = new WeakReferenceMessenger();
        var catalogo = new CatalogoDeTemas();
        var tema = new ServicoDeTema(catalogo, new Armazenamento<PreferenciasDeTema>(pasta, "tema", padrao: () => new PreferenciasDeTema("escuro")),
            new AplicadorNulo(), mensageiro, new AjusteDeDestaque());
        tema.Iniciar();
        var servico = new ServicoDeIcones(preferencias, tema, mensageiro);
        return (new IconesViewModel(preferencias, servico, catalogo, new CatalogoDeComandos(), new ConfiguracaoFalsa(), seletor), preferencias);
    }

    string CriarArquivo(string nome, bool imagemValida)
    {
        var caminho = Path.Combine(_pasta, nome);
        if (!imagemValida) { File.WriteAllText(caminho, "nao sou imagem"); return caminho; }
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[64], 16)));
        using var fluxo = File.Create(caminho);
        png.Save(fluxo);
        return caminho;
    }

    [Fact]
    public void Escolher_abre_o_seletor_e_grava_o_caminho_na_linha_e_nas_preferencias()
    {
        WpfDeTeste.Executar(() =>
        {
            var png = CriarArquivo("a.png", true);
            var seletor = new SeletorFalso(() => png);
            var (modelo, preferencias) = Criar(seletor);

            modelo.EscolherImagemCommand.CanExecute(null).Should().BeTrue();
            modelo.EscolherImagemCommand.Execute(null);

            seletor.Chamadas.Should().Be(1);
            modelo.Caminho.Should().Be(png);
            preferencias.Valor.Icones.PorTema["escuro"].Should().Be(png);
            modelo.Mensagem.Should().BeEmpty();
        });
    }

    [Fact]
    public void Cancelar_o_seletor_nao_muda_nada()
    {
        WpfDeTeste.Executar(() =>
        {
            var (modelo, preferencias) = Criar(new SeletorFalso(() => null));
            modelo.EscolherImagemCommand.Execute(null);

            modelo.Caminho.Should().BeNull();
            preferencias.Valor.Icones.PorTema.Should().BeEmpty();
        });
    }

    [Fact]
    public void Arquivo_invalido_ou_extensao_fora_da_lista_vira_mensagem_e_nao_grava()
    {
        WpfDeTeste.Executar(() =>
        {
            var ruim = CriarArquivo("ruim.png", false);
            var txt = CriarArquivo("nota.txt", false);
            foreach (var caminho in new[] { ruim, txt })
            {
                var (modelo, _) = Criar(new SeletorFalso(() => caminho));
                modelo.EscolherImagemCommand.Execute(null);
                modelo.Mensagem.Should().NotBeEmpty();
                modelo.Caminho.Should().BeNull();
            }
        });
    }

    [Fact]
    public void Falha_ao_abrir_o_seletor_vira_mensagem_em_vez_de_excecao()
    {
        WpfDeTeste.Executar(() =>
        {
            var (modelo, _) = Criar(new SeletorFalso(() => throw new InvalidOperationException("sem janela")));
            modelo.EscolherImagemCommand.Execute(null);
            modelo.Mensagem.Should().Contain("sem janela");
        });
    }

    [Fact]
    public void Filtro_do_seletor_aceita_imagens_e_gif()
    {
        foreach (var ext in new[] { "png", "jpg", "jpeg", "bmp", "ico", "gif" })
            SeletorDeImagemWindows.Filtro.Should().Contain("*." + ext);
    }
}
