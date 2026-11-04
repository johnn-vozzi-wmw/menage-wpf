using System.Globalization;
using System.Text;
using Menage.Core.Comandos;
using Menage.Core.Perfis;

namespace Menage.App.Controles;

/// <summary>Uma linha da paleta. Bloqueada, aparece apagada com o motivo e não executa.</summary>
public sealed record ItemDaPaleta(IComando Comando, string? Motivo)
{
    public bool Bloqueado => Motivo is not null;
}

/// <summary>
/// Busca da paleta Ctrl+K: sem acento, sem caixa, todas as palavras precisam casar com o texto ou o grupo.
/// Diferente de <see cref="ICatalogoDeComandos.Buscar"/>, mantém os comandos bloqueados na lista (por último),
/// porque a paleta explica por que eles não rodam.
/// </summary>
public static class BuscaDeComandos
{
    public static IReadOnlyList<ItemDaPaleta> Filtrar(IEnumerable<IComando> comandos, string? texto, PerfilDeUso perfil)
    {
        var palavras = Normalizar(texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return
        [
            .. comandos
                .Select(c => (Comando: c, Alvo: Normalizar($"{c.Texto} {c.Grupo}")))
                .Where(c => palavras.All(c.Alvo.Contains))
                .Select(c => new ItemDaPaleta(c.Comando, Motivo(c.Comando, perfil)))
                .OrderBy(i => i.Bloqueado)
                .ThenBy(i => palavras.Length > 0 && !Normalizar(i.Comando.Texto).StartsWith(palavras[0], StringComparison.Ordinal)),
        ];
    }

    public static string Normalizar(string texto)
    {
        var saida = new StringBuilder(texto.Length);
        foreach (var letra in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra) != UnicodeCategory.NonSpacingMark)
                saida.Append(char.ToLowerInvariant(letra));
        }

        return saida.ToString();
    }

    static string? Motivo(IComando comando, PerfilDeUso perfil)
    {
        if (!comando.PerfisQueLiberam.Contains(perfil))
            return $"Disponível só no perfil {string.Join(" ou ", comando.PerfisQueLiberam)}";
        return comando.PodeExecutar() ? null : "Indisponível no momento";
    }
}
