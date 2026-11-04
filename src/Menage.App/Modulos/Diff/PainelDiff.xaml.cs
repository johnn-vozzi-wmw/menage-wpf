using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Menage.App.Modulos.Diff.Rolagem;
using Menage.App.Modulos.Diff.Texto;
using Menage.Core;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff;

/// <summary>Diff de um arquivo, HEAD x pasta, com a navegação e as chaves. A janela troca o arquivo mostrado com <see cref="MostrarAsync"/>.</summary>
public partial class PainelDiff : UserControl
{
    readonly CoresDiff _cores;
    readonly HashSet<int> _abertos = [];
    ServicoDiff? _servico;
    string _repo = "";
    int _carga;
    ModeloDiff? _modelo;
    VistaDiff? _vista;
    int _atual = -1;

    public PainelDiff()
    {
        _cores = new(this);
        InitializeComponent();
        Esquerdo = new PainelTexto(EditorEsq, _cores);
        Direito = new PainelTexto(EditorDir, _cores);
        Rolagem = new ControladorRolagem([Esquerdo, Direito], [BarraVEsq, BarraVDir], [BarraHEsq, BarraHDir]);
        Rolagem.Moveu += AoMover;
        Rolagem.LigarRoda(Faixas);
        Rolagem.LigarRoda(ReguaArquivo);
        Esquerdo.DobraClicada += AbrirDobra;
        Direito.DobraClicada += AbrirDobra;
        ReguaArquivo.Rolou += AoRegua;
        Aviso.ToolTip = NotasDiff.ExplicacaoDoAviso;
        _cores.Mudou += () => Desenhar(manterPosicao: true);
    }

    public ScrollBar BarraVerticalEsquerda => BarraVEsq;

    public ScrollBar BarraVerticalDireita => BarraVDir;

    public ScrollBar BarraHorizontalEsquerda => BarraHEsq;

    public ScrollBar BarraHorizontalDireita => BarraHDir;

    public Regua ReguaDoArquivo => ReguaArquivo;

    public PainelTexto Esquerdo { get; }

    public PainelTexto Direito { get; }

    public ControladorRolagem Rolagem { get; }

    public ModeloDiff? Modelo => _modelo;

    public VistaDiff? Vista => _vista;

    public int MudancaAtual => _atual;

    public string TextoDaFicha => TextoFicha.Text;

    public void Configurar(ServicoDiff servico, string repo) => (_servico, _repo) = (servico, repo);

    public async Task MostrarAsync(ArquivoAlterado arquivo)
    {
        var carga = ++_carga;
        if (arquivo.Tipo == TipoAlteracao.EmConflito)
        {
            SemDiff("Arquivo em conflito: resolva pela tela de conflito (--conflito).");
            return;
        }
        var resultado = await _servico!.MontarAsync(_repo, arquivo);
        if (carga != _carga) return; // outro arquivo foi escolhido enquanto este carregava
        if (!resultado.EhOk)
        {
            SemDiff($"Não foi possível abrir o diff de {arquivo.Nome}: {resultado.Erro}");
            return;
        }
        Mostrar(arquivo, resultado.Valor);
    }

    /// <summary>No lugar do editor: arquivo binário, erro ao ler, lista vazia.</summary>
    public void SemDiff(string? texto)
    {
        Vazio.Text = texto ?? "";
        Vazio.Visibility = texto is null ? Visibility.Collapsed : Visibility.Visible;
        Grade.Visibility = texto is null ? Visibility.Visible : Visibility.Hidden;
        if (texto is null) return;
        Aviso.Visibility = Visibility.Collapsed;
        TextoFicha.Text = " ";
        Info.Text = "";
    }

    /// <summary>Atalhos do diff; a janela repassa as teclas.</summary>
    public bool Tecla(KeyEventArgs e)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (tecla == Key.F7) Navegar(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        else if (alt && tecla == Key.Down) Navegar(1);
        else if (alt && tecla == Key.Up) Navegar(-1);
        else return false;
        return true;
    }

