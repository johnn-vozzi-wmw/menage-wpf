using System.Windows;
using Menage.App.Controles;
using Menage.App.Modulos.HistoricoReleases;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>
/// Comandos de Personalização e do Histórico de releases. Pílulas, menus e paleta Ctrl+K leem o mesmo catálogo;
/// "Abrir a paleta" também está aqui para o atalho dela ser configurável como os outros.
/// </summary>
public sealed class PersonalizacaoModulo(IServicoDeJanelas janelas, ServicoDeAparencia aparencia) : IModulo
{
    public string Nome => "Personalização";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(Comando("paleta.abrir", "Abrir a paleta de comandos", "Geral", "Search", () =>
            Application.Current.Windows.OfType<JanelaBase>().FirstOrDefault(j => j.IsActive)?.AbrirPaleta(), "Ctrl+K"));
        catalogo.Adicionar(Comando("personalizacao.abrir", "Personalização (tema, destaque, fonte, atalhos)", "Aparência", "Color",
            () => janelas.Abrir(JanelaDePersonalizacao.Id), "Ctrl+,"));
        catalogo.Adicionar(Comando("aparencia.sistema", "Tema: seguir o Windows (claro ou escuro)", "Tema", "Desktop", () => Modo(ModoDeTema.Sistema)));
        catalogo.Adicionar(Comando("aparencia.claro", "Tema: sempre claro", "Tema", "WeatherSunny", () => Modo(ModoDeTema.Claro)));
        catalogo.Adicionar(Comando("aparencia.escuro", "Tema: sempre escuro", "Tema", "WeatherMoon", () => Modo(ModoDeTema.Escuro)));
        catalogo.Adicionar(Comando("aparencia.reduzir-movimento", "Alternar \"reduzir movimento\"", "Aparência", "Flash",
            () => aparencia.Alterar(p => p with { ReduzirMovimento = !p.ReduzirMovimento })));
        catalogo.Adicionar(Comando("mais.historico", "Histórico de releases (betas e tags)", "Mais", "History",
            () => janelas.Abrir(JanelaDeReleases.Id)));
    }

    void Modo(ModoDeTema modo) => aparencia.Alterar(p => p with { Modo = modo });

    static Comando Comando(string id, string texto, string grupo, string icone, Action acao, string? atalho = null) =>
        new(id, texto, _ =>
        {
            acao();
            return Task.CompletedTask;
        }, grupo, icone, atalho);
}
