using System.IO;
using System.Windows;
using Menage.Core.Atualizacao;

namespace Menage.App;

public sealed class CoordenadorDeAtualizacao(IServicoDeAtualizacao servico)
{
    public async Task VerificarEmSegundoPlanoAsync(Window dono)
    {
        try
        {
            if (!servico.PodeAtualizar) return;
            var nova = await Task.Run(() => servico.VerificarAsync());
            if (nova is null || nova.Versao == servico.VersaoIgnorada) return;

            var janela = new JanelaDeAtualizacao(nova.Versao) { Owner = dono };
            janela.Show();
            await EsperarEscolhaAsync(janela);
            switch (janela.Escolha)
            {
                case EscolhaDeAtualizacao.Ignorar: servico.Ignorar(nova); break;
                case EscolhaDeAtualizacao.Instalar: await BaixarEReiniciarAsync(nova, janela); break;
            }
        }
        catch (Exception ex)
        {
            // Atualização é conveniência: falha de rede ou de origem nunca deve atrapalhar o uso do app.
            RegistrarFalha(ex);
        }
    }

    private async Task BaixarEReiniciarAsync(AtualizacaoDisponivel nova, JanelaDeAtualizacao janela)
    {
        await servico.BaixarAsync(nova, new Progress<int>(janela.MostrarProgresso));
        servico.AplicarEReiniciar(nova);
    }

    private static void RegistrarFalha(Exception ex)
    {
        var pasta = Path.GetDirectoryName(OpcoesDeAtualizacao.CaminhoPadrao)!;
        Directory.CreateDirectory(pasta);
        File.AppendAllText(Path.Combine(pasta, "atualizacao.log"), $"{DateTime.Now:s} {ex}{Environment.NewLine}");
    }

    private static Task EsperarEscolhaAsync(JanelaDeAtualizacao janela)
    {
        var fim = new TaskCompletionSource();
        janela.Closed += (_, _) => fim.TrySetResult();
        janela.EscolhaFeita += () => fim.TrySetResult();
        return fim.Task;
    }
}
