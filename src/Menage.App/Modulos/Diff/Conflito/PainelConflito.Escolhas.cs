using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using Menage.App.Modulos.Diff.Texto;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Conflito;

/// <summary>Trecho do resultado que veio de uma escolha; acompanha as edições, para "Desfazer escolha".</summary>
public sealed class TrechoEscolhido : TextSegment
{
    public int Numero { get; init; }
}

/// <summary>Põe ou tira a marca de um trecho junto com o texto, na mesma entrada da pilha: Ctrl+Z e Ctrl+Y a mantêm certa.</summary>
sealed class OperacaoDeTrecho(TextSegmentCollection<TrechoEscolhido> trechos, TrechoEscolhido trecho, bool adicionar) : IUndoableOperation
{
    public void Undo() => Aplicar(!adicionar);

    public void Redo() => Aplicar(adicionar);

    void Aplicar(bool presente)
    {
        if (presente) trechos.Add(trecho);
        else trechos.Remove(trecho);
    }
}

public partial class PainelConflito
{
    /// <summary>Escolhe o que fica no conflito <paramref name="numero"/>. Um Ctrl+Z desfaz a escolha inteira.</summary>
    public async void Usar(int numero, EscolhaConflito escolha)
    {
        var doc = Resultado.Editor.Document;
        if (_arquivo is not { } arquivo || !JuncaoConflito.Pendentes(LinhasDoResultado()).TryGetValue(numero, out var indice)) return;
        var linhas = arquivo.Juncao.Conflitos.First(c => c.Numero == numero).Linhas(escolha);
        var linha = doc.GetLineByNumber(indice + 1);
        var texto = string.Join('\n', linhas);
        var (inicio, fim) = (linha.Offset, linha.EndOffset);
        if (linhas.Count == 0)
        {
            // Sem linhas para manter, some a linha de aviso inteira (com a quebra; na última linha, a quebra de antes).
            if (linha.DelimiterLength > 0) fim += linha.DelimiterLength;
            else if (inicio > 0) inicio--;
        }
        using (doc.RunUpdate())
        {
            doc.Replace(inicio, fim - inicio, texto);
            if (texto.Length > 0) Marcar(new TrechoEscolhido { Numero = numero, StartOffset = inicio, Length = texto.Length }, adicionar: true);
        }
        _escolhidos[numero] = escolha;
        await RecalcularAsync();
        Ir(1);
    }

    /// <summary>Volta só aquele conflito a ficar pendente.</summary>
    public async void DesfazerEscolha(int numero)
    {
        if (_arquivo is not { } arquivo || _trechos.FirstOrDefault(t => t.Numero == numero) is not { } trecho) return;
        var doc = Resultado.Editor.Document;
        using (doc.RunUpdate())
        {
            Marcar(trecho, adicionar: false);
            doc.Replace(trecho.StartOffset, trecho.Length, JuncaoConflito.TextoDaMarca(numero, arquivo.Juncao.Conflitos.Count));
        }
        _escolhidos.Remove(numero);
        _foco = numero;
        await RecalcularAsync();
        if (_analise?.Pendentes.FirstOrDefault(p => p.Numero == numero) is { Numero: > 0 } p) Realcar(p);
    }

    void Marcar(TrechoEscolhido trecho, bool adicionar)
    {
        var operacao = new OperacaoDeTrecho(_trechos, trecho, adicionar);
        operacao.Redo();
        Resultado.Editor.Document.UndoStack.Push(operacao);
    }

    /// <summary>Conflito em que Alt+1/2/3 e os botões agem: o realçado, se está na tela; senão o mais perto do meio dela.</summary>
    public void UsarNoAtual(EscolhaConflito escolha)
    {
        if (_analise is not { Pendentes.Count: > 0 } analise) return;
        var h = Resultado.AlturaLinha;
        var topo = Rolagem.Y(AnaliseConflito.PainelResultado) / h;
        var naTela = Rolagem.Viewport / h;
        var foco = analise.Pendentes.FirstOrDefault(p => p.Numero == _foco);
        if (foco.Numero > 0 && foco.LinhaResultado >= topo && foco.LinhaResultado < topo + naTela)
        {
            Usar(foco.Numero, escolha);
            return;
        }
        var meio = topo + (naTela / 2);
        Usar(analise.Pendentes.MinBy(p => Math.Abs(p.LinhaResultado - meio)).Numero, escolha);
    }

    void LigarMouse(PainelTexto painel)
    {
        painel.Editor.ContextMenuOpening += (_, e) => AoAbrirMenu(painel, e);
        if (painel.Editavel) return;
        painel.Editor.TextArea.PreviewMouseDoubleClick += (_, e) => AoDuploClique(painel, e);
    }

    /// <summary>Conflito pendente na linha do painel: o bloco do lado em cima, a linha de aviso no resultado.</summary>
    int ConflitoEm(PainelTexto painel, int linhaDocumento)
    {
        var linha = linhaDocumento - 1;
        foreach (var p in _analise?.Pendentes ?? [])
        {
            if (ReferenceEquals(painel, Resultado))
            {
                if (linha == p.LinhaResultado) return p.Numero;
                continue;
            }
            var (ini, len) = ReferenceEquals(painel, Atual) ? (p.AtualIni, p.AtualLen) : (p.EntrandoIni, p.EntrandoLen);
            if (ini >= 0 && linha >= ini && linha < ini + Math.Max(1, len)) return p.Numero;
        }
        return 0;
    }

    void AoDuploClique(PainelTexto painel, MouseButtonEventArgs e)
    {
        var numero = ConflitoEm(painel, painel.LinhaEm(e.GetPosition(painel.Vista)));
        if (numero == 0) return;
        e.Handled = true;
        Usar(numero, ReferenceEquals(painel, Atual) ? EscolhaConflito.Atual : EscolhaConflito.Entrando);
    }

