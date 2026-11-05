# Instalador e atualização (Velopack)

## O que é gerado
`.\publicar.ps1 -Versao 1.0.1` faz publish self-contained (`win-x64`, arquivo único comprimido) e roda `vpk pack`.
Tudo vai para `artifacts\releases` (ignorado pelo git) ou para a pasta de `-Saida`. **Não envia nada a lugar nenhum.**
Mantenha a mesma pasta de saída entre versões: o `vpk` usa os pacotes anteriores dela para gerar o delta.

| Arquivo | Uso |
|---|---|
| `MenageApp-win-Setup.exe` | Instalador para os colegas. Cria atalhos no Menu Iniciar e na Área de Trabalho, entrada em "Aplicativos" (desinstalador). |
| `MenageApp-win-Portable.zip` | Sem instalação, sem atalhos, sem registro. Usado nos testes. |
| `MenageApp-X-full.nupkg`, `-delta.nupkg`, `releases.win.json` | O que o app baixa ao atualizar. É isso que vai para a origem da atualização. |

- Id do pacote: `MenageApp` (instala em `%LOCALAPPDATA%\MenageApp`). **Não** usar `Menage`: o desinstalador apaga a pasta de instalação inteira e `%LOCALAPPDATA%\Menage` guarda os dados do usuário. Os atalhos se chamam "Menage".
- Nada é compartilhado com o Git Manager antigo (`C:\wmw\GMV*`, `C:\wmw\Git Manager`).
- Não está assinado (sem certificado): o Windows SmartScreen pode avisar na primeira execução do Setup.

## Como o app atualiza
`IServicoDeAtualizacao` (Core, `ServicoDeAtualizacaoVelopack`) é registrado por injeção de dependência. Ao abrir, o app
verifica em segundo plano; se houver versão nova, mostra **Instalar agora / Depois / Ignorar esta versão**.
"Instalar" baixa com barra de progresso e reinicia. "Ignorar" grava a versão em `%LOCALAPPDATA%\Menage\atualizacao.json`.
Falhas (rede, origem fora do ar) vão para `%LOCALAPPDATA%\Menage\atualizacao.log` e nunca atrapalham o uso. Fora do instalador (F5 no VS) não há verificação.

**Origem configurável** (`origem` em `atualizacao.json`, ou a variável `MENAGE_ORIGEM_ATUALIZACAO`, que tem prioridade):
- pasta local ou de rede: `\servidor\menage\releases`
- URL http(s) que sirva os arquivos acima
- `github:johnn-vozzi-wmw/menage-wpf` (usa a variável `MENAGE_TOKEN_ATUALIZACAO` como token; o token nunca fica no JSON)

Sem origem configurada, não verifica nada.

## Repositório PRIVADO: opções
O Velopack lê Releases do GitHub pela API e, em repositório privado, exige um token em cada máquina. Não testei com token real (não pedi nem gravei nenhum); o comportamento do download dos arquivos com token deve ser confirmado antes de adotar a opção A.

**A) GitHub Releases + token somente-leitura em cada PC** (fine-grained PAT, só `Contents: read` neste repositório)
- Prós: nenhuma infraestrutura; versões e histórico já ficam no GitHub.
- Contras: um segredo em cada máquina de colega (guardar com DPAPI em `%LOCALAPPDATA%\Menage`, ainda assim extraível pelo próprio usuário); PAT expira e precisa de rotação e redistribuição; quem sair da empresa leva o token; se vazar, lê o código-fonte inteiro (o repositório é o código, não só os binários).

**B) Pasta de rede da WMW (ou URL interna) com os arquivos de `artifacts\releases`** — **recomendada**
- Prós: sem token e sem segredo no cliente; acesso controlado pelas permissões da própria rede; o código-fonte continua privado; funciona offline na rede interna; testada de ponta a ponta (ver abaixo) com pasta local.
- Contras: quem publica precisa copiar a pasta para o compartilhamento (um `robocopy` no fim do `publicar.ps1`, quando você pedir); só atualiza com acesso à rede/VPN.

**C) Repositório público só de binários** (ex.: `menage-releases`) — sem token, mas qualquer pessoa baixa o app. Só se a WMW aceitar isso.

**Recomendação: B.** Se o compartilhamento não for viável, A com PAT fine-grained somente-leitura e validade curta.

## Teste de ponta a ponta realizado (local, nada instalado no sistema)
1. `publicar.ps1 -Versao 1.0.0` e `-Versao 1.0.1` para a mesma pasta em `%TEMP%` (gerou o delta 1.0.0 → 1.0.1).
2. Zip portátil da 1.0.0 extraído em `%TEMP%\menage-e2e\app`; `LOCALAPPDATA` do processo de teste apontado para a pasta temporária.
3. Origem apontada para a pasta de releases pela variável de ambiente; aviso apareceu; "Instalar agora" foi acionado por automação de UI.
4. O app baixou, reiniciou e a janela passou a mostrar **Versão 1.0.1** (`current\sq.version` = 1.0.1). Capturas em `docs/galeria/instalador/`.

