using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Tema;
using Menage.Core.Diff;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Diff;

/// <summary><c>--galeria-icone-iniciais &lt;pasta&gt;</c>: captura a lista de arquivos do diff (claro e escuro) com exemplos de nomes e status. Segundo plano, dados temporários.</summary>
public static class GaleriaIconeIniciais
{
    const string Argumento = "--galeria-icone-iniciais";

    public static bool Iniciar(Application app, string[] args)
    {
        var i = Array.IndexOf(args, Argumento);
        if (i < 0 || i + 1 >= args.Length) return false;
        var saida = Path.GetFullPath(args[i + 1]);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () => app.Shutdown(await ExecutarAsync(saida)));
        return true;
    }

    static async Task<int> ExecutarAsync(string saida)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "mgii" + Guid.NewGuid().ToString("N")[..4]);
        try
        {
            Directory.CreateDirectory(saida);
            Directory.CreateDirectory(raiz);
            using var servicos = ComposicaoDoApp.Montar(new ModoDeInicio(true, null, raiz));
            servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
            var tema = servicos.GetRequiredService<IServicoDeTema>();
            tema.Iniciar();

            var lista = new ListaArquivos { Width = 360, Height = 520 };
            var janela = new Window { Content = lista, Width = 380, Height = 560, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 };
            janela.Show();
            lista.Definir(
            [
                new("src/main/java/ConfirmacaoPedidoClienteManager.java", TipoAlteracao.Modificado),
                new("src/main/java/PedidoService.java", TipoAlteracao.Modificado),
                new("src/main/java/PedidoBean.java", TipoAlteracao.Adicionado),
                new("src/main/java/RegraPendenciaMargemRentabilidade.java", TipoAlteracao.Excluido),
                new("res/Messages_en_US.properties", TipoAlteracao.Modificado),
                new("res/Messages_pt_BR.properties", TipoAlteracao.Modificado),
                new("pom.xml", TipoAlteracao.Renomeado, "pom-old.xml"),
                new("scripts/atualiza_pedido.sql", TipoAlteracao.Novo),
                new("web/app.js", TipoAlteracao.EmConflito),
                new("README", TipoAlteracao.Modificado),
            ]);
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
            {
                tema.Trocar(id, animar: false);
                await Task.Delay(500);
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
                var dpi = VisualTreeHelper.GetDpi(elemento);
                var imagem = new RenderTargetBitmap((int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                imagem.Render(elemento);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(imagem));
                using var arquivo = File.Create(Path.Combine(saida, $"icone-iniciais-{id}.png"));
                png.Save(arquivo);
            }
            janela.Close();
            return 0;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(saida, "erro.txt"), ex.ToString());
            return 1;
        }
        finally
        {
            try { Directory.Delete(raiz, true); } catch (IOException) { }
        }
    }
}
