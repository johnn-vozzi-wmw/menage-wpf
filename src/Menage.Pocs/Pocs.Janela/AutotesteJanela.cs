using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Janela;

/// <summary>
/// Confere por mensagem do Windows (WM_NCHITTEST) o que o sistema enxerga em cada ponto da janela, o recuo ao
/// maximizar e, se a janela estiver em primeiro plano, o encaixe real (Win+Seta Esquerda). Só mexe na própria janela.
/// </summary>
public sealed class AutotesteJanela(Window j, string saida, string? pastaCapturas)
{
    private const uint WM_NCHITTEST = 0x0084;
    private const int GWL_STYLE = -16, WS_THICKFRAME = 0x40000, WS_MAXIMIZEBOX = 0x10000;
    private static readonly Dictionary<int, string> Nomes = new()
    {
        [1] = "HTCLIENT", [2] = "HTCAPTION", [10] = "HTLEFT", [11] = "HTRIGHT", [12] = "HTTOP", [13] = "HTTOPLEFT",
        [14] = "HTTOPRIGHT", [15] = "HTBOTTOM", [16] = "HTBOTTOMLEFT", [17] = "HTBOTTOMRIGHT"
    };

    public async Task ExecutarAsync()
    {
        var r = new Dictionary<string, object>();
        await Esperar(800);
        var hwnd = new WindowInteropHelper(j).Handle;
        Nativos.GetWindowRect(hwnd, out var rc);
        var estilo = Nativos.GetWindowLong(hwnd, GWL_STYLE);
        r["estiloTemBordaDeRedimensionar_WS_THICKFRAME"] = (estilo & WS_THICKFRAME) != 0;
        r["estiloTemMaximizar_WS_MAXIMIZEBOX"] = (estilo & WS_MAXIMIZEBOX) != 0;
        r["retanguloInicial"] = $"{rc.Esq},{rc.Topo} {rc.Dir - rc.Esq}x{rc.Base - rc.Topo}";

        int mx = (rc.Esq + rc.Dir) / 2, my = (rc.Topo + rc.Base) / 2;
        var pontos = new (string Nome, int X, int Y, int Esperado)[]
        {
            ("borda esquerda", rc.Esq + 2, my, 10), ("borda direita", rc.Dir - 3, my, 11),
            ("borda superior", mx, rc.Topo + 2, 12), ("borda inferior", mx, rc.Base - 3, 15),
            ("canto sup. esquerdo", rc.Esq + 2, rc.Topo + 2, 13), ("canto sup. direito", rc.Dir - 3, rc.Topo + 2, 14),
            ("canto inf. esquerdo", rc.Esq + 2, rc.Base - 3, 16), ("canto inf. direito", rc.Dir - 3, rc.Base - 3, 17),
            ("barra de título (arrastar)", mx, rc.Topo + 20, 2), ("botão fechar (clicável)", rc.Dir - 20, rc.Topo + 20, 1),
            ("área do conteúdo", mx, my, 1)
        };
        var hits = new List<string>();
        foreach (var p in pontos)
        {
            var lp = (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
            var got = (int)Nativos.SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, lp);
            hits.Add($"{(got == p.Esperado ? "OK " : "ERRO")} {p.Nome}: esperado {Nomes[p.Esperado]}, recebeu {(Nomes.TryGetValue(got, out var n) ? n : got.ToString())}");
        }
        r["testeDeAcertoPorPonto"] = hits;

        var cap = new Capturador();
        if (pastaCapturas is not null) cap.Salvar(j, Path.Combine(pastaCapturas, "janela-normal.png"));
        j.WindowState = WindowState.Maximized;
        await Esperar(600);
        var area = SystemParameters.WorkArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(j);
        var raiz = (FrameworkElement)((System.Windows.Controls.Border)j.Content).Child;
        var canto = raiz.PointToScreen(new Point(0, 0));
        var fim = raiz.PointToScreen(new Point(raiz.ActualWidth, raiz.ActualHeight));
        r["maximizado_conteudo_dentro_da_area_de_trabalho"] =
            canto.X >= area.Left * dpi.DpiScaleX - 1 && canto.Y >= area.Top * dpi.DpiScaleY - 1 &&
            fim.X <= area.Right * dpi.DpiScaleX + 1 && fim.Y <= area.Bottom * dpi.DpiScaleY + 1;
        r["maximizado_detalhe"] = $"conteúdo {canto.X:0},{canto.Y:0} -> {fim.X:0},{fim.Y:0}; área útil {area.Left * dpi.DpiScaleX:0},{area.Top * dpi.DpiScaleY:0} -> {area.Right * dpi.DpiScaleX:0},{area.Bottom * dpi.DpiScaleY:0}";
        if (pastaCapturas is not null) cap.Salvar(j, Path.Combine(pastaCapturas, "janela-maximizada.png"));
        j.WindowState = WindowState.Normal;
        await Esperar(500);

        j.Activate();
        await Esperar(300);
        if (Nativos.GetForegroundWindow() == hwnd)
        {
            Tecla(0x5B, 0x25); // Win+Seta Esquerda = encaixar na metade esquerda
            await Esperar(900);
            Nativos.GetWindowRect(hwnd, out var enc);
            var larguraArea = area.Width * dpi.DpiScaleX;
            r["encaixe_WinSetaEsquerda"] = $"largura {enc.Dir - enc.Esq}px de {larguraArea:0}px de área útil; ocupa a metade: {Math.Abs((enc.Dir - enc.Esq) - larguraArea / 2) < 24}";
            if (pastaCapturas is not null) cap.Salvar(j, Path.Combine(pastaCapturas, "janela-encaixada.png"));
            Tecla(0x5B, 0x28); // Win+Seta Baixo = restaura
            await Esperar(700);
        }
        else r["encaixe_WinSetaEsquerda"] = "NÃO TESTADO: a janela não estava em primeiro plano";

        File.WriteAllText(saida, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Application.Current.Shutdown();
    }

    private static void Tecla(byte a, byte b)
    {
        Nativos.keybd_event(a, 0, 0, UIntPtr.Zero);
        Nativos.keybd_event(b, 0, 0, UIntPtr.Zero);
        Nativos.keybd_event(b, 0, 2, UIntPtr.Zero);
        Nativos.keybd_event(a, 0, 2, UIntPtr.Zero);
    }

    private static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }
}