    void AoAbrirMenu(PainelTexto painel, ContextMenuEventArgs e)
    {
        var ponto = Mouse.GetPosition(painel.Vista);
        var linha = painel.LinhaEm(ponto);
        var menu = new ContextMenu { PlacementTarget = painel.Editor };
        var numero = ConflitoEm(painel, linha);
        if (numero > 0) ItensDeEscolha(menu, numero);
        if (ReferenceEquals(painel, Resultado)) ItensDeDesfazerEscolha(menu, linha);
        ItensDeTexto(menu, painel);
        painel.Editor.ContextMenu = menu;
    }

    void ItensDeEscolha(ContextMenu menu, int n)
    {
        var lados = _arquivo!.Lados;
        Item(menu, lados.UsarEntrando, "Alt+1", () => Usar(n, EscolhaConflito.Entrando));
        Item(menu, lados.UsarAtual, "Alt+2", () => Usar(n, EscolhaConflito.Atual));
        menu.Items.Add(new Separator());
        Item(menu, $"Os dois: {lados.CurtoAtual}, depois {lados.CurtoEntrando}", "Alt+3", () => Usar(n, EscolhaConflito.AtualDepoisEntrando));
        Item(menu, $"Os dois: {lados.CurtoEntrando}, depois {lados.CurtoAtual}", "", () => Usar(n, EscolhaConflito.EntrandoDepoisAtual));
        menu.Items.Add(new Separator());
        Item(menu, "Próximo conflito", "F7", () => Ir(1));
        menu.Items.Add(new Separator());
    }

    void ItensDeDesfazerEscolha(ContextMenu menu, int linhaDocumento)
    {
        var doc = Resultado.Editor.Document;
        foreach (var t in _trechos.ToList())
        {
            if (doc.GetLineByOffset(t.StartOffset).LineNumber > linhaDocumento || doc.GetLineByOffset(t.EndOffset).LineNumber < linhaDocumento) continue;
            Item(menu, $"Desfazer a escolha do conflito {t.Numero}", "", () => DesfazerEscolha(t.Numero));
            menu.Items.Add(new Separator());
        }
    }

    static void ItensDeTexto(ContextMenu menu, PainelTexto painel)
    {
        Item(menu, "Copiar", "Ctrl+C", painel.Editor.Copy, !painel.Editor.TextArea.Selection.IsEmpty);
        if (!painel.Editavel) return;
        Item(menu, "Recortar", "Ctrl+X", painel.Editor.Cut, !painel.Editor.TextArea.Selection.IsEmpty);
        Item(menu, "Colar", "Ctrl+V", painel.Editor.Paste, Clipboard.ContainsText());
        Item(menu, "Desfazer", "Ctrl+Z", () => painel.Editor.Undo(), painel.Editor.CanUndo);
        Item(menu, "Refazer", "Ctrl+Y", () => painel.Editor.Redo(), painel.Editor.CanRedo);
    }

    static void Item(ContextMenu menu, string texto, string atalho, Action acao, bool habilitado = true)
    {
        var item = new MenuItem { Header = texto, InputGestureText = atalho, IsEnabled = habilitado };
        item.Click += (_, _) => acao();
        menu.Items.Add(item);
    }

    /// <summary>Pílulas no fim da linha: em cima "Usar este", no aviso do resultado os dois lados, e "Desfazer escolha" no que já foi escolhido.</summary>
    void DefinirPilulas(LadosConflito lados, AnaliseConflito analise)
    {
        var (entrando, atual, resultado) = (new Dictionary<int, IReadOnlyList<Pilula>>(), new Dictionary<int, IReadOnlyList<Pilula>>(), new Dictionary<int, IReadOnlyList<Pilula>>());
        foreach (var p in analise.Pendentes)
        {
            var n = p.Numero;
            var usarEntrando = () => Depois(() => Usar(n, EscolhaConflito.Entrando));
            var usarAtual = () => Depois(() => Usar(n, EscolhaConflito.Atual));
            if (p.EntrandoIni >= 0) entrando[p.EntrandoIni + 1] = [new($"Usar este ({lados.CurtoEntrando})", usarEntrando)];
            if (p.AtualIni >= 0) atual[p.AtualIni + 1] = [new($"Usar este ({lados.CurtoAtual})", usarAtual)];
            resultado[p.LinhaResultado + 1] = [new(lados.TituloEntrando, usarEntrando), new(lados.TituloAtual, usarAtual)];
        }
        var doc = Resultado.Editor.Document;
        foreach (var t in _trechos)
        {
            var (n, linha) = (t.Numero, doc.GetLineByOffset(t.StartOffset).LineNumber);
            resultado[linha] = [new("Desfazer escolha", () => Depois(() => DesfazerEscolha(n)), DicaDaEscolha(lados, n))];
        }
        _pilulasEntrando.Definir(entrando);
        _pilulasAtual.Definir(atual);
        _pilulasResultado.Definir(resultado);
    }

    string DicaDaEscolha(LadosConflito lados, int n)
    {
        var ficou = _escolhidos.TryGetValue(n, out var e) ? e switch
        {
            EscolhaConflito.Entrando => "ficou: " + lados.CurtoEntrando,
            EscolhaConflito.Atual => "ficou: " + lados.CurtoAtual,
            _ => "ficaram os dois",
        } : "escolha feita";
        return $"Volta o conflito {n} a ficar pendente ({ficou})";
    }

    /// <summary>Uma pílula some ao recalcular: a ação roda depois do clique terminar.</summary>
    void Depois(Action acao) => Dispatcher.BeginInvoke(() => Agir(acao));
}