    /// <summary>Liga e desliga as chaves por código (roteiro de teste); redesenha mantendo a posição.</summary>
    public void DefinirChaves(bool recolher, bool ignorar)
    {
        ChaveRecolher.IsChecked = recolher;
        ChaveIgnorar.IsChecked = ignorar;
        Desenhar(manterPosicao: true);
    }

    public void Navegar(int passo)
    {
        if (_vista is not { Blocos.Count: > 0 } vista || Rolagem.Mapa is not { } mapa) return;
        var h = Direito.AlturaLinha;
        var linhas = vista.Blocos.Select(b => b.DRow).ToList();
        var topo = (int)(Rolagem.Y(1) / h);
        _atual = Navegacao.Proxima(linhas, _atual, topo, (int)(Rolagem.Viewport / h), passo);
        var b = vista.Blocos[_atual];
        var (painel, linha) = b.DRows > 0 || b.ERows == 0 ? (1, b.DRow) : (0, b.ERow);
        Rolagem.IrPara(0, mapa.UDaLinha(painel, linha * h));
        Realcar(b);
        ReguaArquivo.DefinirFoco(mapa.Unificado(1, b.DRow * h) / mapa.Altura, Math.Max(1, b.DRows) * h / mapa.Altura);
        Faixas.Atual = _atual;
        AoMover();
        Ficha_Texto($"Mudança {_atual + 1} de {vista.Blocos.Count}" + (b.Motivo is null ? "" : $"   •   {b.Motivo}"), b.Motivo is not null);
    }

