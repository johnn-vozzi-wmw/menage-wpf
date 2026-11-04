using Menage.Core;
using Menage.Core.Diff;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Menage.App.Modulos.Diff;

/// <summary>Pergunta qual lado fica num arquivo que não dá para juntar como texto. Devolve nulo para decidir depois.</summary>
public interface IPerguntaDeLado
{
    Task<LadoEscolhido?> PerguntarAsync(string titulo, string mensagem, (string Texto, LadoEscolhido Lado) primeira, (string Texto, LadoEscolhido Lado) segunda);
}

/// <summary>Diálogos do módulo com o MessageBox do WPF UI, até a janela principal registrar a implementação geral.</summary>
public sealed class DialogosDiff : IServicoDeDialogos, IPerguntaDeLado
{
    public async Task<bool> ConfirmarAsync(Previa previa)
    {
        var caixa = new MessageBox
        {
            Title = previa.Titulo,
            Content = previa.Mensagem,
            PrimaryButtonText = "Sim",
            CloseButtonText = "Não",
        };
        return await caixa.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public Task InformarAsync(string titulo, string mensagem) =>
        new MessageBox { Title = titulo, Content = mensagem, CloseButtonText = "OK" }.ShowDialogAsync();

    public Task MostrarErroAsync(Erro erro) => InformarAsync(erro.Causa, erro.ToString());

    public Task MostrarResumoAsync(IResumoDoLote resumo) => Task.CompletedTask;

    public async Task<LadoEscolhido?> PerguntarAsync(string titulo, string mensagem, (string Texto, LadoEscolhido Lado) primeira, (string Texto, LadoEscolhido Lado) segunda)
    {
        var caixa = new MessageBox
        {
            Title = titulo,
            Content = mensagem,
            PrimaryButtonText = primeira.Texto,
            SecondaryButtonText = segunda.Texto,
            CloseButtonText = "Decidir depois",
            IsSecondaryButtonEnabled = true,
        };
        return await caixa.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => primeira.Lado,
            MessageBoxResult.Secondary => segunda.Lado,
            _ => null,
        };
    }
}
