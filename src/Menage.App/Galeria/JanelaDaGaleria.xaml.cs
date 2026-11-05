using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Controles;
using Menage.App.Tema;
using Menage.Core.Comandos;
using Menage.Core.Perfis;
using Wpf.Ui.Controls;

namespace Menage.App.Galeria;

public sealed record LinhaDeAmostra(string Repositorio, string Branch, string Grupo, TomDeCor Tom);

/// <summary>Tela catálogo: todos os componentes, para conferir no olho (--catalogo) ou capturar (--galeria).</summary>
public partial class JanelaDaGaleria : JanelaBase, IRecipient<TemaMudou>
{
    static readonly PapelDeCor[] _papeisDeAmostra =
    [
        PapelDeCor.Fundo, PapelDeCor.Painel, PapelDeCor.Borda, PapelDeCor.Texto, PapelDeCor.TextoFraco, PapelDeCor.Destaque, PapelDeCor.Verde,
        PapelDeCor.Amarelo, PapelDeCor.Laranja, PapelDeCor.Vermelho, PapelDeCor.Azul, PapelDeCor.GradeSel, PapelDeCor.Console,
    ];

    readonly IServicoDeTema _tema;

    public JanelaDaGaleria(
        ContextoDeJanela contexto, ICatalogoDeTemas temas, ICatalogoDeComandos comandos, IPerfilAtual perfil, IMessenger mensageiro) : base(contexto)
    {
        _tema = contexto.Tema;
        InitializeComponent();
        Menu = FabricaDeComandos.Menu(comandos.Disponiveis(perfil.Atual).Where(c => c.Grupo == "Exemplo"));
        PilulaComMenu.Menu = Menu;
        foreach (var barra in new[] { BarraLarga, BarraMedia, BarraEstreita }) Preencher(barra);
        foreach (var papel in _papeisDeAmostra) Amostras.Children.Add(Amostra(papel));

        Grade.ItemsSource = Linhas();
        Cascata.ItemsSource = Enumerable.Range(1, 8).Select(i => $"Item {i} da lista");
        EscolhaDeTema.ItemsSource = temas.Todos;
        EscolhaDeTema.SelectedItem = _tema.Atual;
        NomeDoTema.Text = _tema.Atual.Nome;
        mensageiro.Register(this);
    }

    public ContextMenu Menu { get; }

    public BarraDePilulas BarraDeTeste => BarraMedia;

    public void Receive(TemaMudou message)
    {
        NomeDoTema.Text = message.Tema.Nome;
        EscolhaDeTema.SelectedItem = message.Tema;
    }

    void AoEscolherTema(object origem, SelectionChangedEventArgs e)
    {
        if (EscolhaDeTema.SelectedItem is DefinicaoDeTema tema) _tema.Trocar(tema.Id);
    }

    static void Preencher(BarraDePilulas barra)
    {
        (string Texto, SymbolRegular Icone)[] acoes =
        [
            ("Fetch", SymbolRegular.ArrowSync24), ("Pull", SymbolRegular.ArrowDown24), ("Checkout", SymbolRegular.BranchFork24),
            ("Commit", SymbolRegular.Checkmark24), ("Push", SymbolRegular.ArrowUp24), ("Cherry-pick", SymbolRegular.ArrowRight24),
        ];
        foreach (var (texto, icone) in acoes) barra.Children.Add(new Pilula { Texto = texto, Icone = icone });
        barra.Children.Add(new Pilula { Texto = "Gerar beta", Icone = SymbolRegular.Rocket24, Tom = TomDeCor.Destaque, Preenchida = true });
    }

    static Border Amostra(PapelDeCor papel)
    {
        var amostra = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), ToolTip = papel.ToString() };
        amostra.SetResourceReference(Border.BackgroundProperty, $"Tema.{papel}");
        amostra.SetResourceReference(Border.BorderBrushProperty, "Tema.Borda");
        return amostra;
    }

    static List<LinhaDeAmostra> Linhas()
    {
        (string, TomDeCor)[] grupos = [("WEB", TomDeCor.GrupoWeb), ("FW", TomDeCor.GrupoFw), ("APP", TomDeCor.GrupoApp), ("TC", TomDeCor.GrupoTc)];
        return [.. Enumerable.Range(1, 200).Select(i => new LinhaDeAmostra($"repositorio-{i:000}", i % 3 == 0 ? "develop" : $"feature/item-{i}", grupos[i % 4].Item1, grupos[i % 4].Item2))];
    }
}