    void Mostrar(ArquivoAlterado arquivo, ModeloDiff modelo)
    {
        _modelo = modelo;
        _abertos.Clear();
        var (esq, dir) = (modelo.Esquerda, modelo.Direita);
        DetalheEsq.Text = arquivo.Caminho + (modelo.Novo ? "   •   não existia no HEAD" : esq is null ? "" : $"   •   {esq.NomeCodificacao} {esq.FimDominante}");
        DetalheDir.Text = modelo.Excluido ? "   •   excluído da pasta" : dir is null ? "" : $"{dir.NomeCodificacao} {dir.FimDominante}" + (modelo.Novo ? "   •   arquivo novo" : "");
        if (modelo.Motivo != SemTexto.Nao)
        {
            SemDiff(modelo.Motivo switch
            {
                SemTexto.Binario => "Arquivo binário: o conteúdo não dá para comparar como texto.",
                SemTexto.MuitoGrande => "Arquivo grande demais para comparar aqui (mais de 32 MB).",
                _ => "Sem conteúdo para comparar (submódulo ou caminho que não é um arquivo).",
            });
            return;
        }
        SemDiff(null);
        Esquerdo.Fonte = modelo.Esq;
        Direito.Fonte = modelo.Dir;
        var aviso = NotasDiff.Aviso(modelo);
        Aviso.Text = aviso;
        Aviso.Visibility = aviso.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Desenhar(manterPosicao: false);
        // Depois do layout: só então a altura da tela vale para posicionar a primeira mudança.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            AoProntaLayout();
            Navegar(1);
        });
    }

    void AoProntaLayout() => Desenhar(manterPosicao: false);

    void Desenhar(bool manterPosicao)
    {
        if (_modelo is not { Motivo: SemTexto.Nao } modelo) return;
        var linhaArquivo = manterPosicao && _vista is { } antiga ? antiga.LinhaDoArquivo((int)(Rolagem.Y(1) / Direito.AlturaLinha)) : 0;
        var x = manterPosicao ? Rolagem.X : 0;
        var vista = new MontadorVista(modelo, ChaveIgnorar.IsChecked == true, ChaveRecolher.IsChecked == true, _abertos).Montar();
        _vista = vista;
        var colunas = Math.Max(PainelTexto.Colunas(vista.Esq), PainelTexto.Colunas(vista.Dir));
        Esquerdo.Mostrar(vista.Esq, colunas);
        Direito.Mostrar(vista.Dir, colunas);
        Esquerdo.LimparRealce();
        Direito.LimparRealce();
        _atual = -1;
        Faixas.Configurar(_cores, vista.Blocos);
        var h = Direito.AlturaLinha;
        var largura = (colunas + 2) * Esquerdo.Janela.LarguraCaractere;
        MapaRolagem Criar(double tela) => new(vista.Ancoras, [vista.Esq.Count, vista.Dir.Count], h, tela);
        double Posicao(MapaRolagem m) => manterPosicao ? m.UDeY(1, vista.LinhaDoPainelDireito(linhaArquivo) * h) : 0;
        Rolagem.DefinirMapa(Criar, largura, Posicao, x);
        ReguaArquivo.Configurar(_cores, Marcas(vista, Rolagem.Mapa!, h));
        Info.Text = $"{modelo.Dir.Count:N0} linhas   •   {vista.Blocos.Count} mudança(s)   •   +{vista.Adicionadas}  −{vista.Removidas}";
        var n = vista.Blocos.Count;
        Ficha_Texto(n == 0 ? "Sem mudanças" : n == 1 ? "1 mudança" : $"{n} mudanças", false);
        AoMover();
    }

    List<MarcaRegua> Marcas(VistaDiff vista, MapaRolagem mapa, double h)
    {
        var marcas = new List<MarcaRegua>(vista.Blocos.Count);
        if (mapa.Altura <= 0) return marcas;
        foreach (var b in vista.Blocos)
        {
            var (painel, linha) = b.DRows > 0 ? (1, b.DRow) : (0, b.ERow);
            var inicio = mapa.Unificado(painel, linha * h) / mapa.Altura;
            var tamanho = Math.Max(b.ERows, b.DRows) * h / mapa.Altura;
            var (e, d) = b.Motivo is not null ? (_cores.Ambar, _cores.Ambar) : (_cores.Perigo, _cores.Sucesso);
            marcas.Add(new(inicio, tamanho, b.ERows > 0 ? e : null, b.DRows > 0 ? d : null));
        }
        return marcas;
    }

    void Realcar(BlocoVista b)
    {
        // Só inserção (ou só remoção): o lado vazio realça a linha onde o trecho entraria.
        Esquerdo.Realcar(Math.Max(1, b.ERow + (b.ERows > 0 ? 1 : 0)), b.ERow + Math.Max(1, b.ERows) + (b.ERows > 0 ? 1 : 0));
        Direito.Realcar(Math.Max(1, b.DRow + (b.DRows > 0 ? 1 : 0)), b.DRow + Math.Max(1, b.DRows) + (b.DRows > 0 ? 1 : 0));
    }

    void Ficha_Texto(string texto, bool ambar)
    {
        TextoFicha.Text = texto;
        TextoFicha.Foreground = ambar ? _cores.Pincel(_cores.Ambar) : (Brush)FindResource("TextFillColorPrimaryBrush");
    }

    void AoMover()
    {
        if (Rolagem.Mapa is not { Altura: > 0 } mapa) return;
        Faixas.Redesenhar(Rolagem.Y(0), Rolagem.Y(1), Direito.AlturaLinha);
        var tela = Math.Min(1, Rolagem.Viewport / mapa.Altura);
        ReguaArquivo.Posicionar(mapa.Maximo > 0 ? Rolagem.U / mapa.Maximo * (1 - tela) : 0, tela);
    }

    void AoRegua(double fracao, bool arrastando)
    {
        if (Rolagem.Mapa is not { } mapa) return;
        Rolagem.IrPara(Rolagem.X, (fracao * mapa.Altura) - (Rolagem.Viewport / 2), arrastando);
    }

    void AbrirDobra(LinhaVista dobra)
    {
        _abertos.Add(dobra.Dobra);
        Desenhar(manterPosicao: true);
    }

    void AoChave(object sender, RoutedEventArgs e) => Desenhar(manterPosicao: true);

    void AoAnterior(object sender, RoutedEventArgs e) => Navegar(-1);

    void AoProxima(object sender, RoutedEventArgs e) => Navegar(1);
}
