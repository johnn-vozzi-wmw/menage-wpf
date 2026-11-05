using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.Core.Banco;

public static class BancoExtensions
{
    /// <summary>
    /// Contexto do banco: cadastro de perfis e serviço usado pela barra lateral. O App (galerias) pode registrar antes outro
    /// <see cref="IServicoDeBanco"/>, que vence por ser <c>TryAdd</c>.
    /// </summary>
    public static IServiceCollection AddContextoBanco(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IArmazenamento<ConfiguracaoDoBanco>>(sp =>
            new Armazenamento<ConfiguracaoDoBanco>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDoBanco.NomeDoArquivo, padrao: ConfiguracaoDoBanco.Padrao));
        servicos.TryAddSingleton<RepositorioDeBanco>();
        servicos.TryAddSingleton<ServicoDeBancoContexto>();
        servicos.TryAddSingleton<IServicoDeBanco>(sp => sp.GetRequiredService<ServicoDeBancoContexto>());
        return servicos;
    }
}
