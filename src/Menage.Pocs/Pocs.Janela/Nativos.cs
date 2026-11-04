using System.Runtime.InteropServices;

namespace Menage.Pocs.Janela;

/// <summary>Chamadas Win32 do autoteste (sem estado).</summary>
internal static class Nativos
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Retangulo { public int Esq, Topo, Dir, Base; }

    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Retangulo r);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int indice);
}
