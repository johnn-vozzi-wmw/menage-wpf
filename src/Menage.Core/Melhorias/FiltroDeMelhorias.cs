using System.Globalization;
using System.Text;
using Menage.Core.BetaAutomatica;

namespace Menage.Core.Melhorias;

/// <summary>
/// Busca inteligente das melhorias: texto livre + operadores (como GitHub/Gmail), combinados com E.
/// <c>tag:urgente</c> <c>tag:a,b</c> (qualquer uma) <c>status:pausada</c> <c>projeto:lavendereweb</c> <c>banco:oracle</c> <c>ativa:sim</c>
/// <c>usada:7</c> <c>usada:nunca</c> <c>sem:tag</c> <c>sem:banco</c> <c>-tag:x</c> (nega) <c>"frase entre aspas"</c>.
/// </summary>
public sealed class FiltroDeMelhorias
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tag"] = "tag", ["t"] = "tag", ["status"] = "status", ["s"] = "status", ["projeto"] = "projeto", ["p"] = "projeto", ["proj"] = "projeto",
        ["banco"] = "banco", ["db"] = "banco", ["ativa"] = "ativa", ["usada"] = "usada", ["sem"] = "sem",
    };

    public sealed record Termo(string Campo, string Valor, bool Nega, string Raw);

    public List<Termo> Termos { get; } = [];

    public bool Vazio => Termos.Count == 0;

    public static FiltroDeMelhorias Parse(string? q)
    {
        var f = new FiltroDeMelhorias();
        f.Termos.AddRange(Quebrar(q).Select(LerTermo));
        return f;
    }

    /// <summary>Divide por espaços respeitando aspas.</summary>
    static List<string> Quebrar(string? q)
    {
        var l = new List<string>();
        var atual = new StringBuilder();
        var aspas = false;
        foreach (var c in q ?? "")
        {
            if (c == '"') { aspas = !aspas; atual.Append(c); }
            else if (char.IsWhiteSpace(c) && !aspas)
            {
                if (atual.Length > 0) { l.Add(atual.ToString()); atual.Clear(); }
            }
            else atual.Append(c);
        }

        if (atual.Length > 0) l.Add(atual.ToString());
        return l;
    }

    static Termo LerTermo(string raw)
    {
        var s = raw;
        var nega = false;
        if (s.StartsWith('-') && s.Length > 1) { nega = true; s = s[1..]; }
        var i = s.IndexOf(':');
        if (i > 0 && i < s.Length - 1 && Aliases.TryGetValue(s[..i], out var campo))
            return new Termo(campo, s[(i + 1)..].Trim('"'), nega, raw);
        return new Termo("", (nega ? "-" : "") + s.Trim('"'), false, raw);
    }

    static string Formatar(string campo, string valor) => campo + ':' + (valor.Contains(' ') ? $"\"{valor}\"" : valor);

    public static bool TemTermo(string? q, string campo, string valor) =>
        Parse(q).Termos.Any(t => !t.Nega && t.Campo == campo && t.Valor.Equals(valor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Liga/desliga o termo campo:valor. <paramref name="exclusivo"/>: ao ligar, tira os outros do mesmo campo (ex.: status).</summary>
    public static string Alternar(string? q, string campo, string valor, bool exclusivo)
    {
        var restantes = new List<string>();
        var tinha = false;
        foreach (var raw in Quebrar(q))
        {
            var t = LerTermo(raw);
            if (!t.Nega && t.Campo == campo && t.Valor.Equals(valor, StringComparison.OrdinalIgnoreCase)) { tinha = true; continue; }
            if (exclusivo && t.Campo == campo && !t.Nega) continue;
            restantes.Add(raw);
        }

        if (!tinha) restantes.Add(Formatar(campo, valor));
        return string.Join(' ', restantes);
    }

    /// <summary>Troca um valor de um campo numa busca (usado ao renomear uma tag).</summary>
    public static string TrocarValor(string? q, string campo, string de, string para)
    {
        var raws = Quebrar(q);
        for (var i = 0; i < raws.Count; i++)
        {
            var t = LerTermo(raws[i]);
            if (t.Campo != campo) continue;
            var partes = t.Valor.Split(',');
            var mudou = false;
            for (var k = 0; k < partes.Length; k++)
                if (partes[k].Equals(de, StringComparison.OrdinalIgnoreCase)) { partes[k] = para; mudou = true; }
            if (mudou) raws[i] = (t.Nega ? "-" : "") + Formatar(campo, string.Join(',', partes));
        }

        return string.Join(' ', raws);
    }

    public static DateTime? LerData(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static bool Contem(string texto, string trecho) => Textos.SemAcento(texto).Contains(Textos.SemAcento(trecho), StringComparison.OrdinalIgnoreCase);

    static string NormalizarStatus(string v)
    {
        v = Textos.SemAcento(v.ToLowerInvariant());
        if (v.StartsWith("and", StringComparison.Ordinal) || v.StartsWith("em", StringComparison.Ordinal) || v == "ativo") return Melhoria.Andamento;
        if (v.StartsWith("pau", StringComparison.Ordinal) || v.StartsWith("par", StringComparison.Ordinal)) return Melhoria.Pausada;
        if (v.StartsWith("con", StringComparison.Ordinal) || v.StartsWith("fin", StringComparison.Ordinal) || v.StartsWith("fech", StringComparison.Ordinal)) return Melhoria.Concluida;
        return v;
    }

    static bool Combina(Termo t, Melhoria m, string idAtiva, IContextoDeBanco banco, DateTime agora)
    {
        var v = Textos.SemAcento(t.Valor.ToLowerInvariant());
        switch (t.Campo)
        {
            case "tag":
                return t.Valor.Split(',').Any(x => Melhoria.NormalizarTag(x) is { Length: > 0 } n && m.Tags.Any(g => g.Equals(n, StringComparison.OrdinalIgnoreCase)));
            case "status":
                return m.Status == NormalizarStatus(t.Valor);
            case "projeto":
                return m.Branches.Any(b => Contem(b.Projeto, t.Valor));
            case "banco":
                return m.Perfis.Select(banco.Achar).OfType<PerfilDeBancoRef>().Any(p => TipoCombina(p, v) || Contem(p.Nome, t.Valor));
            case "ativa":
                var ativa = m.Id.Equals(idAtiva, StringComparison.OrdinalIgnoreCase);
                return v is "sim" or "s" or "true" or "1" ? ativa : !ativa;
            case "usada":
                var d = LerData(m.UltimoUso);
                if (v == "nunca") return d is null;
                return int.TryParse(t.Valor, out var dias) && d is not null && (agora - d.Value).TotalDays <= dias;
            case "sem":
                return v switch { "tag" or "tags" => m.Tags.Count == 0, "banco" or "perfil" => m.Perfis.Count == 0, _ => false };
            default:
                var palheiro = $"{m.Id} {m.Titulo} {m.Notas} {string.Join(' ', m.Tags)} {string.Join(' ', m.Branches.Select(b => b.Projeto + " " + b.Branch))}";
                return t.Valor.StartsWith('-') && t.Valor.Length > 1 ? !Contem(palheiro, t.Valor[1..]) : Contem(palheiro, t.Valor);
        }
    }

    static bool TipoCombina(PerfilDeBancoRef p, string v) => p.Tipo.ToLowerInvariant() switch
    {
        "oracle" => v == "oracle",
        "postgres" or "postgresql" => v is "postgres" or "postgresql",
        _ => v is "sqlserver" or "sql" or "server" or "mssql" or "sql server",
    };

    public bool Combina(Melhoria m, string idAtiva, IContextoDeBanco banco, DateTime agora) =>
        Termos.All(t => Combina(t, m, idAtiva, banco, agora) != t.Nega);

    public List<Melhoria> Aplicar(IEnumerable<Melhoria> todas, string idAtiva, IContextoDeBanco banco)
    {
        var agora = DateTime.Now;
        return [.. todas.Where(m => Combina(m, idAtiva, banco, agora))];
    }
}
