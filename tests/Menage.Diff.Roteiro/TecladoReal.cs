using System.Windows;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Texto;
using Menage.Core.Diff;

namespace Menage.Diff.Roteiro;

/// <summary>Cliques, seleção e teclas de verdade na janela de teste: dobra, F7, Alt+setas, PageUp/Down, Home/End, Ctrl+B, F5 e cópia.</summary>
public sealed class TecladoReal(JanelaDiff janela)
{
    const byte Shift = 0x10;
    const byte Control = 0x11;
    const byte Alt = 0x12;
    const byte PageUp = 0x21;
    const byte PageDown = 0x22;
    const byte End = 0x23;
    const byte Home = 0x24;
    const byte SetaBaixo = 0x28;
    const byte TeclaB = 0x42;
    const byte TeclaC = 0x43;
    const byte F5 = 0x74;
    const byte F7 = 0x76;

    readonly PainelDiff _p = janela.PainelDoDiff;
    readonly Dictionary<string, object?> _r = [];

    public async Task<object> RodarAsync()
    {
        await janela.RecarregarAsync("grande.java");
        await Execucao.Esperar(800);
        _p.DefinirChaves(recolher: true, ignorar: true);
        _p.Rolagem.IrParaImediato(0, 0);
        await Execucao.Esperar(300);
        await Clicar(_p.Direito, 3);
        if (!Tela.EmPrimeiroPlano(janela))
        {
            _r["abortado"] = "a janela de teste não ficou em primeiro plano: nenhuma tecla foi enviada";
            return _r;
        }
        await Teclas();
        await Selecao(_p.Esquerdo, "selecao_esquerda");
        await Selecao(_p.Direito, "selecao_direita");
        await Dobra();
        await Lista();
        return _r;
    }

    async Task Teclas()
    {
        _p.Rolagem.IrParaImediato(0, 0);
        _r["pageDown_anda"] = await Anda(() => Tela.Teclar(PageDown));
        _r["pageUp_volta"] = await Anda(() => Tela.Teclar(PageUp));
        _r["seta_baixo_anda"] = await Anda(() => Tela.Teclar(SetaBaixo));
        await Tecla(End);
        _r["end_vai_ao_fim"] = Math.Abs(_p.Rolagem.U - _p.Rolagem.Mapa!.Maximo) < 1;
        await Tecla(Home);
        _r["home_vai_ao_comeco"] = _p.Rolagem.U < 1;
        await Tecla(F7);
        var primeira = _p.TextoDaFicha;
        await Tecla(F7);
        var segunda = _p.TextoDaFicha;
        Tela.Teclar(F7, Shift);
        await Execucao.Esperar(350);
        var voltou = _p.TextoDaFicha;
        Tela.Teclar(SetaBaixo, Alt);
        await Execucao.Esperar(350);
        _r["ficha_f7"] = new[] { primeira, segunda, voltou, _p.TextoDaFicha };
    }

    async Task<double> Anda(Action tecla)
    {
        var antes = _p.Rolagem.U;
        tecla();
        await Execucao.Esperar(350);
        return Math.Round(_p.Rolagem.U - antes);
    }

    static async Task Tecla(byte tecla)
    {
        Tela.Teclar(tecla);
        await Execucao.Esperar(1100);
    }

    async Task Clicar(PainelTexto painel, int linhaNaTela)
    {
        var p = painel.Vista.PointToScreen(new Point(120, (linhaNaTela + 0.5) * painel.AlturaLinha));
        if (!Tela.PontoEstaNaJanela(janela, p)) return;
        Tela.MoverMouse(p);
        await Execucao.Esperar(60);
        Tela.Apertar();
        await Execucao.Esperar(40);
        Tela.Soltar();
        await Execucao.Esperar(250);
    }

