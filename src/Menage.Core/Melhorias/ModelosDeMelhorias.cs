using System.Text;
using System.Text.RegularExpressions;
using Menage.Core.BetaAutomatica;

namespace Menage.Core.Melhorias;

public sealed class BranchDaMelhoria
{
    public string Projeto { get; set; } = "";

    public string Branch { get; set; } = "";

    /// <summary>Branch/tag de onde a branch é criada quando ainda não existe.</summary>
    public string Base { get; set; } = "";
}

public sealed class Melhoria
{
    public const string Andamento = "andamento", Pausada = "pausada", Concluida = "concluida";

    public string Id { get; set; } = "";

    public string Titulo { get; set; } = "";

    public string Notas { get; set; } = "";

    public string Status { get; set; } = Andamento;

    public string CriadaEm { get; set; } = "";

    /// <summary>yyyy-MM-dd HH:mm da última ativação.</summary>
    public string UltimoUso { get; set; } = "";

    public string PerfilAtivoId { get; set; } = "";

    public List<BranchDaMelhoria> Branches { get; set; } = [];

    /// <summary>Categorias livres (urgente, cliente-lebon): minúsculas, sem acento, sem espaços.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Ids dos perfis de banco (módulo contexto-banco). Um perfil serve a várias melhorias.</summary>
    public List<string> Perfis { get; set; } = [];

    public BranchDaMelhoria? Do(string projeto) => Branches.FirstOrDefault(b => b.Projeto.Equals(projeto, StringComparison.OrdinalIgnoreCase));

    /// <summary>Perfil que será aplicado ao ativar: o escolhido por último, senão o primeiro associado que ainda existe.</summary>
    public string? PerfilParaAplicar(Func<string, bool> existe)
    {
        if (PerfilAtivoId.Length > 0 && Perfis.Contains(PerfilAtivoId) && existe(PerfilAtivoId)) return PerfilAtivoId;
        return Perfis.FirstOrDefault(existe);
    }

    /// <summary>Tag canônica: minúscula, sem acento, espaço vira hífen, sem vírgula, ponto e vírgula, aspas, dois-pontos nem #.</summary>
    public static string NormalizarTag(string? t)
    {
        t = Textos.SemAcento((t ?? "").Trim().ToLowerInvariant());
        var sb = new StringBuilder();
        foreach (var c in t)
        {
            if (c is ',' or ';' or '"' or ':' or '#') continue;
            sb.Append(char.IsWhiteSpace(c) ? '-' : c);
        }

        return Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
    }

    /// <summary>"urgente, Cliente Lebon" vira [urgente, cliente-lebon], sem repetidas.</summary>
    public static List<string> InterpretarTags(string? texto)
    {
        var l = new List<string>();
        foreach (var parte in (texto ?? "").Split(',', ';'))
            if (NormalizarTag(parte) is { Length: > 0 } n && !l.Contains(n)) l.Add(n);
        return l;
    }

    public static string NomeDoStatus(string s) => s switch { Pausada => "Pausada", Concluida => "Concluída", _ => "Em andamento" };

    /// <summary>O id aparece na mensagem do commit? VENDAS-24574 não casa com VENDAS-245741.</summary>
    public static Regex PadraoDeCommit(string id) =>
        new("(?<![0-9A-Za-z])" + Regex.Escape(id) + "(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>O que havia ANTES de ativar: branch de cada projeto que a ativação mudou, banco, version.conf e dono do sqlite. Serve ao Desvincular.</summary>
public sealed class EstadoAnterior
{
    public string Melhoria { get; set; } = "";

    public string Quando { get; set; } = "";

    public List<BranchDaMelhoria> Branches { get; set; } = [];

    public bool TinhaBanco { get; set; }

    public string BancoId { get; set; } = "";

    public string BancoNome { get; set; } = "";

    public bool GuardouVersaoConf { get; set; }

    public string VersaoConfCaminho { get; set; } = "";

    public string VersaoConfAntes { get; set; } = "";

    public bool GuardouSqlite { get; set; }

    public string SqliteDonoAntes { get; set; } = "";
}

public sealed class ConfigSqlite
{
    public bool Ativo { get; set; }

    /// <summary>Caminho COMPLETO do wmwvendas.sqlite3 (vazio = não configurado; não há padrão).</summary>
    public string Caminho { get; set; } = "";

    /// <summary>Atividade dona do wmwvendas.sqlite3 corrente.</summary>
    public string Dono { get; set; } = "";
}

public sealed class ConfigVersaoConf
{
    public bool Ativo { get; set; }

    public string Caminho { get; set; } = @"C:\wmw\ferramentas\vendas-prd\data\wmw\wmwvendas\custom\wmwvendasapp\version.conf";
}

public sealed class FiltroSalvo
{
    public string Nome { get; set; } = "";

    public string Consulta { get; set; } = "";
}

/// <summary>Tudo o que as Melhorias guardam (<c>melhorias.json</c>).</summary>
public sealed class ConfigMelhorias
{
    public const string NomeDoArquivo = "melhorias";

    public string Ativa { get; set; } = "";

    public EstadoAnterior? Anterior { get; set; }

    public ConfigVersaoConf VersaoConf { get; set; } = new();

    public ConfigSqlite Sqlite { get; set; } = new();

    /// <summary>Última busca do dashboard (volta como estava).</summary>
    public string Consulta { get; set; } = "";

    public List<Melhoria> Lista { get; set; } = [];

    public List<FiltroSalvo> Filtros { get; set; } = [];

    /// <summary>tag (minúscula) → chave da cor na paleta; sem entrada = cor automática.</summary>
    public Dictionary<string, string> CoresTags { get; set; } = [];

    public static Func<ConfigMelhorias> Padrao => () => new ConfigMelhorias();

    public Melhoria? Achar(string id) => Lista.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public Melhoria? AtivaMelhoria => Ativa.Length == 0 ? null : Achar(Ativa);

    /// <summary>Tags existentes com o número de melhorias de cada uma (mais usadas primeiro).</summary>
    public List<KeyValuePair<string, int>> TagsComContagem() =>
        [.. Lista.SelectMany(m => m.Tags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Renomeia a tag em todas as melhorias (se a nova já existe, as duas viram uma); devolve quantas melhorias mudaram.</summary>
    public int RenomearTag(string de, string para)
    {
        para = Melhoria.NormalizarTag(para);
        if (para.Length == 0) return 0;
        var n = 0;
        foreach (var m in Lista)
        {
            var i = m.Tags.FindIndex(t => t.Equals(de, StringComparison.OrdinalIgnoreCase));
            if (i < 0) continue;
            m.Tags.RemoveAt(i);
            if (!m.Tags.Contains(para)) m.Tags.Insert(i, para);
            n++;
        }

        foreach (var f in Filtros) f.Consulta = FiltroDeMelhorias.TrocarValor(f.Consulta, "tag", de, para);
        var deN = Melhoria.NormalizarTag(de);
        if (CoresTags.Remove(deN, out var cor)) CoresTags.TryAdd(para, cor);
        return n;
    }

    /// <summary>Chave vazia remove a escolha (volta à cor automática).</summary>
    public void DefinirCor(string tag, string? chave)
    {
        tag = Melhoria.NormalizarTag(tag);
        if (tag.Length == 0) return;
        if (string.IsNullOrEmpty(chave)) CoresTags.Remove(tag);
        else CoresTags[tag] = chave;
    }

    public int RemoverTag(string tag)
    {
        CoresTags.Remove(Melhoria.NormalizarTag(tag));
        return Lista.Count(m => m.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)) > 0);
    }
}
