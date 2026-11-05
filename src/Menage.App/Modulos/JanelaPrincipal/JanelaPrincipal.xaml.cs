using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Saida;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// Janela principal: só liga controles ao ViewModel (pílulas do catálogo, atalhos, animação da barra, divisória da Saída).
/// Nenhuma regra de negócio mora aqui.
/// </summary>
public partial class JanelaPrincipal : JanelaBase
{
    const double LarguraDoTrilho = 56;
    const double LarguraDaBarra = 248;
    const double LarguraMinimaParaPasta = 1180;

    readonly JanelaPrincipalViewModel _modelo;
    readonly PedidosDaJanela _pedidos;
    readonly IServicoDeJanelas _janelas;
    readonly Tema.IServicoDeTema _tema;
    readonly Menage.Core.CherryPick.ServicoDaFila _fila;
    readonly AcoesDoProjeto _doProjeto;
    readonly PreferenciasDaJanelaServico _preferencias;

    public JanelaPrincipal(
        ContextoDeJanela contexto, JanelaPrincipalViewModel modelo, AcoesGit acoes, PedidosDaJanela pedidos,
        DialogosComResumoNaSaida dialogos, IServicoDeJanelas janelas, Menage.Core.CherryPick.ServicoDaFila fila,
        ICatalogoDeComandos catalogo, Menage.Core.Perfis.IPerfilAtual perfil, AcoesDoProjeto doProjeto, PreferenciasDaJanelaServico preferencias) : base(contexto)
    {
        (_modelo, _pedidos, _janelas, _tema, _fila, _doProjeto, _preferencias) = (modelo, pedidos, janelas, contexto.Tema, fila, doProjeto, preferencias);
        InitializeComponent();
        DataContext = modelo;
        Animador = new AnimadorBarraLateral(Hospedeiro, BarraLateralVisual, Centro, ColunaLateral, LarguraDoTrilho, LarguraDaBarra);
        if (modelo.BarraLateral.Recolhida) Animador.DefinirSemAnimar(false);

        MontarPilulas();
        Ligar(acoes, dialogos);
        // Ctrl+B, Ctrl+F, F5 e Ctrl+A vêm do catálogo (comandos janela.*), por ServicoDeAtalhos: o usuário pode trocá-los.
        // Beta automática e Melhorias: atalhos do Git Manager antigo (o Ctrl+B daqui recolhe a barra, então o painel é Ctrl+Shift+B).
        foreach (var (tecla, modificadores, id) in new[] { (Key.B, ModifierKeys.Control | ModifierKeys.Shift, "beta-auto.painel"), (Key.T, ModifierKeys.Control | ModifierKeys.Shift, "beta-auto.console"), (Key.M, ModifierKeys.Control, "melhorias.painel") })
            InputBindings.Add(new KeyBinding(new RelayCommand(() => ExecutarSePermitido(catalogo, perfil, id)), tecla, modificadores));
        if (perfil.Atual == Menage.Core.Perfis.PerfilDeUso.Desenvolvedor)
        {
            InputBindings.Add(new KeyBinding(new RelayCommand(() => _janelas.Abrir(Diario.JanelaDoDiario.Id)), Key.J, ModifierKeys.Control));
            CartaoDiario.Visibility = Visibility.Visible;
        }

        Loaded += AoCarregar;
        Activated += (_, _) => _ = _modelo.Grade.AtualizarAoFocarAsync();
    }

    static void ExecutarSePermitido(ICatalogoDeComandos catalogo, Menage.Core.Perfis.IPerfilAtual perfil, string id)
    {
        if (catalogo.Obter(id) is { } c && c.PerfisQueLiberam.Contains(perfil.Atual) && c.PodeExecutar()) _ = c.ExecutarAsync();
    }

    public AnimadorBarraLateral Animador { get; }

    public BarraDePilulas BarraDeAcoes => BarraDePilulas;

    public double ColunaDaBarra => ColunaLateral.ActualWidth;

    public GradePadrao GradeDeProjetos => Grade;

    public void AlternarBarra()
    {
        if (Animador.Animando) return;
        _modelo.BarraLateral.Alternar();
        Animador.Alternar();
    }

    void AoCarregar(object sender, RoutedEventArgs e)
    {
        PrepararLinhasDaGrade();
        AplicarAlturas();
        _ = _modelo.Grade.LerAsync();
        AvisarFilaPendente();
    }

