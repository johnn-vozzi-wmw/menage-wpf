# Nexus, Tomcat Express e Pull Requests

Migrados do Git Manager antigo (`Nexus.cs`, `TomcatExpress*.cs`, `PullRequests*.cs`). Motor em `src/Menage.Core/{Http,Nexus,Tomcat,PullRequests}` (sem WPF); telas em `src/Menage.App/Modulos/{Nexus,Tomcat,PullRequests}`. Nada lê arquivos do Git Manager antigo.

## Decisões

- **HTTP atrás de `IClienteHttp`** (`Core/Http`): GET em texto e download com progresso, `.parte` trocado no fim, cancelamento. Testes usam o `NexusFalso` (HttpListener real em `127.0.0.1`) ou um `ClienteHttpFalso` que responde pela URL completa. **Nenhum teste fala com Nexus, Tomcat ou GitHub reais.**
- **Segredos**: a senha do Nexus fica em `%LOCALAPPDATA%\Menage\nexus.json` só como `senhaProtegida` (DPAPI, usuário atual, `IProtetorDeSegredo`). Nunca em log, nunca em código. O token do GitHub não é gravado: vem do Git Credential Manager (`git credential fill`) a cada listagem. A credencial do Nexus só é enviada ao **mesmo host** configurado (o `downloadUrl` de outro host vai sem credencial).
- **Nexus**: busca por name (vários, vírgula; a API filtra um por vez), grupo, versão (sem `*` vira prefixo) e repositório; até 5 páginas por name por pedido + "Carregar mais"; ordena por data. Baixa jar/war/pom, com ou sem subpastas do Nexus e com a subpasta `beta_Nexus_<versões>` (versões em ordem natural: `9.060-13` depois de `9.060-3`). Falha de um arquivo não para os outros. Favoritos (conjuntos de names). "Servidor" testa listando os repositórios antes de salvar.
- **Tomcat Express**: ambientes em `tomcat-express.json`. Preparar = validar, achar `wmwvendas`/`wmwdados` no Nexus (versão vazia = a mais recente), conferir espaço, baixar os 2 `.war` ao mesmo tempo, conferir tamanho e `WEB-INF/web.xml`, ajustar portas no `server.xml` (backup `.menage.bak`, comentários e formatação intactos), mover o `.war` e a pasta atuais para `_express\backup\<data>`, limpar `work\Catalina` e instalar. **Erro na troca devolve tudo ao lugar**; cancelar antes da troca não altera nada. Subir/Parar: `catalina.bat run` oculto com o `JAVA_HOME` do `setenv.bat`; confere Java, portas (diz quem usa), `.war` e linhas inválidas do `wmw_database_config.bat`; Parar manda `SHUTDOWN` e, após 40 s, mata a árvore. Reabrir o Menage reencontra o Tomcat pelo PID. O log é traduzido (`InterpretadorDeLog`). **Fechar o Menage não para os Tomcats.** Perfil: Desenvolvedor e Geral (o Tester não vê).
- **Processo longo**: o `IProcessoRunner` só serve para comandos que terminam; o Tomcat usa `ILancadorDeProcessos` (`Core/Processos`), que entrega a saída linha a linha.
- **Pull Requests** (só o dono: e-mail global do git começa com `johnn.bryan@`; é conveniência, quem autoriza o push é o GitHub): lista pela API do GitHub com o token do Credential Manager ou, sem ele, pelas refs `refs/pull/*/head`; valida cada PR numa cópia temporária (`git worktree` em `%TEMP%\gpr<N>`: merge com a main + comando de validação, padrão `dotnet build Menage.sln -warnaserror`, editável na tela); mescla os validados na **main local** (`--no-ff`), recusa pasta suja e PR que mudou depois da validação, e devolve a main ao estado anterior se algo falhar. **Só envia ao GitHub com a caixa "Enviar a main ao GitHub" marcada** (desmarcada por padrão).
- **Fora de escopo, de propósito**: o antigo subia a versão, recompilava o instalador e gravava `update/versao.txt` ao publicar. No Menage isso é do `publicar.ps1`/Velopack e não faz parte da mescla. A edição do banco do Tomcat (`wmw_database_config.bat`, botão "Banco de dados...") veio na rodada de paridade: ver `docs/PARIDADE-INTEGRACOES.md`.
- Comandos no catálogo (pílula, menu e Ctrl+K leem a mesma lista): `nexus.abrir` (todos os perfis), `tomcat.abrir` (Desenvolvedor, Geral), `prs.abrir` (habilitado só para o dono).

