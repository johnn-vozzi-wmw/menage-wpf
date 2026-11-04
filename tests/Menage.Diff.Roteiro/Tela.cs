using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Menage.Diff.Roteiro;

/// <summary>Mouse e tela de verdade: SetCursorPos + mouse_event e cópia dos pixels que estão no monitor.</summary>
public static partial class Tela
{
    const uint BotaoEsquerdoDesce = 0x0002;
    const uint BotaoEsquerdoSobe = 0x0004;
    const int Srccopy = 0x00CC0020;
    const uint TeclaSobe = 0x0002;

    public static void MoverMouse(Point p) => SetCursorPos((int)p.X, (int)p.Y);

    public static void Apertar() => mouse_event(BotaoEsquerdoDesce, 0, 0, 0, IntPtr.Zero);

    public static void Soltar() => mouse_event(BotaoEsquerdoSobe, 0, 0, 0, IntPtr.Zero);

    /// <summary>Aperta e solta uma tecla de verdade, com os modificadores pedidos. Só na janela em primeiro plano.</summary>
    public static void Teclar(byte tecla, params byte[] modificadores)
    {
        foreach (var m in modificadores) keybd_event(m, 0, 0, IntPtr.Zero);
        keybd_event(tecla, 0, 0, IntPtr.Zero);
        keybd_event(tecla, 0, TeclaSobe, IntPtr.Zero);
        foreach (var m in modificadores.Reverse()) keybd_event(m, 0, TeclaSobe, IntPtr.Zero);
    }

    public static bool EmPrimeiroPlano(Window janela) => GetForegroundWindow() == new WindowInteropHelper(janela).Handle;

    /// <summary>A janela sob o ponto é a nossa? Nenhum clique sai sem esta confirmação.</summary>
    public static bool PontoEstaNaJanela(Window janela, Point p)
    {
        var alvo = new WindowInteropHelper(janela).Handle;
        var achada = WindowFromPoint(new Ponto { X = (int)p.X, Y = (int)p.Y });
        return achada == alvo || GetAncestor(achada, 2) == alvo;
    }

    /// <summary>Pixels BGRA do retângulo da tela, como estão no monitor agora.</summary>
    public static BitmapSource Capturar(Int32Rect area)
    {
        var tela = GetDC(IntPtr.Zero);
        var memoria = CreateCompatibleDC(tela);
        var bitmap = CreateCompatibleBitmap(tela, area.Width, area.Height);
        var antigo = SelectObject(memoria, bitmap);
        BitBlt(memoria, 0, 0, area.Width, area.Height, tela, area.X, area.Y, Srccopy);
        SelectObject(memoria, antigo);
        var imagem = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        imagem.Freeze();
        DeleteObject(bitmap);
        DeleteDC(memoria);
        _ = ReleaseDC(IntPtr.Zero, tela);
        return imagem;
    }

    public static void Salvar(BitmapSource imagem, string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(caminho);
        codificador.Save(arquivo);
    }

    public static Int32Rect AreaDe(FrameworkElement elemento)
    {
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var canto = elemento.PointToScreen(new Point(0, 0));
        return new((int)canto.X, (int)canto.Y, (int)(elemento.ActualWidth * dpi.DpiScaleX), (int)(elemento.ActualHeight * dpi.DpiScaleY));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Ponto
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, int dados, IntPtr extra);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte tecla, byte scan, uint flags, IntPtr extra);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(Ponto p);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr janela, uint tipo);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr janela);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr janela, IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int largura, int altura);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr objeto);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr destino, int x, int y, int largura, int altura, IntPtr origem, int ox, int oy, int operacao);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr objeto);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);
}
