using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Menage.Core.Banco;

public static partial class ContextoXml
{
    [GeneratedRegex(@"<Resource\b[^>]*?name\s*=\s*""jdbc/LAVENDERE(?:WEB|PDA|INT)DS""[^>]*?/>", RegexOptions.Singleline)]
    private static partial Regex ReResource();

    /// <summary>Trechos do texto que NÃO estão dentro de comentário: pares (início, fim).</summary>
    static List<(int Ini, int Fim)> TrechosAtivos(string t)
    {
        var l = new List<(int, int)>();
        var i = 0;
        while (true)
        {
            var c = t.IndexOf("<!--", i, StringComparison.Ordinal);
            if (c < 0) { l.Add((i, t.Length)); break; }
            l.Add((i, c));
            var f = t.IndexOf("-->", c + 4, StringComparison.Ordinal);
            if (f < 0) break;
            i = f + 3;
        }

        return l;
    }

    /// <summary>Comenta cada Resource ativo das conexões LAVENDERE*DS (os já comentados ficam como estão).</summary>
    static string ComentarAtivos(string t)
    {
        var sb = new StringBuilder();
        var ultimo = 0;
        foreach (var (ini, fim) in TrechosAtivos(t))
        {
            sb.Append(t, ultimo, ini - ultimo);
            sb.Append(ReResource().Replace(t[ini..fim], m =>
                m.Value.Contains("--")
                    ? throw new InvalidOperationException("Um <Resource> ativo contém \"--\" e não pode ser comentado automaticamente. Ajuste o arquivo à mão.")
                    : $"<!-- {m.Value} -->"));
            ultimo = fim;
        }

        sb.Append(t, ultimo, t.Length - ultimo);
        return sb.ToString();
    }

    static string NomeSeguro(string s)
    {
        s = new string([.. s.Where(ch => ch is not ('<' or '>' or '\r' or '\n'))]);
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.TrimEnd('-');
    }

    /// <summary>Remove TODAS as regiões geradas (e marcadores soltos): um arquivo com regiões repetidas volta a ter uma só.</summary>
    static string RemoverRegioes(string t)
    {
        while (true)
        {
            var ini = t.IndexOf(MarcaInicio, StringComparison.Ordinal);
            if (ini < 0) break;
            var fim = t.IndexOf(MarcaFim, ini, StringComparison.Ordinal);
            t = fim < 0 ? t.Remove(ini, MarcaInicio.Length) : t.Remove(ini, fim + MarcaFim.Length - ini);
        }

        return t.Replace(MarcaFim, "");
    }

    /// <summary>
    /// Devolve o texto do context.xml com o perfil ativo descomentado. A região gerada tem cada perfil válido de <paramref name="todos"/>
    /// uma vez (só o ativo descomentado), então trocar de perfil só alterna qual está ativo. O resto do arquivo não muda.
    /// </summary>
    public static Resultado<string> Aplicar(string texto, PerfilDeBanco ativo, IEnumerable<PerfilDeBanco>? todos = null)
    {
        if (Validar(ativo) is { } erro) return Resultado.Falha<string>(erro);
        try
        {
            return Resultado.Ok(Reescrever(texto, ativo, todos ?? []));
        }
        catch (InvalidOperationException ex)
        {
            return Resultado.Falha<string>(ex.Message, null, "Nada foi gravado.");
        }
    }

    static string Reescrever(string texto, PerfilDeBanco ativo, IEnumerable<PerfilDeBanco> todos)
    {
        var nl = texto.Contains("\r\n") ? "\r\n" : "\n";
        var perfis = new List<PerfilDeBanco>();
        var achouAtivo = false;
        foreach (var p in todos)
        {
            if (string.Equals(p.Nome, ativo.Nome, StringComparison.OrdinalIgnoreCase)) { if (!achouAtivo) perfis.Add(ativo); achouAtivo = true; }
            else if (Validar(p) is null && !perfis.Exists(x => string.Equals(x.Nome, p.Nome, StringComparison.OrdinalIgnoreCase))) perfis.Add(p);
        }

        if (!achouAtivo) perfis.Add(ativo);

        texto = ComentarAtivos(RemoverRegioes(texto));
        var ctx = texto.LastIndexOf("</Context>", StringComparison.Ordinal);
        if (ctx < 0) throw new InvalidOperationException("O arquivo não tem a tag </Context>: não parece um context.xml.");
        var cabeca = texto[..ctx].TrimEnd(' ', '\t', '\r', '\n');

        var sb = new StringBuilder().Append(MarcaInicio).Append('\n');
        foreach (var p in perfis)
        {
            var eAtivo = ReferenceEquals(p, ativo);
            sb.Append(CultureInfo.InvariantCulture, $"<!-- Perfil: {NomeSeguro(p.Nome)} ({TipoDeBanco.Nome(p.Tipo)}){(eAtivo ? " - ATIVO" : "")} -->\n\n");
            foreach (var k in ChavesDeConexao.Todas)
            {
                var r = GerarResource(p, k);
                sb.Append(eAtivo ? r : $"<!-- {r} -->").Append("\n\n");
            }
        }

        sb.Append(MarcaFim);
        var novo = cabeca + nl + nl + sb.ToString().Replace("\r", "").Replace("\n", nl) + nl + nl + texto[ctx..];
        try
        {
            new XmlDocument().LoadXml(novo.TrimStart('﻿'));
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"O resultado não seria um XML válido ({ex.Message}).");
        }

        return novo;
    }
}