    /// <summary>Seleciona 3 linhas arrastando o mouse e copia. A área de transferência do usuário é guardada e devolvida.</summary>
    async Task Selecao(PainelTexto painel, string nome)
    {
        _p.Rolagem.IrParaImediato(0, 0);
        await Execucao.Esperar(200);
        var h = painel.AlturaLinha;
        var (de, ate) = (painel.Vista.PointToScreen(new Point(60, 8.5 * h)), painel.Vista.PointToScreen(new Point(200, 11.5 * h)));
        if (!Tela.PontoEstaNaJanela(janela, de)) return;
        Tela.MoverMouse(de);
        await Execucao.Esperar(60);
        Tela.Apertar();
        await Execucao.Esperar(40);
        for (var f = 0.1; f <= 1; f += 0.1)
        {
            Tela.MoverMouse(new Point(de.X + ((ate.X - de.X) * f), de.Y + ((ate.Y - de.Y) * f)));
            await Execucao.Esperar(15);
        }
        Tela.Soltar();
        await Execucao.Esperar(150);
        var selecionado = painel.Editor.SelectedText;
        _r[nome] = new { caracteres = selecionado.Length, linhas = selecionado.Split('\n').Length, copiou = await CopiarAsync(painel, selecionado), rolagemDosDoisMesmoX = _p.Esquerdo.Vista.HorizontalOffset == _p.Direito.Vista.HorizontalOffset };
    }

    /// <summary>Ctrl+C de verdade, mas cancelado no evento de cópia: a área de transferência do usuário não é tocada.</summary>
    static async Task<bool> CopiarAsync(PainelTexto painel, string esperado)
    {
        string? copiado = null;
        void AoCopiar(object sender, DataObjectCopyingEventArgs e)
        {
            copiado = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            e.CancelCommand();
        }
        DataObject.AddCopyingHandler(painel.Editor.TextArea, AoCopiar);
        Tela.Teclar(TeclaC, Control);
        await Execucao.Esperar(200);
        DataObject.RemoveCopyingHandler(painel.Editor.TextArea, AoCopiar);
        return copiado?.Replace("\r\n", "\n", StringComparison.Ordinal) == esperado.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    async Task Dobra()
    {
        _p.Rolagem.IrParaImediato(0, 0);
        await Execucao.Esperar(200);
        var linha = _p.Vista!.Dir.ToList().FindIndex(l => l.Tipo == TipoLinha.Dobra);
        var linhasAntes = _p.Vista.Dir.Count;
        _p.Direito.Editor.TextArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(_p.Direito.Editor.TextArea, 0, _p.Direito.Editor.Document.GetLineByNumber(linha + 2).EndOffset);
        _r["copia_com_dobra_traz_as_linhas_reais"] = !_p.Direito.TextoParaCopiar().Contains("clique para mostrar", StringComparison.Ordinal);
        _p.Direito.Editor.TextArea.ClearSelection();
        await Clicar(_p.Direito, linha);
        await Execucao.Esperar(300);
        _r["clique_na_dobra"] = new { antes = linhasAntes, depois = _p.Vista!.Dir.Count, igualNosDois = _p.Vista.Dir.Count(l => l.Tipo == TipoLinha.Dobra) == _p.Vista.Esq.Count(l => l.Tipo == TipoLinha.Dobra) };
    }

    async Task Lista()
    {
        var total = janela.ListaDeArquivos.Total;
        Tela.Teclar(TeclaB, Control);
        await Execucao.Esperar(450);
        var recolhida = !janela.Animador.Expandida;
        Tela.Teclar(TeclaB, Control);
        await Execucao.Esperar(450);
        Tela.Teclar(F5);
        await Execucao.Esperar(700);
        _r["ctrl_b"] = new { recolhida, expandiuDeNovo = janela.Animador.Expandida };
        _r["f5_mantem_lista"] = janela.ListaDeArquivos.Total == total && janela.ListaDeArquivos.Selecionado?.Caminho == "grande.java";
    }
}
