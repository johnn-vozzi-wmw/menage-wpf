using System.Windows;
using Menage.App.Controles;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Menage.App.Tema;

/// <summary>
/// Troca os recursos da aplicação. Com <c>animar</c>, cada janela aberta guarda a imagem de como estava e a
/// dissolve sobre as cores novas: custa um quadro de captura e depois só opacidade, sem layout por quadro,
/// e vale igual para controles nossos e do WPF UI.
/// </summary>
public sealed class AplicadorDeTemaWpf : IAplicadorDeTema
{
    ResourceDictionary? _atual;

    public void Aplicar(DefinicaoDeTema tema, bool animar)
    {
        var janelas = animar ? Application.Current.Windows.OfType<JanelaBase>().Where(j => j.IsVisible).ToList() : [];
        foreach (var janela in janelas) janela.CongelarParaTransicao();

        TrocarRecursos(tema);

        foreach (var janela in janelas) janela.DissolverTransicao();
    }

    void TrocarRecursos(DefinicaoDeTema tema)
    {
        var aplicacao = ApplicationTheme(tema);
        ApplicationThemeManager.Apply(aplicacao, WindowBackdropType.None, false);
        ApplicationAccentColorManager.Apply(
            tema[PapelDeCor.Destaque], tema[PapelDeCor.Destaque], tema[PapelDeCor.DestaqueHover], tema[PapelDeCor.DestaqueHover]);

        // Um só dicionário trocado de uma vez: uma invalidação de recursos em vez de uma por chave.
        var novo = DicionarioDoTema.Montar(tema);
        var dicionarios = Application.Current.Resources.MergedDictionaries;
        var indice = _atual is null ? -1 : dicionarios.IndexOf(_atual);
        if (indice >= 0) dicionarios[indice] = novo;
        else dicionarios.Add(novo);
        _atual = novo;
    }

    static ApplicationTheme ApplicationTheme(DefinicaoDeTema tema) =>
        tema.Escuro ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
}
