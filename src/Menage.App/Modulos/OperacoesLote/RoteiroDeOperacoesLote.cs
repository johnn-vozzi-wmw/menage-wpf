using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.App.Tema;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>
/// Galeria dos diálogos de lote (chamada pelo modo <c>--galeria-principal</c>): abre cada diálogo com dados de exemplo, sem
/// repositório real, e salva uma imagem por tema em <c>operacoes-lote\</c>. Nenhum botão é clicado.
/// </summary>
public sealed class RoteiroDeOperacoesLote(IServiceProvider servicos, IServicoDeTema tema)
{
    public async Task<IReadOnlyList<string>> ExecutarAsync(Window dono, string pasta)
    {
        var destino = Path.Combine(pasta, "operacoes-lote");
        Directory.CreateDirectory(destino);
        var resultado = new List<string>();
        foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
        {
            tema.Trocar(id, animar: false);
            foreach (var (nome, pedido) in Exemplos())
            {
                var janela = servicos.GetRequiredService<JanelaDeFormulario>();
                janela.Owner = dono;
                janela.Receber(pedido);
                (janela.ShowActivated, janela.ShowInTaskbar, janela.WindowStartupLocation, janela.Left, janela.Top) = (false, false, WindowStartupLocation.Manual, -12000, 0);
                janela.Show();
                await Esperar(700);
                Salvar(janela, Path.Combine(destino, $"{nome}-{id}.png"));
                janela.Close();
                resultado.Add($"ok: diálogo {nome} ({id}) capturado");
            }
        }

        return resultado;
    }

    static IEnumerable<(string Nome, PedidoDeFormulario Pedido)> Exemplos()
    {
        yield return ("commit", new("Commit", "A mesma mensagem será usada em cada projeto marcado.",
        [
            new CampoInformacao("previa", "Projetos",
            [
                "lavenderecore (feature/VENDAS-123-tela): 3 versionado(s), 1 novo(s)",
                "lavendereweb (feature/VENDAS-123-tela): 5 versionado(s), 0 novo(s)",
                "lavenderedados: ignorado - detached HEAD - faça checkout de uma branch",
            ]),
            new CampoTexto("mensagem", "Mensagem do commit", "VENDAS-123: ", VariasLinhas: true, Obrigatorio: true),
            new CampoCaixa("novos", "Incluir arquivos novos (não versionados)"),
            new CampoCaixa("push", "Fazer push em seguida", true),
        ], "Commit", Altura: 620));

        yield return ("excluir-branches", new("Excluir branches / tags", "Um nome por linha (ou separados por vírgula). Se não for branch, procura tag.",
        [
            new CampoTexto("nomes", "Nomes", "feature/VENDAS-100\nv1.0-beta", VariasLinhas: true, Obrigatorio: true),
            new CampoCaixa("local", "Excluir neste computador (local)", true),
            new CampoCaixa("remoto", "Excluir também no origin (remoto)"),
            new CampoCaixa("backup", "Criar backup (backup/<nome>-<data>) antes de apagar", true),
        ], "Ver prévia", Altura: 500));

        yield return ("limpeza", new("Limpeza de branches", "Marque o que deve ser excluído.",
        [
            new CampoLista("itens", "Branches encontradas (mescladas vêm marcadas; as só inativas, não)",
            [
                new("1", "lavenderecore: feature/VENDAS-90 - mesclada em main (Ana, 14/10/2026)", true),
                new("2", "lavenderecore: bugfix/login - mesclada em main (Bruno, 02/09/2026)", true),
                new("3", "lavendereweb: spike/relatorio - sem commits há 140 dias (Carla, 17/06/2026)", false),
            ]),
            new CampoCaixa("backup", "Fazer backup das que não estão mescladas (backup/<nome>-<data>)", true),
        ], "Excluir marcadas", Largura: 760, Altura: 500));

        yield return ("configuracoes", new("Configurações", "Valem só dentro do Menage: nenhum hook é instalado nos repositórios.",
        [
            new CampoCaixa("bloquearPush", "Proteção de branches: bloquear push", true),
            new CampoCaixa("bloquearExclusao", "Proteção de branches: bloquear exclusão", true),
            new CampoTexto("branches", "Branches protegidas (uma por linha; aceita * como curinga)", "main\nmaster\ndevelop\nrelease*", VariasLinhas: true),
            new CampoCaixa("validarCommit", "Convenção de commit: exigir tipo ou chave de ticket"),
            new CampoOpcoes("tema", "Tema", ["Escuro", "Claro"], "Escuro"),
        ], "Salvar", Altura: 640));
    }

    static void Salvar(Window janela, string arquivo)
    {
        var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var stream = File.Create(arquivo);
        png.Save(stream);
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
