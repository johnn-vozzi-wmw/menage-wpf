using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Perfis;

namespace Menage.App.Galeria;

/// <summary>Comandos de mentira, só na galeria: mostram acento, atalho, grupo e bloqueio por perfil na paleta.</summary>
public sealed class ComandosDeExemploModulo(IServicoDeDialogos dialogos) : IModulo
{
    public string Nome => "Exemplo";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        Task Avisar(string texto) => dialogos.InformarAsync("Comando de exemplo", texto);

        catalogo.Adicionar(new Comando("exemplo.sincronizar", "Sincronização de projetos", _ => Avisar("Sincronização"),
            Grupo: "Exemplo", Icone: "ArrowSync", Atalho: "Ctrl+Shift+S"));
        catalogo.Adicionar(new Comando("exemplo.nova-branch", "Nova branch", _ => Avisar("Nova branch"), Grupo: "Exemplo", Icone: "BranchFork"));
        catalogo.Adicionar(new Comando("exemplo.renomear", "Renomear branch", _ => Avisar("Renomear"), Grupo: "Exemplo", Icone: "Rename"));
        catalogo.Adicionar(new Comando("exemplo.desfazer", "Desfazer último commit", _ => Avisar("Desfazer"),
            Grupo: "Exemplo", Icone: "ArrowUndo", Habilitado: () => false));
        catalogo.Adicionar(new Comando("exemplo.diario", "Diário de trabalho", _ => Avisar("Diário"), Grupo: "Exemplo", Icone: "Notebook",
            Perfis: ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor)));
    }
}
