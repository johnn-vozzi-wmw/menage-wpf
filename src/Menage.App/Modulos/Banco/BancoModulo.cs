using Menage.Core;
using Menage.Core.Banco;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Microsoft.Extensions.DependencyInjection;
using Menage.App.Controles;

namespace Menage.App.Modulos.Banco;

/// <summary>Comandos do Contexto do banco: a lateral e a paleta Ctrl+K usam a mesma lista.</summary>
public sealed class BancoModulo(IServicoDeJanelas janelas, IServicoDeBanco banco, IServicoDeDialogos dialogos) : IModulo
{
    public string Nome => "Contexto do banco";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        catalogo.Adicionar(new Comando("banco.gerenciar", "Gerenciar perfis de banco (context.xml)", _ =>
        {
            janelas.Abrir(JanelaDoBanco.Id);
            return Task.CompletedTask;
        }, "Banco", "Database"));
        catalogo.Adicionar(new Comando("banco.aplicar", "Aplicar o perfil de banco escolhido", async cancelar =>
        {
            var r = await banco.AplicarAsync(cancelar);
            if (!r.EhOk) await dialogos.MostrarErroAsync(r.Erro);
        }, "Banco", "Database"));
    }
}

public static class ComposicaoDoBanco
{
    public static IServiceCollection AddBancoApp(this IServiceCollection servicos)
    {
        servicos.AddContextoBanco();
        servicos.AddModulo<BancoModulo>();
        servicos.AddTransient<BancoViewModel>();
        servicos.AddJanela<JanelaDoBanco>(JanelaDoBanco.Id);
        servicos.AddJanela<JanelaImportarBanco>(JanelaImportarBanco.Id);
        return servicos;
    }
}