    void Ligar(AcoesGit acoes, DialogosComResumoNaSaida dialogos)
    {
        _pedidos.Pedido += AoPedido;
        acoes.Aviso += (titulo, texto, grave) => Dispatcher.InvokeAsync(() => Notificar(titulo, texto, grave));
        dialogos.ResumoPronto += resumo => Dispatcher.InvokeAsync(() =>
            Notificar(resumo.Operacao, FormatadorDeResumo.Totais(resumo), resumo.Erros > 0, resumo.Avisos > 0 || resumo.Interrompido));
        _modelo.BarraLateral.Falhou += texto => Notificar("Banco de dados", texto, true);
        _modelo.Saida.Copiar += texto =>
        {
            try { Clipboard.SetText(texto); } catch (System.Runtime.InteropServices.ExternalException) { Notificar("Copiar", "A área de transferência está ocupada.", true); }
        };
        _modelo.Saida.Rolar += () => Dispatcher.BeginInvoke(() =>
        {
            if (ListaDaSaida.Items.Count > 0) ListaDaSaida.ScrollIntoView(ListaDaSaida.Items[^1]);
        });
        _modelo.Saida.PropertyChanged += AoMudarSaida;
        AreaDeGrade.SizeChanged += (_, e) => _modelo.Saida.AlturaDisponivel = e.NewSize.Height;
        SizeChanged += (_, _) => ColunaPasta.Visibility = ActualWidth >= LarguraMinimaParaPasta ? Visibility.Visible : Visibility.Collapsed;
    }

    // Fila de cherry-pick de uma sessão anterior: avisa ao abrir para o usuário retomar pelo menu Mais.
    void AvisarFilaPendente()
    {
        if (!_fila.TemFilaPendente) return;
        Notificar("Fila de cherry-pick", $"Há uma fila pendente ({_fila.Fila.Pendentes} commit(s)). Abra \"Fila de cherry-pick\" no menu Mais para retomar.", false, aviso: true);
    }

    void Notificar(string titulo, string texto, bool grave, bool aviso = false)
    {
        _modelo.Informar($"{titulo}: {texto}");
        Avisar(titulo, texto, grave ? ControlAppearance.Danger : aviso ? ControlAppearance.Caution : ControlAppearance.Success);
    }

    void AoGerenciarBanco(object sender, RoutedEventArgs e) => _janelas.Abrir(Banco.JanelaDoBanco.Id);

    void AoClicarDiario(object sender, MouseButtonEventArgs e) => _janelas.Abrir(Diario.JanelaDoDiario.Id);

    void AoClicarLogo(object sender, MouseButtonEventArgs e) => AlternarBarra();

    void AoClicarModoBeta(object sender, MouseButtonEventArgs e) => _modelo.BarraLateral.ModoBeta = !_modelo.BarraLateral.ModoBeta;

    void AoClicarFetch(object sender, MouseButtonEventArgs e) => _modelo.FetchAutomatico = !_modelo.FetchAutomatico;

    void AoClicarBancoTrilho(object sender, MouseButtonEventArgs e) => AoGerenciarBanco(sender, e);

    void AoPedido(string id)
    {
        switch (id)
        {
            case "janela.atualizar": _ = _modelo.Grade.AtualizarAsync(); break;
            case "janela.buscar-projetos": CampoDeBusca.Focus(); CampoDeBusca.SelectAll(); break;
            case "janela.barra-lateral": AlternarBarra(); break;
            case "janela.saida": _modelo.Saida.Alternar(); break;
            case "janela.marcar-todos": _modelo.Grade.MarcarVisiveis(true); break;
            case "janela.limpar-selecao": _modelo.BarraLateral.LimparSelecaoCommand.Execute(null); break;
            case "projetos.gerenciar": _janelas.Abrir(JanelaDeProjetos.Id); break;
        }
    }

    // ---- pílulas do catálogo ----

    void MontarPilulas()
    {
        foreach (var acao in _modelo.Acoes.Acoes) _pilulas[acao.Definicao.Texto] = CriarPilula(acao);
        LugarDoBeta.Content = CriarPilula(_modelo.Acoes.AcaoBeta);
        MostrarPilulas();
    }

    Pilula CriarPilula(AcaoDaBarra acao)
    {
        var d = acao.Definicao;
        var pilula = new Pilula { Texto = d.Texto, Icone = FabricaDeComandos.Icone(d.Icone), Tom = d.Tom, Preenchida = d.Destaque };
        ToolTipService.SetShowOnDisabled(pilula, true);
        if (d.Texto != _modelo.Acoes.AcaoBeta.Definicao.Texto) LigarMenuDaPilula(d.Texto, pilula);
        if (acao.TemMenu) pilula.Command = new RelayCommand(() => { });
        else pilula.Command = new AsyncRelayCommand(async () => { if (acao.Principal is { } c) await c.ExecutarAsync(); });

        void Atualizar()
        {
            pilula.IsEnabled = acao.Habilitada;
            pilula.ToolTip = acao.Motivo ?? d.Texto;
            if (acao.TemMenu) pilula.Menu = CriarMenu(acao.Menu);
        }

        acao.PropertyChanged += (_, _) => Atualizar();
        Atualizar();
        return pilula;
    }

