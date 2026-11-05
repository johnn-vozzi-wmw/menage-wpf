namespace Menage.Core.Diff;

/// <summary>Transforma o modelo no que os painéis mostram: recolhe trechos iguais, ignora o invisível, numera e ancora.</summary>
public sealed class MontadorVista(ModeloDiff modelo, bool ignorarInvisivel, bool recolher, IReadOnlySet<int> abertos, int contexto = 3)
{
    readonly List<LinhaVista> _esq = [];
    readonly List<LinhaVista> _dir = [];
    readonly List<BlocoVista> _blocos = [];
    readonly List<int[]> _ancoras = [];
    int _adicionadas;
    int _removidas;

    public VistaDiff Montar()
    {
        var segmentos = modelo.Segmentos;
        for (var i = 0; i < segmentos.Count;)
        {
            if (!ContaComoIgual(segmentos[i]))
            {
                Mudanca(segmentos[i++]);
                continue;
            }
            // Trechos iguais vizinhos (separados só por linhas ignoradas) recolhem como um trecho só.
            var fim = i;
            while (fim + 1 < segmentos.Count && ContaComoIgual(segmentos[fim + 1])) fim++;
            Iguais(i, fim);
            i = fim + 1;
        }
        return new(_esq, _dir, _blocos, _ancoras, _adicionadas, _removidas);
    }

    public static string TextoDaDobra(int linhas) => $"        ⋯   {linhas:N0} linhas iguais   —   clique para mostrar";

    bool ContaComoIgual(Segmento s) => s.Igual || (ignorarInvisivel && s.SoInvisivel);

    void Ancorar() => _ancoras.Add([_esq.Count, _dir.Count]);

    void Iguais(int primeiro, int ultimo)
    {
        Ancorar();
        var segmentos = modelo.Segmentos;
        var total = 0;
        for (var i = primeiro; i <= ultimo; i++) total += segmentos[i].ELen;
        var inicio = primeiro == 0 ? 0 : contexto;
        var fim = ultimo == segmentos.Count - 1 ? 0 : contexto;
        var (eIni, dIni) = (segmentos[primeiro].EIni, segmentos[primeiro].DIni);
        var tudo = !recolher || abertos.Contains(dIni + inicio) || total <= inicio + fim + 2;

        var invisiveis = segmentos.Skip(primeiro).Take(ultimo - primeiro + 1).SelectMany(s => s.Pares).ToDictionary(p => p.Esq);
        for (var k = 0; k < total; k++)
        {
            if (!tudo && k == inicio)
            {
                Dobra(eIni + k, dIni + k, total - inicio - fim);
                k = total - fim - 1;
                continue;
            }
            invisiveis.TryGetValue(eIni + k, out var par);
            Linha(eIni + k, dIni + k, par);
        }
        Ancorar();
    }

    void Linha(int e, int d, Par? par)
    {
        if (par is null)
        {
            _esq.Add(new(Limpar(modelo.Esq[e]), e + 1, TipoLinha.Igual));
            _dir.Add(new(Limpar(modelo.Dir[d]), d + 1, TipoLinha.Igual));
            return;
        }
        var nota = NotasDiff.Nota(modelo, par);
        _esq.Add(new(Limpar(modelo.Esq[e]), e + 1, TipoLinha.Igual) { Trivial = par.Tipo, Nota = nota, SoDica = true });
        _dir.Add(new(Limpar(modelo.Dir[d]), d + 1, TipoLinha.Igual) { Trivial = par.Tipo, Nota = nota, SoDica = true });
    }

    void Dobra(int e, int d, int quantas)
    {
        var texto = TextoDaDobra(quantas);
        _esq.Add(new(texto, 0, TipoLinha.Dobra) { Dobra = d, Escondidas = quantas, PrimeiraEscondida = e });
        _dir.Add(new(texto, 0, TipoLinha.Dobra) { Dobra = d, Escondidas = quantas, PrimeiraEscondida = d });
    }

    void Mudanca(Segmento s)
    {
        Ancorar();
        _adicionadas += s.DLen;
        _removidas += s.ELen;
        _blocos.Add(new(_esq.Count, s.ELen, _dir.Count, s.DLen, s.SoInvisivel ? NotasDiff.Motivo(modelo, s) : null));
        for (var k = 0; k < s.ELen; k++) _esq.Add(LinhaDaMudanca(s, k, s.EIni + k, modelo.Esq, TipoLinha.Removida, esquerda: true));
        for (var k = 0; k < s.DLen; k++) _dir.Add(LinhaDaMudanca(s, k, s.DIni + k, modelo.Dir, TipoLinha.Adicionada, esquerda: false));
        Ancorar();
    }

    LinhaVista LinhaDaMudanca(Segmento s, int k, int indice, IReadOnlyList<string> linhas, TipoLinha tipo, bool esquerda)
    {
        var par = k < s.Pares.Count ? s.Pares[k] : null;
        if (par is null) return new(Limpar(linhas[indice]), indice + 1, tipo);
        if (par.Tipo == Trivial.Nao)
            return new(Limpar(linhas[indice]), indice + 1, tipo) { Palavras = esquerda ? par.PalavrasEsq : par.PalavrasDir };
        return new(Limpar(linhas[indice]), indice + 1, TipoLinha.Invisivel) { Trivial = par.Tipo, Nota = NotasDiff.Nota(modelo, par) };
    }

    /// <summary>Um \r solto no meio da linha quebraria o alinhamento: o editor o trataria como fim de linha.</summary>
    static string Limpar(string linha) => linha.Contains('\r') ? linha.Replace('\r', '␍') : linha;
}
