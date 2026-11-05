namespace Menage.Core.Atualizacao;

/// <summary>Uma instalação do Git Manager antigo encontrada em disco.</summary>
public sealed record InstalacaoAntiga(string Pasta, string? Executavel, string? Versao);

/// <summary>
/// Só LÊ: lista as pastas do Git Manager antigo (GMV*, "Git Manager") para o Sobre informar que ele convive com o Menage.
/// Nunca altera, move, fecha nem desinstala nada. O Menage guarda dados em %LOCALAPPDATA%\Menage e não usa nada dele.
/// </summary>
public sealed class DetectorDoAppAntigo(string raiz = @"C:\wmw")
{
    static readonly string[] Padroes = ["GMV*", "Git Manager*"];

    public IReadOnlyList<InstalacaoAntiga> Detectar()
    {
        try
        {
            if (!Directory.Exists(raiz)) return [];
            return Padroes.SelectMany(p => Directory.EnumerateDirectories(raiz, p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(Descrever).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static InstalacaoAntiga Descrever(string pasta)
    {
        try
        {
            var exe = Directory.EnumerateFiles(pasta, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).Contains("git", StringComparison.OrdinalIgnoreCase));
            var versao = exe is null ? null : System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion;
            return new(pasta, exe is null ? null : Path.GetFileName(exe), versao);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(pasta, null, null); }
    }
}
