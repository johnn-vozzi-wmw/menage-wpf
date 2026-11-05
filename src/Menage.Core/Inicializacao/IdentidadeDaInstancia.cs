using System.Security.Cryptography;
using System.Text;

namespace Menage.Core.Inicializacao;

/// <summary>
/// Nomes do mutex e do sinal "mostrar janela". Uma instância por pasta de dados (como o Git Manager antigo, que usava a pasta de
/// configuração): quem abre o app de novo traz a janela que já está aberta; outra pasta de dados (<c>--dados</c>) convive.
/// </summary>
public static class IdentidadeDaInstancia
{
    public static string Nome(string pastaDeDados)
    {
        var normal = Path.GetFullPath(pastaDeDados).TrimEnd('\\', '/').ToLowerInvariant();
        return "Menage_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)))[..16];
    }

    public static string NomeDoMutex(string pastaDeDados) => "Local\\" + Nome(pastaDeDados);

    public static string NomeDoSinal(string pastaDeDados) => "Local\\" + Nome(pastaDeDados) + "_mostrar";
}
