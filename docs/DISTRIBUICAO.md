# Distribuição do Menage para amigos

## Modelo adotado: repositório público só de distribuição
- **Código** fica em `johnn-vozzi-wmw/menage-wpf` (**privado**). Recebe só commits e tags `vX.Y.Z`.
- **Instaladores** ficam em `johnn-vozzi-wmw/menage-releases` (**público**, sem código-fonte): cada Release tem `MenageApp-win-Setup.exe`, os `.nupkg` e `releases.*.json`.
- O app lê o feed por `github:johnn-vozzi-wmw/menage-releases`, sem token. Nada de segredo no app nem no repositório.
- O nome `menage-releases` é uma sugestão: se criar com outro nome, troque em `distribuicao.json`.

## Como a origem é configurável (para mudar de modelo no futuro)
A origem das atualizações é resolvida nesta ordem (`OpcoesDeAtualizacao.Carregar`):
1. variável de ambiente `MENAGE_ORIGEM_ATUALIZACAO`;
2. `%LOCALAPPDATA%\Menage\atualizacao.json` (campo `Origem`, o usuário muda no app);
3. `distribuicao.json` que vai junto do exe (vem do `distribuicao.json` da raiz do repositório).

Formatos aceitos em qualquer um deles:
| Valor | Modelo |
|---|---|
| `github:dono/repo` | repositório público (B ou C); se privado, token em `MENAGE_TOKEN_ATUALIZACAO` (modelo A) |
| `https://servidor/pasta` | URL própria (D) |
| `\servidor\menage\releases` ou `C:\pasta` | pasta de rede/local (D) |

Trocar de modelo = editar `distribuicao.json` (`origemAtualizacao` e `repositorioDeDistribuicao`) e lançar uma versão nova. Quem já instalou continua apontando para a origem antiga até receber essa versão, então **mantenha o feed antigo vivo por uns tempos**. Pontual: quem quiser pode sobrescrever pela variável de ambiente ou pelo `atualizacao.json`.

## Passos manuais (uma vez só, você no site do GitHub)
1. github.com → **+** (canto superior direito) → **New repository**. Nome `menage-releases`, **Public**, marque **Add a README file** e clique **Create repository**. Não coloque código nele.
2. Autenticar o `gh` (para publicar por script): instale o GitHub CLI e rode `gh auth login` → GitHub.com → HTTPS → login pelo navegador.
3. Primeira publicação: `.\lancar-versao.ps1 -Versao X.Y.Z` (ele pergunta antes de enviar tag e criar o Release). O Release é criado em `menage-releases` por `--repo`; a tag vai só ao repositório de código. Sem `gh` autenticado o script mostra os passos manuais (Releases → **Draft a new release** → anexar Setup, `.nupkg`, `RELEASES`, `*.json`).
4. Conferir: abra `https://github.com/johnn-vozzi-wmw/menage-releases/releases` numa janela anônima; o Setup deve baixar sem login.

Lembrete: tudo em `menage-releases` é público, inclusive as notas de versão. Não escreva nelas nada interno da WMW.

## Teste local do feed (sem publicar)
Gere os pacotes com `publicar.ps1 -Versao X.Y.Z -Saida C:\mt\feedteste` e aponte um Menage instalado para a pasta com `MENAGE_ORIGEM_ATUALIZACAO=C:\mt\feedteste`.
