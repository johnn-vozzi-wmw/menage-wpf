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

/// <summary>Aba Ícones (protótipo D): tema selecionado, zoom por tema, usar em todos, soltar arquivo e opções de onde aparece.</summary>
public sealed class IconesAbaDTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgd" + Guid.NewGuid().ToString("N")[..6]);

    public IconesAbaDTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    sealed class AplicadorNulo : IAplicadorDeTema
    {
        public void Aplicar(DefinicaoDeTema tema, bool animar) { }
    }

    sealed class SeletorFalso(Func<string?> resposta) : ISeletorDeImagem
    {
        public string? Escolher() => resposta();
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

    (IconesViewModel Modelo, ServicoDePreferencias Preferencias, ServicoDeIcones Servico) Criar(Func<string?>? seletor = null, ServicoDePreferencias? existentes = null)
    {
        var pasta = new PastaDeDados(_pasta);
        var preferencias = existentes ?? new ServicoDePreferencias(new Armazenamento<PreferenciasDeAparencia>(pasta, "aparencia", padrao: () => new PreferenciasDeAparencia()));
        var mensageiro = new WeakReferenceMessenger();
        var catalogo = new CatalogoDeTemas();
        var tema = new ServicoDeTema(catalogo, new Armazenamento<PreferenciasDeTema>(pasta, "tema", padrao: () => new PreferenciasDeTema("escuro")),
            new AplicadorNulo(), mensageiro, new AjusteDeDestaque());
        tema.Iniciar();
        var servico = new ServicoDeIcones(preferencias, tema, mensageiro);
        return (new IconesViewModel(preferencias, servico, catalogo, new CatalogoDeComandos(), new ConfiguracaoFalsa(), new SeletorFalso(seletor ?? (() => null))), preferencias, servico);
    }

    string Png(string nome)
    {
        var caminho = Path.Combine(_pasta, nome);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[64], 16)));
        using var fluxo = File.Create(caminho);
        png.Save(fluxo);
        return caminho;
    }

    static ChipDeTema Chip(IconesViewModel m, string id) => m.Temas.First(t => t.Id == id);

    [Fact]
    public void Zoom_e_por_tema_e_so_vale_com_icone_proprio()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar(() => Png("a.png"));
            m.TemaSelecionado = Chip(m, "claro");
            m.ZoomPercentual = 150;
            m.ZoomPercentual.Should().Be(100, "sem ícone próprio o zoom não muda");

            m.EscolherImagemCommand.Execute(null);
            m.ZoomPercentual = 150;
            m.TemaSelecionado = Chip(m, "escuro");

            m.ZoomPercentual.Should().Be(100);
            m.TemaSelecionado = Chip(m, "claro");
            m.ZoomPercentual.Should().Be(150);
            m.ZoomFator.Should().Be(1.5);
            m.ZoomTexto.Should().Be("150%");
            p.Valor.Icones.ZoomPorTema["claro"].Should().Be(150);
        });
    }

    [Fact]
    public void Zoom_e_limitado_a_50_200_e_persiste_ao_reabrir()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar(() => Png("a.png"));
            m.EscolherImagemCommand.Execute(null);
            m.ZoomPercentual = 999;
            m.ZoomPercentual.Should().Be(200);
            m.ZoomPercentual = 10;
            m.ZoomPercentual.Should().Be(50);

            var (reaberto, _, _) = Criar(existentes: p);
            reaberto.ZoomPercentual.Should().Be(50);
        });
    }

    [Fact]
    public void Usar_em_todos_copia_icone_e_zoom_e_acende_os_pontos()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar(() => Png("a.png"));
            m.TemaSelecionado = Chip(m, "claro");
            m.EscolherImagemCommand.Execute(null);
            m.ZoomPercentual = 70;
            m.UsarEmTodosCommand.Execute(null);

            foreach (var chip in m.Temas.Where(t => t.Id != IconesPersonalizados.TodosOsTemas))
            {
                chip.TemIcone.Should().BeTrue(chip.Id);
                p.Valor.Icones.ZoomDoApp(chip.Id).Should().Be(70);
            }

            Chip(m, IconesPersonalizados.TodosOsTemas).TemIcone.Should().BeFalse("o geral não é tocado");
        });
    }

    [Fact]
    public void Usar_em_todos_fica_desabilitado_sem_icone_ou_no_tema_geral()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, _, _) = Criar(() => Png("a.png"));
            m.PodeUsarEmTodos.Should().BeFalse();
            m.TemaSelecionado = Chip(m, IconesPersonalizados.TodosOsTemas);
            m.EscolherImagemCommand.Execute(null);
            m.PodeUsarEmTodos.Should().BeFalse();
            m.TemaSelecionado = Chip(m, "escuro");
            m.EscolherImagemCommand.Execute(null);
            m.PodeUsarEmTodos.Should().BeTrue();
        });
    }

    [Fact]
    public void Remover_apaga_icone_e_zoom_e_apaga_o_ponto()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar(() => Png("a.png"));
            m.EscolherImagemCommand.Execute(null);
            m.ZoomPercentual = 80;
            m.RemoverCommand.Execute(null);

            m.TemIcone.Should().BeFalse();
            m.UsaPadrao.Should().BeTrue();
            m.NomeDoArquivo.Should().Be("Ícone padrão");
            Chip(m, "escuro").TemIcone.Should().BeFalse();
            p.Valor.Icones.ZoomPorTema.Should().BeEmpty();
        });
    }

    [Fact]
    public void Arquivo_solto_vale_como_escolhido_e_arquivo_ruim_vira_mensagem()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar();
            var ruim = Path.Combine(_pasta, "x.png");
            File.WriteAllText(ruim, "nao sou imagem");
            m.ReceberArquivo(ruim);
            m.Mensagem.Should().NotBeEmpty();
            m.TemIcone.Should().BeFalse();

            var bom = Png("b.png");
            m.ReceberArquivo(bom);
            m.Mensagem.Should().BeEmpty();
            m.NomeDoArquivo.Should().Be("b.png");
            m.Imagem.Should().NotBeNull();
            p.Valor.Icones.PorTema["escuro"].Should().Be(bom);
        });
    }

    [Fact]
    public void Caixas_de_onde_aparece_gravam_nas_preferencias()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, p, _) = Criar();
            m.AplicarNoMenuEPilulas = true;
            m.AplicarEmGruposEProjetos = true;
            p.Valor.Icones.AplicarNoMenuEPilulas.Should().BeTrue();
            p.Valor.Icones.AplicarEmGruposEProjetos.Should().BeTrue();
            m.AplicarNoMenuEPilulas = false;
            p.Valor.Icones.AplicarNoMenuEPilulas.Should().BeFalse();
        });
    }

    [Fact]
    public void Servico_aplica_o_zoom_do_tema_atual_e_decodifica_uma_vez_por_largura()
    {
        WpfDeTeste.Executar(() =>
        {
            var (m, _, servico) = Criar(() => Png("a.png"));
            m.EscolherImagemCommand.Execute(null);
            servico.Zoom(UsoDoIcone.App).Should().Be(1.0);
            m.ZoomPercentual = 125;
            servico.Zoom(UsoDoIcone.App).Should().Be(1.25);
            servico.Zoom(UsoDoIcone.Grupo).Should().Be(1.0);

            var caminho = m.Caminho!;
            servico.Imagem(caminho).Should().BeSameAs(servico.Imagem(caminho));
            servico.Imagem(caminho, ServicoDeIcones.LarguraGrande).Should().BeSameAs(servico.Imagem(caminho, ServicoDeIcones.LarguraGrande));
        });
    }
}
