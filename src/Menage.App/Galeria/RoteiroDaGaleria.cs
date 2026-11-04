using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Tema;
using Menage.Core.Interacao;

namespace Menage.App.Galeria;

/// <summary>
/// Modo <c>--galeria &lt;pasta&gt;</c>: passa por todos os temas e estados, salva as capturas e grava em
/// <c>verificacoes.txt</c> o que foi exercitado. Teclas entram como eventos roteados, sem teclado real.
/// </summary>
public sealed class RoteiroDaGaleria(IServicoDeTema tema, ICatalogoDeTemas temas, IServicoDeDialogos dialogos)
{
    readonly List<string> _verificacoes = [];
    string _pasta = string.Empty;
    JanelaDaGaleria _janela = null!;

    public async Task<int> ExecutarAsync(JanelaDaGaleria janela, string pasta)
    {
        (_janela, _pasta) = (janela, pasta);
        Directory.CreateDirectory(pasta);
        await Esperar(900);
        try
        {
            await CapturarTemasAsync();
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarEstadosAsync(id);
            await VerificarTransicaoAsync();
            await VerificarBarraAsync();
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    async Task CapturarTemasAsync()
    {
        foreach (var item in temas.Todos)
        {
            tema.Trocar(item.Id, animar: false);
            await Esperar(150);
            Salvar(Raiz(), $"tema-{item.Id}");
            var console = (SolidColorBrush)_janela.FindResource("Tema.Console");
            Conferir($"Saída preta em {item.Id}", console.Color == Colors.Black);
        }
    }

    async Task CapturarEstadosAsync(string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(150);
        await PaletaAsync(id);
        await DialogosAsync(id);
        await MenuAsync(id);
    }

    async Task PaletaAsync(string id)
    {
        var paleta = _janela.Paleta!;
        var modelo = (PaletaDeComandosViewModel)paleta.DataContext;
        Tecla(ModifierKeys.Control, Key.K);
        await Esperar(350);
        Conferir("Ctrl+K abre a paleta", paleta.Aberta);
        Salvar(Raiz(), $"estado-paleta-{id}");

        modelo.Busca = "sincronizacao projetos";
        await Esperar(100);
        Conferir("busca sem acento e por vários termos", modelo.Itens.Count == 1 && modelo.Itens[0].Comando.Id == "exemplo.sincronizar");

        modelo.Busca = "exemplo";
        await Esperar(150);
        Conferir("item bloqueado por perfil aparece com motivo", modelo.Itens.Any(i => i.Comando.Id == "exemplo.diario" && i.Bloqueado));
        Salvar(Raiz(), $"estado-paleta-bloqueados-{id}");

        modelo.Busca = "zzzz";
        await Esperar(100);
        Conferir("busca sem resultado", modelo.Itens.Count == 0);

        Esc();
        Conferir("Esc fecha a paleta", !paleta.Aberta);
        _janela.AbrirPaleta();
        _janela.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = _janela });
        Conferir("clique fora fecha a paleta", !paleta.Aberta);
    }

    async Task DialogosAsync(string id)
    {
        var previa = new Previa("Excluir 3 branches?", "As branches abaixo serão apagadas no remoto.", ["feature/a", "feature/b", "feature/c"]);
        var confirmar = dialogos.ConfirmarAsync(previa);
        await Esperar(450);
        var botao = Visuais.Descendentes(_janela.DialogoAberto!).OfType<Button>().FirstOrDefault(b => b.IsDefault);
        Conferir($"foco no botão padrão ({botao?.Content})", botao?.IsKeyboardFocused == true || botao?.IsFocused == true);
        Salvar(Raiz(), $"estado-dialogo-pergunta-{id}");
        Esc();
        Conferir("Esc cancela o diálogo", !await confirmar);

        var erro = dialogos.MostrarErroAsync(new Core.Erro("Push recusado", "O remoto tem commits que você não tem.", "Faça pull e tente de novo."));
        await Esperar(450);
        Salvar(Raiz(), $"estado-dialogo-erro-{id}");
        Esc();
        await erro;

        var informar = dialogos.InformarAsync("Tudo certo", "Os 12 projetos estão atualizados.");
        await Esperar(450);
        Salvar(Raiz(), $"estado-dialogo-info-{id}");
        Esc();
        await informar;
    }

    async Task MenuAsync(string id)
    {
        _janela.Menu.PlacementTarget = _janela;
        _janela.Menu.IsOpen = true;
        await Esperar(350);
        Salvar(_janela.Menu, $"estado-menu-{id}");
        _janela.Menu.IsOpen = false;
    }

    async Task VerificarTransicaoAsync()
    {
        var camada = Visuais.Descendentes(_janela).OfType<Image>().First(i => i.Name == "PART_Transicao");
        tema.Trocar("azul-escuro");
        Conferir("troca de tema cobre a janela com a imagem anterior", camada.Visibility == Visibility.Visible);
        await Esperar(120);
        Salvar(Raiz(), "estado-transicao-meio");
        await Esperar(450);
        Conferir("a imagem some depois da dissolução", camada.Visibility == Visibility.Collapsed);

        var antes = tema.Atual;
        Tecla(ModifierKeys.Control, Key.T);
        Conferir("Ctrl+T vai para o par", tema.Atual.Id == antes.Par);
        await Esperar(450);
    }

    async Task VerificarBarraAsync()
    {
        var barra = _janela.BarraDeTeste;
        Conferir($"barra de 330 px sem texto (modo {barra.Modo})", barra.Modo == ModoDaBarra.SoIcone);
        var (passes, translacao) = (0, 0.0);
        void AoAtualizar(object? o, EventArgs e) => passes++;
        void AoDesenhar(object? o, EventArgs e)
        {
            if (barra.Children[barra.Children.Count - 1].RenderTransform is TranslateTransform t) translacao = Math.Max(translacao, Math.Abs(t.X) + Math.Abs(t.Y));
        }

        barra.Width = 170;
        barra.LayoutUpdated += AoAtualizar;
        CompositionTarget.Rendering += AoDesenhar;
        await Esperar(400);
        barra.LayoutUpdated -= AoAtualizar;
        CompositionTarget.Rendering -= AoDesenhar;
        Conferir($"mudar de modo desliza e gasta poucos passes de layout ({passes} passes, deslocamento {translacao:0} px)", translacao > 0 && passes <= 4);
    }

    void Tecla(ModifierKeys modificador, Key tecla)
    {
        var atalho = _janela.InputBindings.OfType<KeyBinding>().First(b => b.Key == tecla && b.Modifiers == modificador);
        atalho.Command.Execute(null);
    }

    void Esc()
    {
        var fonte = PresentationSource.FromVisual(_janela)!;
        _janela.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, fonte, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    FrameworkElement Raiz() => (FrameworkElement)VisualTreeHelper.GetChild(_janela, 0);

    void Salvar(FrameworkElement elemento, string nome)
    {
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(Path.Combine(_pasta, nome + ".png"));
        png.Save(arquivo);
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
