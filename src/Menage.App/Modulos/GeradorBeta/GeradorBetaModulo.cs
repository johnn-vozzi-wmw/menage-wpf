using Menage.Core.Comandos;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Comandos do Gerador de Beta: a pílula "Gerar beta" da janela principal e a paleta Ctrl+K abrem a mesma janela.</summary>
public sealed class GeradorBetaModulo(IServicoDeJanelas janelas) : IModulo
{
    public string Nome => "Gerador de Beta";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando("beta.gerar", "Gerar beta", _ =>
        {
            janelas.Abrir(JanelaDoGerador.Id);
            return Task.CompletedTask;
        }, "Beta", "Rocket"));
        catalogo.Adicionar(new Comando("beta.entregas", "Entregas do Gerador de Beta (pacotes Web e PDA)", _ =>
        {
            janelas.Abrir(JanelaDoGerador.Id, JanelaDoGerador.AbrirEntregas);
            return Task.CompletedTask;
        }, "Beta", "BoxMultiple"));
    }
}
