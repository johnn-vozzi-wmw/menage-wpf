using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;

namespace Menage.Pocs.Diff;

/// <summary>
/// Recolhe, nos dois painéis ao mesmo tempo, os trechos iguais longos (deixa 'contexto' linhas de cada lado).
/// Como as colunas são alinhadas, a mesma faixa de linhas vale para os dois e a altura total continua igual.
/// </summary>
public sealed class RecolhimentoIguais
{
    private readonly List<(FoldingSection Esq, FoldingSection Dir, int Linhas)> _secoes = [];
    private readonly FoldingManager _esq, _dir;
    private readonly TextEditor _ed1, _ed2;

    public RecolhimentoIguais(TextEditor esquerdo, TextEditor direito)
    {
        (_ed1, _ed2) = (esquerdo, direito);
        _esq = FoldingManager.Install(esquerdo.TextArea);
        _dir = FoldingManager.Install(direito.TextArea);
    }

    public int Trechos => _secoes.Count;

    public void Criar(ModeloDiff modelo, int contexto = 3)
    {
        _esq.Clear();
        _dir.Clear();
        _secoes.Clear();
        var n = modelo.Esquerda.Count;
        for (var i = 0; i < n;)
        {
            if (modelo.Esquerda[i].Tipo != TipoLinha.Igual) { i++; continue; }
            var fim = i;
            while (fim + 1 < n && modelo.Esquerda[fim + 1].Tipo == TipoLinha.Igual) fim++;
            var primeira = i + contexto;      // índice (0-based) da 1ª linha escondida
            var ultima = fim - contexto;
            if (ultima >= primeira + 1) Adicionar(primeira, ultima);
            i = fim + 1;
        }
    }

    public void DefinirTodos(bool recolhido)
    {
        foreach (var (e, d, _) in _secoes) { e.IsFolded = recolhido; d.IsFolded = recolhido; }
    }

    /// <summary>Duplo clique numa linha recolhida alterna o trecho nos dois lados.</summary>
    public void Alternar(int linha)
    {
        foreach (var (e, d, _) in _secoes)
        {
            var l = _ed1.Document.GetLineByOffset(e.StartOffset).LineNumber;
            if (linha < l || linha > _ed1.Document.GetLineByOffset(e.EndOffset).LineNumber) continue;
            var novo = !e.IsFolded;
            e.IsFolded = novo;
            d.IsFolded = novo;
            return;
        }
    }

    private void Adicionar(int primeira, int ultima)
    {
        var linhas = ultima - primeira + 1;
        var titulo = $"  ··· {linhas} linhas iguais ···";
        var e = _esq.CreateFolding(_ed1.Document.GetLineByNumber(primeira).EndOffset, _ed1.Document.GetLineByNumber(ultima + 1).EndOffset);
        var d = _dir.CreateFolding(_ed2.Document.GetLineByNumber(primeira).EndOffset, _ed2.Document.GetLineByNumber(ultima + 1).EndOffset);
        e.Title = d.Title = titulo;
        e.IsFolded = d.IsFolded = true;
        _secoes.Add((e, d, linhas));
    }
}