    static System.Windows.Controls.ContextMenu CriarMenu(IReadOnlyList<ItemDeMenu> itens)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var item in itens)
        {
            var comando = item.Comando;
            var entrada = new Wpf.Ui.Controls.MenuItem
            {
                Header = comando.Texto,
                Icon = new SymbolIcon(FabricaDeComandos.Icone(comando.Icone)),
                InputGestureText = comando.Atalho ?? string.Empty,
                IsEnabled = item.Habilitado,
                ToolTip = item.Motivo,
                Command = new AsyncRelayCommand(() => comando.ExecutarAsync()),
            };
            ToolTipService.SetShowOnDisabled(entrada, true);
            menu.Items.Add(entrada);
        }

        return menu;
    }

    // ---- barra lateral ----

    void AoAlternarBarra(object sender, RoutedEventArgs e) => AlternarBarra();

    void AoClicarGrupo(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ItemDeGrupo grupo)
            _modelo.BarraLateral.AlternarGrupo(grupo, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    void AoClicarConfiguracoes(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = CartaoConfiguracoes, Placement = PlacementMode.Top };
        menu.Items.Add(Entrada("Configurações...", "Settings", () => _janelas.Abrir(Configuracoes.JanelaDeConfiguracoes.Id)));
        menu.Items.Add(Entrada("Perfis de uso...", "People", () => _janelas.Abrir(Perfis.JanelaDePerfis.Id)));
        menu.Items.Add(Entrada("Gerenciar projetos...", "FolderAdd", () => _janelas.Abrir(JanelaDeProjetos.Id)));
        menu.Items.Add(Entrada("Personalização (tema, fonte, atalhos)...", "Color", () => _janelas.Abrir("personalizacao.abrir")));
        menu.Items.Add(Entrada("Alternar tema claro/escuro (Ctrl+T)", "WeatherMoon", () => _tema.AlternarPar()));
        menu.IsOpen = true;
    }

    static Wpf.Ui.Controls.MenuItem Entrada(string texto, string icone, Action acao) => new()
    {
        Header = texto,
        Icon = new SymbolIcon(FabricaDeComandos.Icone(icone)),
        Command = new RelayCommand(acao),
    };

    // ---- centro ----

    void AoClicarBuscaDeComandos(object sender, MouseButtonEventArgs e)
    {
        AbrirPaleta();
        e.Handled = true;
    }

    void AoMarcarTodos(object sender, RoutedEventArgs e)
    {
        _modelo.Grade.MarcarVisiveis(_modelo.Grade.TodosMarcados != true);
        ((CheckBox)sender).GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    // Clique na linha marca ou desmarca; a caixa cuida de si mesma.
    void AoClicarNaGrade(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origem || Ancestral<CheckBox>(origem) is not null || Ancestral<DataGridColumnHeader>(origem) is not null) return;
        if (Ancestral<DataGridRow>(origem) is { Item: ItemDeProjeto item })
        {
            item.Marcado = !item.Marcado;
            e.Handled = true;
        }
    }

    static T? Ancestral<T>(DependencyObject origem) where T : DependencyObject
    {
        for (var atual = origem; atual is not null; atual = atual is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(atual) : LogicalTreeHelper.GetParent(atual))
            if (atual is T achado) return achado;
        return null;
    }

    // As cores de linha entram por cima do estilo da grade do design system, sem copiar o template dele.
    void PrepararLinhasDaGrade()
    {
        var estilo = new Style(typeof(DataGridRow), Grade.RowStyle);
        estilo.Triggers.Add(Gatilho(nameof(ItemDeProjeto.TemAlteracoes), "Fundo.Alterado"));
        estilo.Triggers.Add(Gatilho(nameof(ItemDeProjeto.Marcado), "Tema.GradeSel"));
        Grade.RowStyle = estilo;
    }

    static DataTrigger Gatilho(string propriedade, string recurso)
    {
        var gatilho = new DataTrigger { Binding = new System.Windows.Data.Binding(propriedade), Value = true };
        gatilho.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension(recurso)));
        return gatilho;
    }

    // ---- Saída ----

    void AoClicarCabecalhoDaSaida(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origem && Ancestral<System.Windows.Controls.Button>(origem) is not null) return;
        _modelo.Saida.Alternar();
    }

    void AoAlternarSaida(object sender, RoutedEventArgs e) => _modelo.Saida.Alternar();

    void AoArrastarDivisoria(object sender, DragDeltaEventArgs e) => _modelo.Saida.Arrastar(e.VerticalChange);

    void AoSoltarDivisoria(object sender, DragCompletedEventArgs e) => _modelo.Saida.ConcluirArrasto();

    void AoMudarSaida(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SaidaViewModel.AlturaDaGrade) or nameof(SaidaViewModel.AlturaDaSaida) or nameof(SaidaViewModel.Recolhida)) AplicarAlturas();
    }

    void AplicarAlturas()
    {
        var saida = _modelo.Saida;
        if (saida.AlturaDisponivel <= 0) return;
        LinhaDaGrade.Height = new GridLength(saida.AlturaDaGrade);
        LinhaDaSaida.Height = new GridLength(saida.AlturaDaSaida);
        ListaDaSaida.Visibility = saida.Recolhida ? Visibility.Collapsed : Visibility.Visible;
        BotaoSaida.Content = saida.Recolhida ? "Abrir" : "Recolher";
    }
}
