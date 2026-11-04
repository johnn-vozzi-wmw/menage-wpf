using Menage.Core.Comandos;

namespace Menage.App.Tema;

/// <summary>Comandos de tema no catálogo: aparecem na paleta Ctrl+K e em qualquer menu que leia o catálogo.</summary>
public sealed class AparenciaModulo(IServicoDeTema tema, ICatalogoDeTemas temas) : IModulo
{
    public string Nome => "Aparência";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando("tema.alternar", "Alternar tema claro/escuro", Executar(tema.AlternarPar),
            Grupo: "Tema", Icone: "DarkTheme", Atalho: "Ctrl+T"));

        foreach (var item in temas.Todos)
        {
            catalogo.Adicionar(new Comando($"tema.{item.Id}", $"Tema: {item.Nome}", Executar(() => tema.Trocar(item.Id)),
                Grupo: "Tema", Icone: "Color"));
        }
    }

    static Func<CancellationToken, Task> Executar(Action acao) => _ =>
    {
        acao();
        return Task.CompletedTask;
    };
}
