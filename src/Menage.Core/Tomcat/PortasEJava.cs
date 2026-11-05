using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Menage.Core.Processos;

namespace Menage.Core.Tomcat;

/// <summary>Portas do Windows atrás de uma interface (testes simulam portas ocupadas sem abrir nada).</summary>
public interface IPortasDoSistema
{
    /// <summary>Só lê a tabela do Windows (não abre porta nenhuma): serve para o acompanhamento da tela.</summary>
    bool Escutando(int porta);

    /// <summary>Confere de verdade (lê a tabela e tenta abrir a porta por um instante, o que pega as reservadas pelo Windows).</summary>
    bool Livre(int porta);

    /// <summary>"java.exe (PID 1234)" de quem escuta; vazio se não achou.</summary>
    Task<string> OcupanteAsync(int porta, CancellationToken cancelar = default);
}

public sealed partial class PortasDoSistema(IProcessoRunner runner) : IPortasDoSistema
{
    public bool Escutando(int porta)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(l => l.Port == porta); }
        catch (NetworkInformationException) { return false; }
    }

    public bool Livre(int porta)
    {
        try
        {
            if (Escutando(porta)) return false;
            var teste = new TcpListener(IPAddress.Any, porta);
            teste.Start();
            teste.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async Task<string> OcupanteAsync(int porta, CancellationToken cancelar = default)
    {
        var saida = await runner.ExecutarAsync(new ComandoProcesso("netstat.exe", ["-ano", "-p", "TCP"], Timeout: TimeSpan.FromSeconds(10)), cancelar);
        if (!saida.Sucesso) return "";
        foreach (var linha in saida.Texto.Split('\n'))
        {
            var m = Escuta().Match(linha);
            if (!m.Success || m.Groups[1].Value != porta.ToString(System.Globalization.CultureInfo.InvariantCulture)) continue;
            var pid = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            try { return $"{System.Diagnostics.Process.GetProcessById(pid).ProcessName}.exe (PID {pid})"; }
            catch (ArgumentException) { return $"PID {pid}"; }
        }

        return "";
    }

    [GeneratedRegex(@"^\s*TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)")]
    private static partial Regex Escuta();
}

public static class EscolhaDePortas
{
    public static int ProximaLivre(IPortasDoSistema portas, int aPartir, ISet<int> evitar)
    {
        for (var p = Math.Max(1025, aPartir); p < 65000; p++)
            if (!evitar.Contains(p) && portas.Livre(p)) return p;
        throw new InvalidOperationException($"Não achei uma porta livre a partir de {aPartir}.");
    }

    /// <summary>Mantém a porta atual se ela não briga com ninguém; senão procura a próxima livre.</summary>
    public static int Escolher(IPortasDoSistema portas, int atual, ISet<int> evitar) =>
        atual > 0 && !evitar.Contains(atual) && portas.Livre(atual) ? atual : ProximaLivre(portas, atual > 0 ? atual + 1 : 8005, evitar);

    /// <summary>Portas (HTTP, parada, AJP) já usadas pelos OUTROS ambientes: não podem se repetir.</summary>
    public static HashSet<int> DosOutros(ConfiguracaoDoTomcat configuracao, AmbienteTomcat ambiente)
    {
        var portas = new HashSet<int>();
        foreach (var o in configuracao.Ambientes.Where(a => a.Id != ambiente.Id))
        {
            portas.Add(o.Porta);
            var xml = Path.Combine(o.Pasta, "conf", "server.xml");
            if (!File.Exists(xml)) continue;
            try
            {
                var p = ServerXml.Ler(ArquivoDeTexto.Ler(xml).Texto);
                if (p.Parada > 0) portas.Add(p.Parada);
                if (p.Ajp > 0) portas.Add(p.Ajp);
            }
            catch (IOException) { /* arquivo em uso: ignora as portas extras deste ambiente */ }
        }

        return portas;
    }

    /// <summary>O mesmo Tomcat em dois ambientes se atropela: devolve o dono da pasta, se houver outro.</summary>
    public static AmbienteTomcat? DonoDaPasta(ConfiguracaoDoTomcat configuracao, AmbienteTomcat ambiente)
    {
        if (string.IsNullOrWhiteSpace(ambiente.Pasta)) return null;
        var alvo = Normalizar(ambiente.Pasta);
        return configuracao.Ambientes.FirstOrDefault(o => o.Id != ambiente.Id && Normalizar(o.Pasta) == alvo);
    }

    static string Normalizar(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return p.Trim().ToLowerInvariant(); }
    }
}
