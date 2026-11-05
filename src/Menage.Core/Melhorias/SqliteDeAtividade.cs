using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Melhorias;

public enum NivelDeLinha { Ok, Info, Aviso, Erro }

public sealed record LinhaDeResumo(NivelDeLinha Nivel, string Texto);

/// <summary>Resumo de uma ativação, desvinculação ou criação de branches (o que a tela mostra).</summary>
public sealed class ResumoDeMelhoria(string titulo)
{
    public string Titulo { get; } = titulo;

    public List<LinhaDeResumo> Linhas { get; } = [];

    public bool Parado { get; set; }

    public int Erros => Linhas.Count(l => l.Nivel == NivelDeLinha.Erro);

    public void Ok(string t) => Linhas.Add(new(NivelDeLinha.Ok, t));

    public void Info(string t) => Linhas.Add(new(NivelDeLinha.Info, t));

    public void Aviso(string t) => Linhas.Add(new(NivelDeLinha.Aviso, t));

    public void Erro(string t) => Linhas.Add(new(NivelDeLinha.Erro, t));
}

/// <summary>
/// Opção "banco sqlite por atividade": ao trocar de atividade, o wmwvendas.sqlite3 corrente vira wmwvendas-&lt;ID&gt;.sqlite3 e o da atividade
/// nova (se existir) assume o nome wmwvendas.sqlite3. NUNCA sobrescreve nem apaga; falha só avisa.
/// </summary>
public sealed partial class SqliteDeAtividade
{
    /// <summary>Chamado quando outro programa (ex.: DBeaver) está com o arquivo aberto: devolve verdadeiro para tentar de novo.</summary>
    public Func<string, Task<bool>>? AoArquivoEmUso { get; set; }

    /// <summary>Quem está com o arquivo aberto (Restart Manager). Trocável nos testes.</summary>
    public Func<string, IReadOnlyList<string>> QuemUsa { get; set; } = ArquivoEmUso.Quem;

    /// <summary>Id da atividade na branch (VENDAS-25174 de "feature/vendas-25174-xyz"); vazio para tag, detached ou branch sem id.</summary>
    public static string IdDaBranch(string? branch) =>
        IdNoNome().Match(branch ?? "") is { Success: true } m ? "VENDAS-" + m.Groups[1].Value : "";

