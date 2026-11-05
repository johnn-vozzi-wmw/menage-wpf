using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Icones;
using Menage.App.Tema;
using Menage.Core.Armazenamento;
using Menage.Core.Personalizacao;

namespace Menage.App.Tests;

public sealed class ServicoDeIconesTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgi" + Guid.NewGuid().ToString("N")[..6]);
    readonly ServicoDePreferencias _preferencias;
    readonly ServicoDeIcones _servico;

    public ServicoDeIconesTests()
    {
        Directory.CreateDirectory(_pasta);
        var pasta = new PastaDeDados(_pasta);
        _preferencias = new ServicoDePreferencias(new Armazenamento<PreferenciasDeAparencia>(pasta, "aparencia", padrao: () => new PreferenciasDeAparencia()));
        var mensageiro = new WeakReferenceMessenger();
        var tema = new ServicoDeTema(new CatalogoDeTemas(),
            new Armazenamento<PreferenciasDeTema>(pasta, "tema", padrao: () => new PreferenciasDeTema("escuro")), new AplicadorNulo(), mensageiro, new Menage.App.Modulos.Personalizacao.AjusteDeDestaque());
        tema.Iniciar();
        _servico = new ServicoDeIcones(_preferencias, tema, mensageiro);
    }

    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    string CriarPng(string nome)
    {
        var caminho = Path.Combine(_pasta, nome);
        var pixels = new byte[4 * 4 * 4];
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16)));
        using var fluxo = File.Create(caminho);
        png.Save(fluxo);
        return caminho;
    }

    [Fact]
    public void Cada_arquivo_e_decodificado_uma_vez_e_arquivo_invalido_vira_nulo()
    {
        WpfDeTeste.Executar(() =>
        {
            var png = CriarPng("a.png");
            var ruim = Path.Combine(_pasta, "ruim.png");
            File.WriteAllText(ruim, "nao sou imagem");

            _servico.Imagem(png).Should().NotBeNull().And.BeSameAs(_servico.Imagem(png));
            _servico.Imagem(ruim).Should().BeNull();
            _servico.Imagem(Path.Combine(_pasta, "nao-existe.png")).Should().BeNull();
        });
    }

    [Fact]
    public void Controle_troca_o_padrao_pelo_icone_do_grupo_so_com_a_caixa_marcada()
    {
        WpfDeTeste.Executar(() =>
        {
            ServicoDeIcones.Compartilhado = _servico;
            var padrao = new FrameworkElement[] { new System.Windows.Controls.Border() }[0];
            var controle = new IconeOuPadrao { Uso = UsoDoIcone.Grupo, Id = "WEB", Padrao = padrao };
            controle.Measure(new Size(20, 20));
            var png = CriarPng("g.png");
            var avisos = 0;
            _servico.Alterado += () => avisos++;

            _preferencias.Alterar(p => p with { Icones = p.Icones.ComIconeDe(AlvoDeIcone.Grupo, "WEB", png) });
            _servico.Caminho(UsoDoIcone.Grupo, "WEB").Should().BeNull("a caixa de grupos e projetos está desmarcada");

            _preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarEmGruposEProjetos = true } });
            _servico.Caminho(UsoDoIcone.Grupo, "WEB").Should().Be(png);
            avisos.Should().Be(2);
            _servico.Caminho(UsoDoIcone.App, null).Should().BeNull();
            _preferencias.Alterar(p => p with { Icones = p.Icones.ComIconeDoApp(IconesPersonalizados.TodosOsTemas, png) });
            _servico.Caminho(UsoDoIcone.App, null).Should().Be(png);
        });
    }

    sealed class AplicadorNulo : IAplicadorDeTema
    {
        public void Aplicar(DefinicaoDeTema tema, bool animar) { }
    }
}
