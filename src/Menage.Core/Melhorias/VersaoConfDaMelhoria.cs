using System.Text;
using Menage.Core.BetaAutomatica;
using Menage.Core.ModoBeta;

namespace Menage.Core.Melhorias;

/// <summary>
/// Opção "gravar a versão do pom.xml do projeto principal no version.conf" (desligada por padrão). O valor vai exatamente como está
/// no pom (ex.: <c>9.060-SNAPSHOT</c>). O conteúdo de antes é guardado para o Desvincular restaurar.
/// </summary>
public static class VersaoConfDaMelhoria
{
    /// <summary>Projeto principal que manda na versão: o app (lavenderepda) e, sem ele, dados, web e core.</summary>
    public static readonly string[] Principais = ["lavenderepda", "lavenderedados", "lavendereweb", "lavenderecore"];

    public static string? NomeDoPrincipal(IEnumerable<string> projetos) =>
        Principais.FirstOrDefault(p => projetos.Contains(p, StringComparer.OrdinalIgnoreCase));

    public static string? LerVersao(string pastaDoProjeto)
    {
        var pom = Path.Combine(pastaDoProjeto, "pom.xml");
        return File.Exists(pom) ? VerificadorDeBranches.VersaoDoPom(LeitorDePom.Ler(pom)) : null;
    }

    /// <summary>Grava a versão no arquivo. Guarda o "antes" em <paramref name="anterior"/> (só na primeira vez).</summary>
    public static void Gravar(ConfigVersaoConf conf, string projeto, string pastaDoProjeto, EstadoAnterior? anterior, ResumoDeMelhoria res)
    {
        var arq = conf.Caminho;
        var v = LerVersao(pastaDoProjeto);
        if (v is null) { res.Erro($"version.conf: não achei a versão no pom.xml de {projeto} ({Path.Combine(pastaDoProjeto, "pom.xml")})."); return; }
        try
        {
            var antes = File.Exists(arq) ? File.ReadAllText(arq) : null;
            if (antes is null && !Directory.Exists(Path.GetDirectoryName(arq))) { res.Erro($"version.conf: a pasta do arquivo não existe ({Path.GetDirectoryName(arq)})."); return; }
            if (antes is not null && antes.Trim() == v) { res.Ok($"version.conf: já está em {v} (de {projeto})"); return; }
            if (anterior is { GuardouVersaoConf: false })
            {
                anterior.GuardouVersaoConf = true;
                anterior.VersaoConfCaminho = arq;
                anterior.VersaoConfAntes = antes ?? "";
            }

            var fim = antes is not null && antes.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : antes is not null && antes.EndsWith('\n') ? "\n" : "";
            File.WriteAllText(arq, v + fim, new UTF8Encoding(false));
            res.Ok("version.conf: " + (antes is null ? "criado" : (antes.Trim().Length > 0 ? antes.Trim() : "(vazio)") + " ->") + $" {v} (pom.xml de {projeto})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            res.Erro($"version.conf: não foi possível gravar \"{arq}\": {ex.Message}");
        }
    }
}
