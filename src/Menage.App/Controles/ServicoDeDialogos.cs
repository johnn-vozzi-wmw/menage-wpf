using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>
/// Mensagens do app sobre o <see cref="ContentDialog"/> do WPF UI, na janela ativa. Pode ser chamado de
/// qualquer thread. O botão principal é o padrão (Enter) e recebe o foco; Esc equivale a cancelar.
/// </summary>
public sealed class ServicoDeDialogos : IServicoDeDialogos
{
    public async Task<bool> ConfirmarAsync(Previa previa)
    {
        var conteudo = () => new ConteudoDeMensagem(TipoDeSelo.Pergunta, previa.Titulo, previa.Mensagem, detalhes: previa.Detalhes);
        return await MostrarAsync(conteudo, "Confirmar", "Cancelar") == ContentDialogResult.Primary;
    }

    public Task InformarAsync(string titulo, string mensagem) =>
        MostrarAsync(() => new ConteudoDeMensagem(TipoDeSelo.Informacao, titulo, mensagem), "OK");

    public Task MostrarErroAsync(Erro erro) =>
        MostrarAsync(() => new ConteudoDeMensagem(TipoDeSelo.Erro, erro.Causa, erro.Explicacao, erro.AcaoSugerida), "OK");

    public Task MostrarResumoAsync(IResumoDoLote resumo)
    {
        var tipo = resumo.Erros > 0 ? TipoDeSelo.Erro : resumo.Avisos > 0 || resumo.Interrompido ? TipoDeSelo.Aviso : TipoDeSelo.Informacao;
        var fim = resumo.Interrompido ? " (interrompido)" : string.Empty;
        var texto = $"{resumo.Oks} ok, {resumo.Avisos} com aviso, {resumo.Erros} com erro{fim}.";
        var linhas = resumo.Linhas.Where(l => l.Situacao != Situacao.Ok).Select(l => $"{l.Projeto}: {l.Mensagem}").ToList();
        return MostrarAsync(() => new ConteudoDeMensagem(tipo, resumo.Operacao, texto, detalhes: linhas), "OK");
    }

    static async Task<ContentDialogResult> MostrarAsync(Func<ConteudoDeMensagem> conteudo, string principal, string? fechar = null)
    {
        var tela = Application.Current.Dispatcher;
        return await await tela.InvokeAsync(() =>
        {
            var janela = JanelaAtiva() ?? throw new InvalidOperationException("Não há janela aberta para mostrar a mensagem.");
            return janela.MostrarDialogoAsync(host => Montar(host, conteudo(), principal, fechar));
        });
    }

    static JanelaBase? JanelaAtiva()
    {
        var janelas = Application.Current.Windows.OfType<JanelaBase>().Where(j => j.IsVisible).ToList();
        return janelas.Find(j => j.IsActive) ?? Application.Current.MainWindow as JanelaBase ?? janelas.FirstOrDefault();
    }

    static ContentDialog Montar(ContentDialogHost host, ConteudoDeMensagem conteudo, string principal, string? fechar)
    {
        var dialogo = new ContentDialog(host)
        {
            Content = conteudo,
            // Com um botão só, ele é o de fechar: o do WPF UI aparece sempre, e vazio ficaria um botão em branco.
            PrimaryButtonText = fechar is null ? string.Empty : principal,
            CloseButtonText = fechar ?? principal,
            DefaultButton = fechar is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonAppearance = fechar is null ? ControlAppearance.Primary : ControlAppearance.Secondary,
        };
        dialogo.Opened += (d, _) => d.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FocarPrincipal(d));
        return dialogo;
    }

    static void FocarPrincipal(ContentDialog dialogo)
    {
        var botao = Visuais.Descendentes(dialogo).OfType<System.Windows.Controls.Button>().FirstOrDefault(b => b.IsDefault);
        if (botao is not null) Keyboard.Focus(botao);
    }
}
