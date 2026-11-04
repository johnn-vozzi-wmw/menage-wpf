using System.Diagnostics;

namespace Menage.Pocs.Comum;

public record AmostraMemoria(double WorkingSetMb, double PrivadaMb, double ManagedMb);

public sealed class LeitorMemoria
{
    public AmostraMemoria Ler()
    {
        using var p = Process.GetCurrentProcess();
        var managed = GC.GetTotalMemory(true) / 1048576.0;
        return new(Math.Round(p.WorkingSet64 / 1048576.0, 1), Math.Round(p.PrivateMemorySize64 / 1048576.0, 1), Math.Round(managed, 1));
    }
}