## Testar

```bash
dotnet build Menage.sln -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~Menage.Tests.Nexus|FullyQualifiedName~Menage.Tests.Tomcat|FullyQualifiedName~Menage.Tests.PullRequests"
dotnet run --project src/Menage.App -- --galeria-nexus-tomcat-prs docs/galeria/nexus-tomcat-prs
```

Os testes de PR usam repositórios descartáveis em `%TEMP%` (com `refs/pull/1/head` criada no origin local) e o comando de validação `cmd.exe /c exit 0|1`. Os de Tomcat usam uma pasta de Tomcat de mentira em `%TEMP%` e um lançador de processo falso (nenhum Java/Tomcat é executado). A galeria usa dados de exemplo e não toca no Nexus, no GitHub nem em `C:\wmw\dev\dev1`.

## Roteiro manual (com servidor de verdade; ainda não executado)

**Nexus**
1. Abra **Nexus** (Ctrl+K). Sem configuração, o painel Servidor abre: informe endereço (raiz do Nexus 3), usuário e senha → **Testar e salvar**. Senha errada deve mostrar "recusados (401)" e não salvar nada.
2. Busque `wmwvendas, wmwdados` com versão `9.060`: aparecem as versões, mais recentes primeiro; **Carregar mais** aparece se houver mais.
3. Salve os names como favorito; clique no favorito aplica; botão direito exclui.
4. Marque 2 componentes, escolha a pasta, deixe a subpasta ligada e **Baixar marcados**: barra de progresso, pasta `beta_Nexus_<versões>` e botão **Abrir pasta**. Teste **Cancelar** no meio (não deve ficar `.parte`).

**Tomcat Express** (use um Tomcat de teste, nunca o de produção)
1. **Novo**, escolha a pasta do Tomcat (com `bin`, `conf`, `lib`, `webapps`) e porta; **Salvar**. Pasta errada mostra o que faltou. Dois ambientes na mesma pasta ou porta são recusados.
2. **Buscar versões** (opcional) e **Preparar ambiente**: duas barras de progresso, log por etapa, "Ambiente pronto para subir"; confira `webapps\*.war`, `_express\backup\<data>` e `conf\server.xml` (+ `.menage.bak`). Cancele uma vez durante o download: nada muda.
3. **Subir**: situação "subindo" → "no ar" quando as duas aplicações respondem; abra `wmwvendas`/`wmwdados` no navegador. Provoque um erro (porta ocupada, Java errado) e confira a mensagem traduzida.
4. **Parar**: encerra em segundos; feche e reabra o Menage com o Tomcat rodando: ele reaparece "no ar".

**Pull requests** (e-mail global do git começando com `johnn.bryan@`)
1. Abra **Pull requests**, confira a fonte ("API do GitHub" ou "refs de PR do git") e a pasta; **Trocar pasta** só aceita repositório git.
2. Marque um PR, **Validar marcados**: o log mostra merge e compilação; PR com conflito ou que não compila fica ✖ com o motivo.
3. **Mesclar na main** com a caixa de envio **desmarcada**: confirme no diálogo, confira `git log` (commit `Merge pull request #N`) e que o GitHub não recebeu nada. Só então, se quiser, marque o envio.

## Não verificado

Nexus, Tomcat e GitHub reais; cliques reais na interface (a galeria só renderiza); `.war` e Tomcat reais da WMW; o fluxo "subir" com `catalina.bat` de verdade (o lançador de processo real não é exercitado nos testes); API do GitHub com token real; DPAPI real (os testes usam um protetor reversível; o `ProtetorDpapi` só compila).
