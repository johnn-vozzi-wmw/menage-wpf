namespace Menage.Core.Beta;

public sealed record ResultadoDaLimpeza(int Encontradas, int Removidas, IReadOnlyList<string> Avisos, IReadOnlyList<string> RemovidasRelativas);

/// <summary>
/// Apaga a pasta <c>target</c> do projeto e dos módulos antes do Maven (o "clean" falha com arquivo em uso, ex.: Eclipse).
/// Só apaga <c>target</c> ao lado de um <c>pom.xml</c>; nunca entra em <c>.git</c>, <c>node_modules</c>, pastas ocultas
/// nem links simbólicos. Arquivo em uso vira aviso e o build segue.
/// </summary>
public static class LimpadorDeTarget
{
    public static IReadOnlyList<string> Localizar(string raiz)
    {
        var alvos = new List<string>();
        var pilha = new Stack<string>();
        pilha.Push(raiz.TrimEnd('\\', '/'));
        while (pilha.Count > 0)
        {
            var dir = pilha.Pop();
            var temPom = File.Exists(Path.Combine(dir, "pom.xml"));
            foreach (var sub in Subpastas(dir))
            {
                var nome = Path.GetFileName(sub);
                if (nome == "target")
                {
                    if (temPom) alvos.Add(sub);
                    continue;
                }

                if (Ignorada(sub, nome)) continue;
                pilha.Push(sub);
            }
        }

        return alvos;
    }

    public static ResultadoDaLimpeza Limpar(string raiz)
    {
        var alvos = Localizar(raiz);
        var avisos = new List<string>();
        var removidas = new List<string>();
        foreach (var alvo in alvos)
        {
            var relativa = Path.GetRelativePath(raiz, alvo);
            var erro = Remover(alvo);
            if (erro is null) removidas.Add(relativa);
            else avisos.Add($"não foi possível remover {relativa}: {erro} (arquivo em uso? Eclipse aberto?)");
        }

        return new ResultadoDaLimpeza(alvos.Count, removidas.Count, avisos, removidas);
    }

    static bool Ignorada(string caminho, string nome)
    {
        if (nome == "node_modules" || nome.StartsWith('.')) return true;
        try
        {
            var atributos = File.GetAttributes(caminho);
            return (atributos & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    static string[] Subpastas(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Nulo se apagou; senão o motivo. Arquivo somente leitura não impede.</summary>
    static string? Remover(string pasta)
    {
        try
        {
            Directory.Delete(pasta, recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                foreach (var arquivo in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
                    File.SetAttributes(arquivo, FileAttributes.Normal);
                Directory.Delete(pasta, recursive: true);
                return null;
            }
            catch (Exception segunda) when (segunda is IOException or UnauthorizedAccessException)
            {
                return segunda.Message;
            }
        }
    }
}