**Não verificado:** executar o `Setup.exe` (criaria atalhos e entrada de desinstalação reais na máquina), o desinstalador, atalhos e ícone, origem `github:` com token, origem em compartilhamento de rede.

## Roteiro de teste manual (numa máquina de teste/VM)
1. `.\publicar.ps1 -Versao 1.0.0`; rode `artifacts\releases\MenageApp-win-Setup.exe`: confira atalhos "Menage" no Menu Iniciar e na Área de Trabalho.
2. Crie `%LOCALAPPDATA%\Menage\atualizacao.json` com `{ "origem": "C:\pasta\releases" }`.
3. `.\publicar.ps1 -Versao 1.0.1 -Saida C:\pasta\releases` (a pasta já com a 1.0.0) e abra o Menage: deve aparecer o aviso.
4. Teste "Depois" (reabre o aviso na próxima abertura), "Ignorar" (não avisa mais essa versão) e "Instalar agora" (progresso, reinicia na 1.0.1).
5. Desinstale por Configurações > Aplicativos: some o `%LOCALAPPDATA%\MenageApp` e os atalhos; `%LOCALAPPDATA%\Menage` permanece.

## Sobre / Atualizações (gestão dentro do app)
Comando **Sobre / Atualizações** (menu Mais e paleta Ctrl+K, id `app.sobre`). Mostra versão atual, verifica o feed, lista as versões com notas, atualiza (barra de progresso e reinício), volta para a versão anterior (com confirmação na tela), troca a origem e o canal estável/beta, e informa se há Git Manager antigo em `C:\wmw\GMV*` / `Git Manager*` (**só leitura**: nada é alterado, fechado ou desinstalado).

- Motor em `Menage.Core/Atualizacao`: `ServicoDeInstalacoes` (regras) sobre `IGerenteDeInstalacao` (Velopack real em `GerenteVelopack`; feed falso nos testes). O aviso na abertura (`ServicoDeAtualizacaoVelopack`) continua igual e usa a mesma origem e canal.
- `atualizacao.json` ganhou `canal` (`"beta"` ou ausente = estável). Arquivos antigos continuam válidos. A variável `MENAGE_ORIGEM_ATUALIZACAO` segue com prioridade (a tela avisa).
- **Canal beta:** publique com `.\publicar.ps1 -Versao 1.1.0-beta.1 -Canal beta` (gera `releases.beta.json`). Quem marca "canal beta" só enxerga esse feed; ao voltar para estável, enxerga o feed estável (o Velopack não rebaixa sozinho: use "Voltar" se quiser).
- **Voltar de versão:** o Velopack não tem rollback nativo. O Menage baixa o pacote *completo* da maior versão do feed menor que a atual (precisa estar na origem; **não apague os `.nupkg` antigos da pasta de rede**) e aplica como recuo.
- **Notas da versão:** `.\publicar.ps1 -Versao 1.0.2 -Notas notas.md` (markdown); aparecem ao selecionar a versão na lista.

### publicar.ps1 e pasta de rede
`-PastaDeRede \servidor\menage\releases` copia a saída (robocopy `/E`, nunca `/MIR`, não apaga nada na rede) **só se você passar o parâmetro**, e pede confirmação (`-WhatIf` ensaia). Sem o parâmetro nada sai da máquina. Aceita versão com sufixo (`1.1.0-beta.1`).

### Roteiro de teste manual (máquina de teste/VM)
1. Gere 1.0.0 e 1.0.1 (e uma `1.1.0-beta.1 -Canal beta`) numa pasta local; instale a 1.0.0 com o `Setup.exe`.
2. Abra Menage > Sobre / Atualizações: versão 1.0.0, "Instalado pelo instalador". Informe a pasta em Origem > Salvar origem: deve listar as versões e oferecer "Atualizar para 1.0.1".
3. Atualizar: barra de progresso, reinício e versão 1.0.1. Abra de novo, "Voltar para 1.0.0": confirmar na tela, reinicia na 1.0.0.
4. Marque "canal beta": a lista passa a mostrar o feed beta; desmarque e confira o estável.
5. Origem inexistente: mensagem de erro em vermelho, o app continua normal. Sem origem: orientação para informar.
6. Com `C:\wmw\GMV*` presente: o card "Convivência" lista as pastas; confira que nada nelas mudou.

### Não verificado (módulo Instalações)
Download/aplicação/rollback reais com o Velopack (só o feed falso nos testes e a tela na galeria), a montagem de `UpdateInfo` para recuo (`GerenteVelopack`), canal beta de ponta a ponta, origem `github:` com token e pasta de rede de verdade, `-PastaDeRede` com robocopy real, `Setup.exe`. Galeria: `docs/galeria/instalacoes-versao` (`--galeria-instalacoes <pasta>`).
