using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Tema;
using Menage.Core.Interacao;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Galeria;

/// <summary>
/// Galeria da paridade Git (modo <c>--galeria-principal</c>): o menu de contexto da grade e a decisão de conflito do merge/rebase,
/// com dados de exemplo e janelas fora da tela. Nenhum botão é clicado.
/// </summary>
public sealed class RoteiroDeParidadeGit(IServiceProvider servicos, IServicoDeTema tema)
{
    public async Task<IReadOnlyList<string>> ExecutarAsync(JanelaPrincipal dono, string pasta)
    {
        var destino = Path.Combine(pasta, "paridade-git");
        Directory.CreateDirectory(destino);
        var resultado = new List<string>();
        foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
        {
            tema.Trocar(id, animar: false);
            var item = dono.GradeDeProjetos.Items.OfType<ItemDeProjeto>().First();
            var menu = dono.MenuDeExemplo(item);
            resultado.Add($"{(menu.Items.Count >= 14 ? "ok" : "FALHOU")}: menu de contexto ({id}) tem {menu.Items.Count} itens");
            await CapturarMenuAsync(menu, dono, Path.Combine(destino, $"menu-de-contexto-{id}.png"));

            var janela = servicos.GetRequiredService<JanelaDeDecisaoDeConflito>();
            janela.Receber(new ConflitoDoGit("Merge", item.Projeto, $"Conflito ao mesclar develop em {item.Branch}", "UU src/Pedido.java\nUU pom.xml\nAA docs/nota.md", "Ainda há arquivos em conflito. Resolva e faça git add antes de continuar.",
                "Já resolvi: continuar", "Pular este projeto", "Abortar (todos)"));
            await CapturarAsync(janela, dono, Path.Combine(destino, $"decisao-de-conflito-{id}.png"));
            resultado.Add($"ok: decisão de conflito ({id}) capturada");
        }

        return resultado;
    }

    // O menu abre de verdade, mas num ponto absoluto fora da tela (-12000), para a captura não aparecer nem tirar o foco.
    static async Task CapturarMenuAsync(ContextMenu menu, Window dono, string arquivo)
    {
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        (menu.HorizontalOffset, menu.VerticalOffset) = (-12000, 0);
        menu.StaysOpen = true;
        menu.IsOpen = true;
        await Task.Delay(700);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Salvar(menu, arquivo);
        menu.IsOpen = false;
    }

    static void Salvar(FrameworkElement elemento, string arquivo)
    {
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap((int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var stream = File.Create(arquivo);
        png.Save(stream);
    }

    static async Task CapturarAsync(Window conteudo, Window dono, string arquivo)
    {
        var janela = conteudo;
        janela.Owner = dono;
        (janela.ShowActivated, janela.ShowInTaskbar, janela.WindowStartupLocation, janela.Left, janela.Top) = (false, false, WindowStartupLocation.Manual, -12000, 0);
        janela.Show();
        await Task.Delay(700);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Salvar((FrameworkElement)VisualTreeHelper.GetChild(janela, 0), arquivo);
        janela.Content = null;
        janela.Close();
    }
}