    /// <summary>Id da melhoria como parte de nome de arquivo: VENDAS-NNN canônico; senão o texto sem caracteres inválidos.</summary>
    public static string IdDaAtividade(string? id)
    {
        var d = IdDaBranch(id);
        if (d.Length > 0) return d;
        var sb = new StringBuilder();
        var invalidos = Path.GetInvalidFileNameChars();
        foreach (var c in (id ?? "").Trim()) sb.Append(Array.IndexOf(invalidos, c) >= 0 || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    /// <summary>wmwvendas.sqlite3 vira wmwvendas-&lt;id&gt;.sqlite3 na mesma pasta.</summary>
    public static string PorId(string caminho, string id) =>
        Path.Combine(Path.GetDirectoryName(caminho)!, Path.GetFileNameWithoutExtension(caminho) + "-" + id + Path.GetExtension(caminho));

    /// <summary>Faz o wmwvendas.sqlite3 corrente pertencer à atividade <paramref name="novo"/>. Altera <c>Dono</c>; quem chama grava.</summary>
    public async Task<bool> TrocarAsync(ConfigSqlite sq, string novo, string idAntes, ResumoDeMelhoria res)
    {
        var arq = sq.Caminho.Trim();
        if (arq.Length == 0) { res.Aviso("sqlite: a opção está ligada, mas o caminho do wmwvendas.sqlite3 não foi configurado - nada foi renomeado."); return false; }
        var pasta = Path.GetDirectoryName(arq);
        if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) { res.Aviso($"sqlite: a pasta do banco não existe ({pasta}) - nada foi renomeado."); return false; }

        var existe = File.Exists(arq);
        var nome = Path.GetFileName(arq);
        if (novo.Length == 0)
        {
            res.Info($"sqlite: esta troca não tem atividade identificada (tag, detached ou branch sem VENDAS-NNN) - {nome} mantido" + (sq.Dono.Length > 0 ? $" (dono: {sq.Dono})" : "") + ".");
            return true;
        }

        var dono = sq.Dono.Length > 0 ? sq.Dono : idAntes;
        if (existe && dono.Equals(novo, StringComparison.OrdinalIgnoreCase))
        {
            sq.Dono = novo;
            res.Ok($"sqlite: {nome} já é de {novo}");
            return true;
        }

        if (existe)
        {
            if (dono.Length == 0) dono = "SEMID-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var guardado = PorId(arq, dono);
            if (File.Exists(guardado)) { res.Aviso($"sqlite: {Path.GetFileName(guardado)} já existe - não renomeei {nome} para não sobrescrever (resolva à mão e troque de novo)."); return false; }
            if (await MoverAsync(arq, guardado) is { } erro) { res.Aviso("sqlite: " + erro); return false; }
            res.Ok($"sqlite: {nome} guardado como {Path.GetFileName(guardado)}");
        }

        var doNovo = PorId(arq, novo);
        if (File.Exists(doNovo))
        {
            if (await MoverAsync(doNovo, arq) is { } erro2) { res.Aviso("sqlite: " + erro2); return false; }
            res.Ok($"sqlite: {Path.GetFileName(doNovo)} agora é {nome}");
        }
        else res.Info($"sqlite: não existe {Path.GetFileName(doNovo)} - {novo} começa sem banco (o atual será guardado com esse id na próxima troca).");

        sq.Dono = novo;
        return true;
    }

    /// <summary>
    /// Cadastro da melhoria: adota um arquivo sqlite existente, renomeando-o para wmwvendas-&lt;ID&gt;.sqlite3 na pasta do wmwvendas.sqlite3
    /// configurado. Vale mesmo com a opção desligada (pedido explícito). Nunca sobrescreve. Devolve (ficou com o nome da melhoria, mensagem).
    /// </summary>
    public async Task<(bool Ok, string Mensagem)> AdotarAsync(ConfigSqlite sq, string? origem, string idMelhoria)
    {
        origem = (origem ?? "").Trim();
        var id = IdDaAtividade(idMelhoria);
        if (origem.Length == 0) return (false, "");
        if (id.Length == 0) return (false, "sqlite: informe o id da melhoria.");
        if (!File.Exists(origem)) return (false, $"sqlite: o arquivo \"{origem}\" não existe - nada foi renomeado.");

        var corrente = sq.Caminho.Trim();
        var modelo = corrente.Length > 0 ? corrente : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(origem))!, "wmwvendas.sqlite3");
        var destino = PorId(modelo, id);
        if (Path.GetFullPath(origem).Equals(Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase)) return (true, $"sqlite: o arquivo já se chama {Path.GetFileName(destino)}.");
        if (corrente.Length > 0 && Path.GetFullPath(origem).Equals(Path.GetFullPath(corrente), StringComparison.OrdinalIgnoreCase))
            return (false, "sqlite: esse é o wmwvendas.sqlite3 em uso agora - não foi renomeado. Ele é guardado como wmwvendas-<ID>.sqlite3 sozinho na próxima troca de atividade.");
        if (File.Exists(destino)) return (false, $"sqlite: {Path.GetFileName(destino)} já existe - não renomeei para não sobrescrever.");
        if (await MoverAsync(origem, destino) is { } erro) return (false, "sqlite: " + erro);
        return (true, $"sqlite: {Path.GetFileName(origem)} agora é {Path.GetFileName(destino)}" + (corrente.Length == 0 ? " (o caminho do wmwvendas.sqlite3 ainda não está configurado no painel)" : ""));
    }

    /// <summary>Renomeia; se outro programa estiver usando, pergunta "Tentar de novo?". Nunca fecha o programa do usuário. Nulo = ok.</summary>
    async Task<string?> MoverAsync(string de, string para)
    {
        while (true)
        {
            try { File.Move(de, para); return null; }
            catch (IOException ex) when (EmUso(ex))
            {
                var quem = ArquivoEmUso.Resumo(QuemUsa(de));
                var onde = quem.Length > 0 ? $"em {quem}" : "em outro programa (ex.: DBeaver)";
                var pedido = $"O arquivo {Path.GetFileName(de)} está aberto {onde}.\n\nDesconecte ou feche esse banco (no DBeaver: botão direito na conexão e Desconectar).\n\nDepois clique em \"Tentar de novo\". Em \"Cancelar\" a troca de branch segue, mas o banco não é renomeado.";
                if (AoArquivoEmUso is { } pergunta && await pergunta(pedido)) continue;
                return $"o arquivo {Path.GetFileName(de)} está aberto {onde} - feche ou desconecte o banco e troque de novo (nada foi renomeado).";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"não foi possível renomear {Path.GetFileName(de)} para {Path.GetFileName(para)}: {ex.Message}";
            }
        }
    }

    static bool EmUso(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    [GeneratedRegex(@"VENDAS[-_ ]?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IdNoNome();
}
