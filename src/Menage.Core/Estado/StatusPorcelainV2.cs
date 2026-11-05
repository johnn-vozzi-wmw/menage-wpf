namespace Menage.Core.Estado;

/// <summary>O que um único <c>git status --porcelain=v2 --branch</c> informa: branch, head, upstream, ahead/behind e contagens.</summary>
public sealed record StatusDoRepo(
    string Branch, bool Detached, string Head, bool TemUpstream, int Ahead, int Behind, int Alteracoes, int NaoVersionados);

/// <summary>
/// Lê a saída de <c>git status --porcelain=v2 --branch</c>. Troca de 4 a 7 processos por projeto (branch, status, head, rev-list,
/// verificações de rebase/cherry-pick/merge) por um só, que é o que mais pesa na leitura da grade no Windows.
/// </summary>
public static class StatusPorcelainV2
{
    public const int TamanhoDoHeadCurto = 7;

    public static StatusDoRepo Interpretar(string saida)
    {
        string branch = "", oid = "";
        var detached = false;
        int ahead = 0, behind = 0, alteracoes = 0, novos = 0;
        var temAb = false;

        foreach (var linha in saida.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = linha.TrimEnd('\r');
            if (l.StartsWith("# branch.oid ", StringComparison.Ordinal)) oid = l["# branch.oid ".Length..];
            else if (l.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var nome = l["# branch.head ".Length..];
                detached = nome == "(detached)";
                branch = detached ? "" : nome;
            }
            else if (l.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var partes = l["# branch.ab ".Length..].Split(' ');
                temAb = partes.Length == 2
                    && int.TryParse(partes[0].TrimStart('+'), out ahead)
                    && int.TryParse(partes[1].TrimStart('-'), out behind);
            }
            else if (l.Length > 0 && l[0] is '1' or '2' or 'u') alteracoes++;
            else if (l.Length > 0 && l[0] == '?') novos++;
        }

        var head = detached && oid.Length >= TamanhoDoHeadCurto && oid != "(initial)" ? oid[..TamanhoDoHeadCurto] : "";
        return new StatusDoRepo(branch, detached, head, temAb, temAb ? ahead : 0, temAb ? behind : 0, alteracoes, novos);
    }
}
